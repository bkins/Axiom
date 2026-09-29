using System.Diagnostics;
using System.Text.Json;
using Avalonia.Controls;
using Axiom.Core;
using Axiom.Infrastructure;

namespace Axiom.App;

internal static class ConnectedDocumentsProbe
{
    internal static string? Root { get; private set; }
    private static bool _verify;

    internal static void Configure(string root, bool verify)
    {
        root = Path.GetFullPath(root);
        if (!verify && (Directory.Exists(Path.Combine(root, "state")) || Directory.Exists(Path.Combine(root, "workspace-1000"))))
            throw new IOException("Native connected-document probe requires fresh state and workspace directories.");
        if (verify && !File.Exists(Path.Combine(root, "expected-session.json"))) throw new IOException("Capture the clean session before verifying startup.");
        Directory.CreateDirectory(root);
        Root = root;
        _verify = verify;
        Environment.SetEnvironmentVariable("AXIOM_STATE_ROOT", Path.Combine(root, "state"));
    }

    internal static async Task StartAsync(MainWindow window)
    {
        var root = Root!;
        window.UnsavedChoiceForTesting = _ => Task.FromResult("Discard");
        try
        {
            await window.StartupCompleted;
            var state = new SqliteStateStore(new AppDataPaths(Path.Combine(root, "state")));
            if (_verify)
            {
                var expected = JsonSerializer.Deserialize<WorkspaceDocumentSession>(await File.ReadAllTextAsync(Path.Combine(root, "expected-session.json")))!;
                var paths = window.TabsForTesting.Select(tab => Path.GetRelativePath(window.ActiveWorkspaceRootForTesting!, tab.Session.Path!)).ToArray();
                if (!paths.SequenceEqual(expected.Documents.Select(document => document.RelativePath))
                    || Path.GetRelativePath(window.ActiveWorkspaceRootForTesting!, window.PathContextForTesting!) != expected.ActiveRelativePath
                    || window.EditorCaretOffsetForTesting != expected.Documents.Single(document => document.RelativePath == expected.ActiveRelativePath).CaretOffset
                    || window.TabsForTesting.Any(tab => tab.Session.IsDirty)) throw new IOException("Native startup did not restore the clean tab set and active caret.");
                var original = JsonSerializer.Deserialize<ConnectedDocumentsProbeResult>(await File.ReadAllTextAsync(Path.Combine(root, "capture-result.json")))!;
                await File.WriteAllTextAsync(Path.Combine(root, "result.json"), JsonSerializer.Serialize(original with { CleanStartupRestored = true }));
                window.Close();
                return;
            }
            var workspace = await Task.Run(() => LinkIndexBenchmarkProbe.GenerateAsync(root, 1000));
            var source = Path.Combine(workspace, "native-source.md");
            var target = Path.Combine(workspace, "native-target.md");
            const string sourceText = "# Source\n\n[Follow](native-target.md#destination)\n";
            var targetText = string.Concat(Enumerable.Repeat("Paragraph before the heading.\n\n", 100)) + "# Destination\n\nTarget body\n"
                + string.Concat(Enumerable.Repeat("Paragraph after the heading.\n\n", 100));
            await File.WriteAllTextAsync(source, sourceText);
            await File.WriteAllTextAsync(target, targetText);
            if (!await window.SwitchWorkspaceAsync(workspace)) throw new IOException("Could not open the native workload workspace.");
            await window.OpenDocumentAsync(source);
            var preview = window.FindControl<NativeWebView>("Preview")!;
            await WaitAsync(async () => (await preview.InvokeScript("document.querySelector('a[href^=\"axiom-link:\"]') !== null"))?.Trim().Trim('"') == "true");
            await preview.InvokeScript("document.querySelector('a[href^=\"axiom-link:\"]').click()");
            await WaitAsync(() => Task.FromResult(window.PathContextForTesting == target));
            await WaitAsync(async () => (await preview.InvokeScript("(() => { const heading = document.getElementById('destination'); if (!heading) return false; const top = heading.getBoundingClientRect().top; return top >= -2 && top < 100; })()"))?.Trim().Trim('"') == "true");
            await window.NavigateHistoryForTestingAsync(false);
            var samples = new List<double>();
            for (var round = 0; round < 5 && samples.Count < 50; round++)
            {
                var indexing = window.RebuildBacklinksForTestingAsync();
                while (!indexing.IsCompleted)
                {
                    var tab = window.TabsForTesting.Single(tab => tab.Session.Path == source);
                    var clock = Stopwatch.StartNew();
                    tab.Document.Insert(tab.Document.TextLength, "x");
                    if (tab.Session.Text != tab.Document.Text) throw new IOException("Native document edit was not applied to its buffer.");
                    samples.Add(clock.Elapsed.TotalMilliseconds);
                    await Task.Delay(10);
                }
                await indexing;
            }
            if (samples.Count < 20) throw new IOException("Indexing completed before enough native edit samples were measured.");
            var p95 = samples.OrderBy(value => value).ElementAt((int)Math.Ceiling(samples.Count * 0.95) - 1);
            if (p95 >= 50) throw new IOException($"Native edit-to-buffer p95 {p95:F2} ms exceeds the 50 ms target.");
            await window.ConfirmAllTabsForTestingAsync();
            await window.OpenDocumentAsync(target);
            window.SetEditorSelectionForTesting(targetText.IndexOf("# Destination", StringComparison.Ordinal), 0);
            var record = (await state.GetActiveWorkspaceAsync())!;
            var index = (await state.GetLinkIndexAsync(record.WorkspaceId))!;
            var unchanged = await File.ReadAllTextAsync(source) == sourceText && await File.ReadAllTextAsync(target) == targetText;
            if (!unchanged) throw new IOException("Native workflow changed saved Markdown.");
            var report = new ConnectedDocumentsProbeResult(Environment.MachineName, true, true, index.Documents.Count, samples.Count, p95, unchanged, false);
            await File.WriteAllTextAsync(Path.Combine(root, "capture-result.json"), JsonSerializer.Serialize(report));
            window.Closed += (_, _) =>
            {
                var remembered = state.GetWorkspaceSessionAsync(record.WorkspaceId).GetAwaiter().GetResult();
                File.WriteAllText(Path.Combine(root, "expected-session.json"), JsonSerializer.Serialize(remembered));
            };
            window.Close();
        }
        catch (Exception exception)
        {
            File.WriteAllText(Path.Combine(root, "error.txt"), exception.ToString());
            Environment.ExitCode = 1;
            window.Close();
        }
    }

    private static async Task WaitAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!await condition())
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException("The native preview/navigation condition was not reached.");
            await Task.Delay(50);
        }
    }
}
