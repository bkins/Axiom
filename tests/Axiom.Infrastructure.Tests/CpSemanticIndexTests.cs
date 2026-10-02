using System.Net;
using System.Text.Json;
using Axiom.Core;

namespace Axiom.Infrastructure.Tests;

public sealed class CpSemanticIndexTests
{
    [Fact]
    public async Task Persistent_Cache_Reuses_Only_Hash_And_Model_Matched_Sections_And_Can_Be_Cleared()
    {
        var root = Path.Combine(Path.GetTempPath(), "Axiom-semantic-cache-" + Guid.NewGuid().ToString("N"));
        var cache = new SqliteStateStore(new AppDataPaths(root));
        await cache.InitializeAsync();
        await cache.SetAppearanceAsync(AppearancePreference.Dark);
        await cache.SaveDraftAsync(new RecoveryDraft("protected-cache-test", null, "recovery source", DateTimeOffset.UtcNow, null, true));
        using var handler = new EmbeddingResponseHandler();
        using var client = new CpDocumentAnalysisClient(new Uri("http://localhost:5273"), "test-semantic-index-credential-at-least-32", handler);
        var index = new CpSemanticIndex();
        var sources = new[] { AnalysisSnapshot.Capture("one", "First source"), AnalysisSnapshot.Capture("two", "Other source") };
        await index.BuildAsync(client, sources, cache: cache);
        await index.BuildAsync(client, sources, cache: cache);

        Assert.Equal(2, index.ReusedSections);
        Assert.Equal(1, handler.EmbeddingCalls);

        sources[1] = AnalysisSnapshot.Capture("two", "Changed text");
        await index.BuildAsync(client, sources, cache: cache);
        Assert.Equal(1, index.ReusedSections);
        Assert.Equal(2, handler.EmbeddingCalls);

        await cache.ClearAsync();
        await index.BuildAsync(client, sources, cache: cache);
        Assert.Equal(0, index.ReusedSections);
        Assert.Equal(3, handler.EmbeddingCalls);
        Assert.Equal(AppearancePreference.Dark, await cache.GetAppearanceAsync());
        Assert.Contains(await cache.GetRecoverableDraftsAsync(), draft => draft.Text == "recovery source");
    }

    [Fact]
    public async Task Failed_Later_Batch_Preserves_Previous_Complete_Index()
    {
        using var handler = new EmbeddingResponseHandler { FailOnCall = 3 };
        using var client = new CpDocumentAnalysisClient(new Uri("http://localhost:5273"), "test-semantic-index-credential-at-least-32", handler);
        var index = new CpSemanticIndex();
        var original = new[] { AnalysisSnapshot.Capture("original", "# First\nOriginal source") };
        await index.BuildAsync(client, original);
        var replacement = new[] { AnalysisSnapshot.Capture("replacement", string.Join("\n", Enumerable.Range(1, 17).Select(number => $"# Heading {number}\nText {number}"))) };

        await Assert.ThrowsAsync<InvalidOperationException>(() => index.BuildAsync(client, replacement));

        Assert.True(index.AppliesTo(original));
        Assert.False(index.AppliesTo(replacement));
        Assert.Equal("test-embedding", index.Model);
    }

    [Fact]
    public async Task Changed_Query_Model_Is_Rejected_And_Source_Change_Invalidates_Index()
    {
        using var handler = new EmbeddingResponseHandler();
        using var client = new CpDocumentAnalysisClient(new Uri("http://localhost:5273"), "test-semantic-index-credential-at-least-32", handler);
        var index = new CpSemanticIndex();
        var original = new[] { AnalysisSnapshot.Capture("doc", "Original source") };
        await index.BuildAsync(client, original);
        handler.Model = "new-model";

        await Assert.ThrowsAsync<InvalidOperationException>(() => index.SearchAsync(client, "query"));

        Assert.False(index.AppliesTo(new[] { AnalysisSnapshot.Capture("doc", "Changed source") }));
        Assert.True(index.AppliesTo(original));
    }

    private sealed class EmbeddingResponseHandler : HttpMessageHandler
    {
        internal int FailOnCall { get; init; } = -1;
        internal string Model { get; set; } = "test-embedding";
        internal int EmbeddingCalls => _calls;
        private int _calls;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request.Method == HttpMethod.Get)
                return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new
                {
                    protocolVersion = 1, insightsConfigured = true, embeddingsConfigured = true, simulated = false
                  , insightsModel = "test", embeddingsModel = Model, availability = "catalog_model_present"
                  , embeddingsRevision = new string('A', 64)
                })) };
            if (++_calls == FailOnCall) return new(HttpStatusCode.BadGateway);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var sources = body.RootElement.GetProperty("documents").EnumerateArray().ToArray();
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    protocolVersion = 1, clientRequestId = body.RootElement.GetProperty("clientRequestId").GetString()
                  , references = sources.Select(source => new { referenceKey = source.GetProperty("referenceKey").GetString(), sourceHash = source.GetProperty("sourceHash").GetString() })
                  , vectors = sources.Select(_ => new[] { 1f, 0f }).ToArray(), provider = "Ollama", model = Model, modelRevision = new string('A', 64), provenance = "configured_model_not_provider_attested", simulated = false
                }))
            };
        }
    }
}
