using Axiom.Core;
using System.Text.Json;

namespace Axiom.Infrastructure;

/// <summary>Opt-in, session-only derived index. A failed or cancelled rebuild never publishes a partial index.</summary>
public sealed class CpSemanticIndex
{
    private IReadOnlyList<SemanticSection> _sections = [];
    private float[][] _vectors = [];
    public string? Model { get; private set; }
    public string? Provider { get; private set; }
    public string? Provenance { get; private set; }
    public string? ModelRevision { get; private set; }
    public bool IsBuilt => _sections.Count > 0;
    public int ReusedSections { get; private set; }

    public async Task BuildAsync(CpDocumentAnalysisClient client, IReadOnlyList<AnalysisSnapshot> documents, CancellationToken cancellationToken = default, ISemanticDocumentResultStore? cache = null)
    {
        var sections = SemanticRanking.Sections(documents);
        if (sections.Count == 0) throw new ArgumentException("The reviewed source set has no nonempty sections.");
        var vectors = new Dictionary<string, float[]>(StringComparer.Ordinal);
        string? model = null, provider = null, provenance = null, revision = null;
        var providerId = "cp:" + client.EndpointIdentity + ":Ollama";
        var reused = 0;
        if (cache is not null)
        {
            var capabilities = await client.GetCapabilitiesAsync(cancellationToken);
            if (capabilities.Simulated) throw new InvalidOperationException("Simulated embeddings cannot build a live semantic index.");
            model = capabilities.EmbeddingsModel;
            revision = capabilities.EmbeddingsRevision;
            provider = "Ollama";
            provenance = "configured_model_with_catalog_digest";
            if (model is not null && revision is not null)
                foreach (var section in sections)
                {
                    var cached = await cache.ReadAsync(CacheKey(section), providerId, model + "|" + revision, section.Snapshot.SourceHash, 0, cancellationToken);
                    if (cached is null) continue;
                    var vector = JsonSerializer.Deserialize<float[]>(cached.ResultJson);
                    if (vector is null || vector.Length is < 1 or > 8192 || vector.Any(value => !float.IsFinite(value))) continue;
                    if (vectors.Count != 0 && vectors.Values.First().Length != vector.Length) continue;
                    vectors.Add(section.Snapshot.ReferenceKey, vector);
                    reused++;
                }
        }
        foreach (var batch in sections.Where(section => !vectors.ContainsKey(section.Snapshot.ReferenceKey)).Chunk(16))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var response = await client.GetEmbeddingsAsync(batch.Select(section => section.Snapshot).ToArray(), cancellationToken);
            if (model is not null && (model != response.Model || provider != response.Provider || revision != response.ModelRevision
                                     || vectors.Count != 0 && vectors.Values.First().Length != response.Vectors[0].Length))
                throw new InvalidOperationException("CP embedding model or dimensions changed during the rebuild. Reconnect and rebuild.");
            model = response.Model;
            provider = response.Provider;
            provenance = response.Provenance;
            revision = response.ModelRevision;
            for (var index = 0; index < batch.Length; index++)
            {
                vectors.Add(batch[index].Snapshot.ReferenceKey, response.Vectors[index]);
                if (cache is not null && revision is not null)
                    await cache.WriteAsync(new SemanticDocumentResult(providerId, model + "|" + revision, CacheKey(batch[index])
                                                                    , batch[index].Snapshot.SourceHash, 0, DateTimeOffset.UtcNow
                                                                    , JsonSerializer.Serialize(response.Vectors[index])), cancellationToken);
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        _sections = sections;
        _vectors = sections.Select(section => vectors[section.Snapshot.ReferenceKey]).ToArray();
        Model = model;
        Provider = provider;
        Provenance = provenance;
        ModelRevision = revision;
        ReusedSections = reused;
    }

    private static string CacheKey(SemanticSection section) => section.DocumentKey + "@" + section.Offset + ":" + section.Length;

    public bool AppliesTo(IReadOnlyList<AnalysisSnapshot> documents)
        => IsBuilt && _sections.All(section => documents.Any(document => document.ReferenceKey == section.DocumentKey && document.SourceHash == section.DocumentHash))
                   && documents.All(document => _sections.Any(section => section.DocumentKey == document.ReferenceKey));

    public async Task<IReadOnlyList<SemanticMatch>> SearchAsync(CpDocumentAnalysisClient client, string query, string? excludedDocument = null, CancellationToken cancellationToken = default)
    {
        if (!IsBuilt) throw new InvalidOperationException("Review sources and explicitly build the semantic index first.");
        var response = await client.GetEmbeddingsAsync(new[] { AnalysisSnapshot.Capture("semantic-query", query) }, cancellationToken);
        if (response.Model != Model || response.Provider != Provider || response.ModelRevision != ModelRevision) throw new InvalidOperationException("The embedding model changed; rebuild the reviewed index.");
        return SemanticRanking.Rank(_sections, _vectors, response.Vectors[0], excludedDocument);
    }
}
