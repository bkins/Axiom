using System.Net.Http.Json;
using System.Text.Json;
using Axiom.Core;

namespace Axiom.Infrastructure;

/// <summary>Session-only credential; no persistence, redirects, retries or background upload.</summary>
public sealed class CpDocumentAnalysisClient : IDisposable
{
    private readonly HttpClient _http;
    public string EndpointIdentity { get; }

    public CpDocumentAnalysisClient(Uri endpoint, string credential, HttpMessageHandler? handler = null)
    {
        if (!endpoint.IsAbsoluteUri || endpoint.UserInfo.Length != 0 || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0
         || (endpoint.Scheme != "https" && !(endpoint.Scheme == "http" && endpoint.IsLoopback)))
            throw new ArgumentException("Choose an HTTPS CP endpoint, or an explicit loopback Development HTTP endpoint.");
        if (credential.Length is < 32 or > 256 || credential.Any(char.IsControl))
            throw new ArgumentException("Enter a dedicated document-analysis credential of 32–256 characters.");
        _http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = new Uri(endpoint.AbsoluteUri.TrimEnd('/') + "/")
          , Timeout = TimeSpan.FromSeconds(125)
        };
        _http.DefaultRequestHeaders.Add("X-Document-Analysis-Key", credential);
        EndpointIdentity = WorkspaceLinkIndexService.Hash(_http.BaseAddress.AbsoluteUri);
    }

    public async Task<AnalysisCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/document-analysis/capabilities");
        using var json = await SendAsync(request, cancellationToken);
        var root = json.RootElement;
        if (root.GetProperty("protocolVersion").GetInt32() != 1) throw new InvalidOperationException("CP document-analysis protocol is unsupported.");
        return new(root.GetProperty("insightsConfigured").GetBoolean(), root.GetProperty("embeddingsConfigured").GetBoolean()
                 , root.GetProperty("simulated").GetBoolean(), Optional(root, "insightsModel"), Optional(root, "embeddingsModel")
                 , Optional(root, "availability") ?? "unknown", Optional(root, "embeddingsRevision"), Optional(root, "insightsRevision"));
    }

    public async Task<AnalysisInsight> GetInsightsAsync(IReadOnlyList<AnalysisSnapshot> snapshots, string? question, CancellationToken cancellationToken = default)
    {
        var sources = Validate(snapshots, question);
        var requestId = Guid.NewGuid().ToString("N");
        using var request = Request("insights", sources, question, requestId);
        using var json = await SendAsync(request, cancellationToken);
        var root = json.RootElement;
        Verify(root, sources, requestId);
        var text = root.GetProperty("text").GetString();
        if (string.IsNullOrWhiteSpace(text) || text.Length > 200_000) throw new InvalidOperationException("CP returned an invalid insight.");
        return new(text, root.GetProperty("provider").GetString()!, Optional(root, "model"), Optional(root, "provenance") ?? "unknown", DateTimeOffset.UtcNow, sources);
    }

    public async Task<AnalysisEmbeddings> GetEmbeddingsAsync(IReadOnlyList<AnalysisSnapshot> snapshots, CancellationToken cancellationToken = default)
    {
        var sources = Validate(snapshots, null);
        var requestId = Guid.NewGuid().ToString("N");
        using var request = Request("embeddings", sources, null, requestId);
        using var json = await SendAsync(request, cancellationToken);
        var root = json.RootElement;
        Verify(root, sources, requestId);
        var vectors = root.GetProperty("vectors").Deserialize<float[][]>()!;
        if (vectors is null || vectors.Length != sources.Length || vectors.Any(vector => vector is null || vector.Length is < 1 or > 8192
                                                                                                       || vector.Any(value => !float.IsFinite(value)))
                                                               || vectors.Any(vector => vector.Length != vectors[0].Length))
            throw new InvalidOperationException("CP returned invalid embedding vectors.");
        return new(vectors, root.GetProperty("provider").GetString()!, root.GetProperty("model").GetString()!
                 , Optional(root, "provenance") ?? "unknown", sources, Optional(root, "modelRevision"));
    }

    private static AnalysisSnapshot[] Validate(IReadOnlyList<AnalysisSnapshot> snapshots, string? question)
    {
        var sources = snapshots.ToArray();
        if (sources.Length is < 1 or > 16 || sources.Sum(source => (long)source.Text.Length) > 200_000 || question?.Length > 4000
         || sources.Any(source => string.IsNullOrWhiteSpace(source.Text) || string.IsNullOrWhiteSpace(source.ReferenceKey)
                               || source.ReferenceKey.Length > 128 || !source.Matches(source.Text))
         || sources.Select(source => source.ReferenceKey).Distinct(StringComparer.Ordinal).Count() != sources.Length)
            throw new ArgumentException("Review 1–16 distinct nonempty snapshots totaling at most 200,000 characters; questions allow 4,000 characters.");
        return sources;
    }

    private static HttpRequestMessage Request(string operation, AnalysisSnapshot[] snapshots, string? question, string requestId)
        => new(HttpMethod.Post, "api/document-analysis/" + operation)
        {
            Content = JsonContent.Create(new { protocolVersion = 1, clientRequestId = requestId, documents = snapshots, question })
        };

    private static void Verify(JsonElement root, AnalysisSnapshot[] sources, string requestId)
    {
        if (root.GetProperty("protocolVersion").GetInt32() != 1 || root.GetProperty("clientRequestId").GetString() != requestId
         || root.GetProperty("simulated").GetBoolean()) throw new InvalidOperationException("CP returned an unsupported, mismatched or simulated response.");
        var references = root.GetProperty("references").EnumerateArray().ToArray();
        if (references.Length != sources.Length) throw new InvalidOperationException("CP source references do not match the reviewed snapshots.");
        for (var index = 0; index < sources.Length; index++)
            if (references[index].GetProperty("referenceKey").GetString() != sources[index].ReferenceKey
             || !string.Equals(references[index].GetProperty("sourceHash").GetString(), sources[index].SourceHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("CP source references do not match the reviewed snapshots.");
    }

    private async Task<JsonDocument> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(125));
        cancellationToken = timeout.Token;
        try
        {
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"CP document analysis returned HTTP {(int)response.StatusCode}. Check connection, dedicated credential, capability and retry availability.");
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var buffer = new MemoryStream();
            var bytes = new byte[8192];
            int count;
            while ((count = await stream.ReadAsync(bytes, cancellationToken)) > 0)
            {
                if (buffer.Length + count > 1_048_576) throw new InvalidOperationException("CP response exceeds the bounded analysis size.");
                buffer.Write(bytes, 0, count);
            }
            return JsonDocument.Parse(buffer.ToArray(), new JsonDocumentOptions { MaxDepth = 32 });
        }
        catch (HttpRequestException) { throw new InvalidOperationException("CP is unavailable. Offline editing, local search and recovery remain available."); }
    }

    private static string? Optional(JsonElement root, string property) => root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    public void Dispose() => _http.Dispose();
}
