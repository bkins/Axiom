using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia.Threading;
using Axiom.Infrastructure;

namespace Axiom.App;

internal static class MultiBufferRecoveryProbe
{
    internal static string? Root { get; private set; }
    private static DispatcherTimer? _inputTimer;

    internal static void Configure(string root)
    {
        root = Path.GetFullPath(root);
        if (Directory.Exists(Path.Combine(root, "workspace")) || Directory.Exists(Path.Combine(root, "state")))
            throw new IOException("The native recovery probe requires fresh workspace and state directories.");
        Directory.CreateDirectory(root);
        Root = root;
        Environment.SetEnvironmentVariable("AXIOM_STATE_ROOT", Path.Combine(root, "state"));
    }

    internal static async Task StartAsync(MainWindow window)
    {
        if (Root is not { } root) return;
        try
        {
            await window.StartupCompleted;
            var workspace = Path.Combine(root, "workspace");
            Directory.CreateDirectory(workspace);
            for (var number = 1; number <= 3; number++)
                await File.WriteAllTextAsync(Path.Combine(workspace, $"saved-{number}.md"), $"# Saved document {number}\n");
            if (!await window.SwitchWorkspaceAsync(workspace)) throw new IOException("The recovery workload could not open its workspace.");
            for (var number = 1; number <= 3; number++)
                await window.OpenDocumentAsync(Path.Combine(workspace, $"saved-{number}.md"));
            await window.NewTabForTestingAsync();
            await window.NewTabForTestingAsync();
            var tabs = window.TabsForTesting.ToArray();
            if (tabs.Length != 5) throw new InvalidOperationException("The workload must contain exactly five independent buffers.");
            var savedTexts = tabs.Select(tab => tab.Session.SavedText).ToArray();
            var payload = string.Concat(Enumerable.Repeat("Markdown input with Unicode café ☕ and independent recovery.\n", 180));
            foreach (var tab in tabs) tab.Document.Text = $"0\n0\n{payload}";
            var clock = Stopwatch.StartNew();
            var sequence = 0;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _inputTimer = timer;
            timer.Tick += (_, _) =>
            {
                var elapsed = clock.ElapsedMilliseconds;
                ++sequence;
                File.WriteAllText(Path.Combine(root, "progress.txt"), $"Input {sequence}: editing buffers");
                foreach (var tab in tabs)
                {
                    var firstNewline = tab.Document.Text.IndexOf('\n');
                    var secondNewline = tab.Document.Text.IndexOf('\n', firstNewline + 1);
                    tab.Document.Replace(0, secondNewline + 1, $"{sequence}\n{elapsed}\n");
                }
                var checkpoint = tabs.Select((tab, index) => new RecoveryProbeBuffer(tab.Session.DraftKey
                                                                                   , tab.Session.Path
                                                                                   , savedTexts[index]
                                                                                   , sequence
                                                                                   , elapsed)).ToArray();
                File.WriteAllText(Path.Combine(root, "progress.txt"), $"Input {sequence}: publishing checkpoint");
                var temporary = Path.Combine(root, "heartbeat.tmp");
                File.WriteAllText(temporary, JsonSerializer.Serialize(checkpoint));
                File.WriteAllText(Path.Combine(root, "progress.txt"), $"Input {sequence}: replacing checkpoint");
                File.Move(temporary, Path.Combine(root, "heartbeat.json"), true);
                File.WriteAllText(Path.Combine(root, "progress.txt"), $"Input {sequence}: checkpoint published");
                if (sequence >= 20 && elapsed >= 1000)
                    File.WriteAllText(Path.Combine(root, "ready.txt"), "Five warmed buffers; native MainWindow recovery timer; input scheduled every 50 ms.");
            };
            timer.Start();
        }
        catch (Exception exception)
        {
            File.WriteAllText(Path.Combine(root, "error.txt"), exception.ToString());
        }
    }

    internal static int Verify(string root, string resultPath)
    {
        try
        {
            var checkpoint = JsonSerializer.Deserialize<RecoveryProbeBuffer[]>(File.ReadAllText(Path.Combine(root, "heartbeat.json")))!;
            var state = new SqliteStateStore(new AppDataPaths(Path.Combine(root, "state")));
            state.InitializeAsync().GetAwaiter().GetResult();
            var drafts = state.GetRecoverableDraftsAsync().GetAwaiter().GetResult();
            if (checkpoint.Length != 5 || drafts.Count != 5) throw new IOException("Recovery did not retain all five buffers.");
            var results = checkpoint.Select(entry =>
            {
                var draft = drafts.Single(candidate => candidate.DraftKey == entry.DraftKey);
                var lines = draft.Text.Split('\n', 3);
                var recoveredSequence = int.Parse(lines[0]);
                var recoveredElapsed = long.Parse(lines[1]);
                var unchanged = entry.FilePath is null || File.ReadAllText(entry.FilePath) == entry.SavedText;
                if (!unchanged) throw new IOException("The recovery workload changed a saved Markdown file.");
                return new RecoveryProbeBufferResult(entry.DraftKey
                                                   , entry.FilePath is null
                                                   , entry.Sequence
                                                   , entry.ElapsedMilliseconds
                                                   , recoveredSequence
                                                   , Math.Max(0, entry.ElapsedMilliseconds - recoveredElapsed)
                                                   , unchanged);
            }).ToArray();
            var result = new RecoveryProbeResult(Environment.MachineName
                                               , RuntimeInformation.FrameworkDescription
                                               , RuntimeInformation.ProcessArchitecture.ToString()
                                               , results);
            File.WriteAllText(resultPath, JsonSerializer.Serialize(result));
            return 0;
        }
        catch (Exception exception)
        {
            File.WriteAllText(resultPath, exception.ToString());
            return 1;
        }
    }
}
