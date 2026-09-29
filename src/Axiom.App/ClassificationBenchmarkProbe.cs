using System.Diagnostics;
using System.Text.Json;
using Axiom.Core;
using Axiom.Infrastructure;

namespace Axiom.App;

internal static class ClassificationBenchmarkProbe
{
    public static int Run(string root, string resultPath)
    {
        try
        {
            var rows = new List<object>();
            foreach (var count in new[] { 1000, 10000 })
            {
                var workspace = LinkIndexBenchmarkProbe.GenerateAsync(root, count).GetAwaiter().GetResult();
                var files = new FileDocumentStore();
                var all = files.ListMarkdownFilesAsync(workspace).GetAwaiter().GetResult().Files;
                // Enrollment is fixture preparation, before the metadata-operation hash boundary.
                foreach (var path in all)
                    File.WriteAllText(path, DocumentIdCodec.Enroll(File.ReadAllText(path), Guid.NewGuid()));
                var before = all.ToDictionary(path => path, path => WorkspaceLinkIndexService.Hash(File.ReadAllText(path)));
                var state = new SqliteStateStore(new AppDataPaths(Path.Combine(root, "state-" + count)));
                state.InitializeAsync().GetAwaiter().GetResult();
                var service = new ClassificationService(files, state, state);
                var clock = Stopwatch.StartNew();
                var catalog = service.DiscoverAsync(workspace).GetAwaiter().GetResult();
                var discoveryMs = clock.Elapsed.TotalMilliseconds;
                var changes = catalog.Documents.Select(document => new ClassificationChange(new(document.DocumentId!.Value, ["Benchmark", "Unicode café"], "Note", ["Alternate " + document.RelativePath]), 0)).ToArray();
                clock.Restart();
                service.ApplyAsync(catalog, changes).GetAwaiter().GetResult();
                var applyMs = clock.Elapsed.TotalMilliseconds;
                catalog = service.DiscoverAsync(workspace).GetAwaiter().GetResult();
                clock.Restart();
                var matched = catalog.Documents.Count(document => ClassificationPolicy.Matches(document.RelativePath, document.Classification!, ["benchmark", "unicode café"], "note", "Alternate"));
                var filterMs = clock.Elapsed.TotalMilliseconds;
                var transferPath = Path.Combine(root, $"metadata-{count}.json");
                clock.Restart();
                MetadataTransferService.WriteAsync(transferPath, MetadataTransferService.Export(catalog)).GetAwaiter().GetResult();
                var transfer = MetadataTransferService.ReadAsync(transferPath).GetAwaiter().GetResult();
                var other = new SqliteStateStore(new AppDataPaths(Path.Combine(root, "imported-" + count)));
                other.InitializeAsync().GetAwaiter().GetResult();
                var importing = new ClassificationService(files, other, other);
                var evidence = importing.DiscoverAsync(workspace).GetAwaiter().GetResult();
                var proposed = MetadataTransferService.Preview(transfer, evidence);
                importing.ApplyAsync(evidence, proposed.Select(proposal => proposal.Change!).ToArray()).GetAwaiter().GetResult();
                var transferMs = clock.Elapsed.TotalMilliseconds;
                var unchanged = all.All(path => before[path] == WorkspaceLinkIndexService.Hash(File.ReadAllText(path)));
                var importedCount = other.GetClassificationsAsync().GetAwaiter().GetResult().Count;
                if (matched != count || importedCount != count || !unchanged) throw new IOException("Classification benchmark correctness check failed.");
                rows.Add(new { Files = count, DiscoveryMilliseconds = discoveryMs, ApplyMilliseconds = applyMs, FilterMilliseconds = filterMs, TransferMilliseconds = transferMs, Matched = matched, Imported = importedCount, MarkdownUnchanged = unchanged });
            }
            File.WriteAllText(resultPath, JsonSerializer.Serialize(new { Machine = Environment.MachineName, Runtime = Environment.Version.ToString(), Collections = rows }, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception exception) { File.WriteAllText(resultPath, exception.ToString()); return 1; }
    }
}
