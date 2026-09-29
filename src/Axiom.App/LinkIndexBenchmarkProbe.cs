using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Axiom.Core;
using Axiom.Infrastructure;

namespace Axiom.App;

internal static class LinkIndexBenchmarkProbe
{
    internal static async Task<string> GenerateAsync(string root, int count)
    {
        var workspace = Path.Combine(root, $"workspace-{count}");
        if (Directory.Exists(workspace)) throw new IOException("Benchmark requires a fresh workspace directory.");
        Directory.CreateDirectory(workspace);
        for (var folder = 0; folder < 10; folder++) Directory.CreateDirectory(Path.Combine(workspace, $"group-{folder}"));
        var filler = string.Concat(Enumerable.Repeat("Plain Markdown body with Unicode café ☕. ", 240));
        for (var number = 0; number < count; number++)
        {
            var path = Path.Combine(workspace, $"group-{number % 10}", $"document {number / 10}.md");
            var links = string.Join('\n', Enumerable.Range(1, 9).Select(offset =>
            {
                var target = (number + offset) % count;
                return $"[Reference {offset}](../group-{target % 10}/document%20{target / 10}.md#repeated-heading)";
            }));
            await File.WriteAllTextAsync(path, $"# Repeated **heading**\n\n{links}\n\n{filler}\n\n## Repeated heading\n\n[Missing](missing.md)\n");
        }
        return workspace;
    }

    internal static int Run(string root, string resultPath)
    {
        try
        {
            root = Path.GetFullPath(root);
            Directory.CreateDirectory(root);
            var results = new List<LinkIndexBenchmarkResult>();
            var paths = new AppDataPaths(Path.Combine(root, "state"));
            var state = new SqliteStateStore(paths);
            state.InitializeAsync().GetAwaiter().GetResult();
            var files = new FileDocumentStore();
            var service = new WorkspaceLinkIndexService(files, new MarkdownLinkTargetReader(files));
            foreach (var count in new[] { 1000, 10000 })
            {
                var workspace = GenerateAsync(root, count).GetAwaiter().GetResult();
                var clock = Stopwatch.StartNew();
                var index = service.BuildAsync(workspace).GetAwaiter().GetResult();
                var buildMilliseconds = clock.Elapsed.TotalMilliseconds;
                var record = state.OpenWorkspaceAsync(workspace, $"Benchmark {count}", DateTimeOffset.UtcNow).GetAwaiter().GetResult();
                clock.Restart();
                state.SaveLinkIndexAsync(record.WorkspaceId, index).GetAwaiter().GetResult();
                var persistenceMilliseconds = clock.Elapsed.TotalMilliseconds;
                var unchanged = index.Documents.All(document => WorkspaceLinkIndexService.Hash(File.ReadAllText(Path.Combine(workspace, document.RelativePath))) == document.ContentHash);
                using var cancellation = new CancellationTokenSource();
                var cancelled = new Stopwatch();
                var progress = new InlineIndexProgress(_ => { if (!cancellation.IsCancellationRequested) { cancelled.Start(); cancellation.Cancel(); } });
                try { service.BuildAsync(workspace, progress, cancellation.Token).GetAwaiter().GetResult(); throw new IOException("Cancelled rebuild completed unexpectedly."); }
                catch (OperationCanceledException) { cancelled.Stop(); }
                results.Add(new(count, index.Documents.Sum(document => document.References.Count), index.Errors.Count,
                    buildMilliseconds, persistenceMilliseconds, cancelled.Elapsed.TotalMilliseconds, unchanged));
            }
            var report = JsonSerializer.Serialize(new { Machine = Environment.MachineName, Runtime = RuntimeInformation.FrameworkDescription, Results = results });
            File.WriteAllText(resultPath, report);
            return results.Any(result => !result.MarkdownUnchanged || result.CancellationAcknowledgementMilliseconds > 1000
                || result.FileCount == 1000 && result.BuildMilliseconds > 10000) ? 1 : 0;
        }
        catch (Exception exception) { File.WriteAllText(resultPath, exception.ToString()); return 1; }
    }
}
