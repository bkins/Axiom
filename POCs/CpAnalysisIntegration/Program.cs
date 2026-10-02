using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Axiom.Core;
using Axiom.Infrastructure;
using CognitivePlatform.Api.Controllers;
using CognitivePlatform.Api.DocumentAnalysis;
using CognitivePlatform.Api.Integrations.Embeddings;
using CognitivePlatform.Api.Interpreter;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Options;

var outputDirectory = Path.GetFullPath(args[0]);
if (Directory.Exists(outputDirectory)) throw new InvalidOperationException("Choose a fresh evidence directory.");
Directory.CreateDirectory(outputDirectory);
var credential = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
builder.Logging.ClearProviders();
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Services.Configure<DocumentAnalysisSettings>(settings =>
{
    settings.Enabled = true;
    settings.ClientKeySha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(credential)));
    settings.TimeoutSeconds = 120;
});
builder.Services.AddSingleton(Options.Create(new LlmClientSettings { Provider = LlmProvider.Ollama, Endpoint = "http://localhost:11434", DefaultModel = "phi3:mini", Timeout = 120 }));
builder.Services.AddSingleton(Options.Create(new EmbeddingSettings { OllamaBaseUrl = "http://localhost:11434", EmbeddingModel = "nomic-embed-text:latest" }));
builder.Services.AddHttpClient("Ollama");
builder.Services.AddHttpClient("OllamaEmbedding");
builder.Services.AddSingleton<IGroqUsageTracker, GroqUsageTracker>();
builder.Services.AddSingleton<ILlmRateLimiter, InMemoryLlmRateLimiter>();
builder.Services.AddSingleton<ILlmClientFactory, LlmClientFactory>();
builder.Services.AddSingleton<IEmbeddingService, OllamaEmbeddingService>();
builder.Services.AddSingleton<DocumentAnalysisGate>();
builder.Services.AddSingleton<IDocumentModelRevisionResolver, DocumentModelRevisionResolver>();
builder.Services.AddControllers().AddApplicationPart(typeof(DocumentAnalysisController).Assembly);
await using var host = builder.Build();
host.MapControllers();
await host.StartAsync();
try
{
    var endpoint = new Uri(host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single());
    using var client = new CpDocumentAnalysisClient(endpoint, credential);
    var capabilities = await client.GetCapabilitiesAsync();
    var snapshots = new[]
    {
        AnalysisSnapshot.Capture("fixture-a", "The fictional project Cedar schedules its first release for Friday. The budget is 100 credits.")
      , AnalysisSnapshot.Capture("fixture-b", "The fictional project Cedar schedules its first release for Monday. The budget is 100 credits.")
    };
    var vectors = await client.GetEmbeddingsAsync(snapshots);
    var insight = await client.GetInsightsAsync(snapshots, "Identify the conflicting release dates and cite fixture-a and fixture-b. Keep the answer brief.");
    var semanticIndex = new CpSemanticIndex();
    var cache = new SqliteStateStore(new AppDataPaths(Path.Combine(outputDirectory, "derived-state")));
    await cache.InitializeAsync();
    await semanticIndex.BuildAsync(client, snapshots, cache: cache);
    await semanticIndex.BuildAsync(client, snapshots, cache: cache);
    if (semanticIndex.ReusedSections != 2) throw new InvalidOperationException("The repeated index build did not reuse both model-digest-matched sections.");
    var semanticMatches = await semanticIndex.SearchAsync(client, "When is the project released?");
    var relatedMatches = await semanticIndex.SearchAsync(client, snapshots[0].Text, "fixture-a");
    var nativeUiVerified = false;
    if (args.Length == 2)
    {
        var start = new System.Diagnostics.ProcessStartInfo(Path.GetFullPath(args[1]))
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
        };
        start.ArgumentList.Add("--everyday-use-probe");
        start.ArgumentList.Add(Path.Combine(outputDirectory, "native-everyday"));
        start.Environment["AXIOM_CP_PROBE_ENDPOINT"] = endpoint.AbsoluteUri;
        start.Environment["AXIOM_CP_PROBE_CREDENTIAL"] = credential;
        using var process = System.Diagnostics.Process.Start(start) ?? throw new InvalidOperationException("Native Axiom did not start.");
        using var nativeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(180));
        await process.WaitForExitAsync(nativeTimeout.Token);
        using var evidence = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(outputDirectory, "native-everyday", "result.json")));
        nativeUiVerified = evidence.RootElement.GetProperty("CpNativeNetworkSubmissionVerified").GetBoolean();
        if (!nativeUiVerified) throw new InvalidOperationException("Native CP submission did not pass.");
    }
    var result = new
    {
        success = true, capabilities
      , vectors.Provider, embeddingModel = vectors.Model, embeddingProvenance = vectors.Provenance
      , dimensions = vectors.Vectors[0].Length, vectorCount = vectors.Vectors.Length
      , insight, sourceHashesPreserved = snapshots.All(snapshot => snapshot.Matches(snapshot.Text))
      , semanticMatches, relatedMatches, indexHashesCurrent = semanticIndex.AppliesTo(snapshots)
      , semanticIndex.ModelRevision, semanticIndex.ReusedSections
      , syntheticInputsOnly = true, domainStoresRegistered = false, actionPipelineRegistered = false
      , credentialPersisted = false, nativeUiVerified, humanQualityAccepted = false
    };
    await File.WriteAllTextAsync(Path.Combine(outputDirectory, "verification.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine("Actual CP HTTP and local Ollama round trip passed; evidence saved without credential.");
}
finally { await host.StopAsync(); }
