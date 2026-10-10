using System.Diagnostics;
using System.Text.Json;
using Avalonia.Controls;
using Axiom.Core;
using Axiom.Infrastructure;

namespace Axiom.App;

internal static class ExternalOpeningProbe
{
    internal static string? Root { get; private set; }

    internal static void Configure(string root)
    {
        Root = Path.GetFullPath(root);
        if (Directory.Exists(Root)) throw new IOException("Choose a fresh external opening evidence directory.");
        Directory.CreateDirectory(Root);
        Environment.SetEnvironmentVariable("AXIOM_STATE_ROOT", Path.Combine(Root, "state"));
    }

    internal static async Task StartAsync(MainWindow window)
    {
        try
        {
            await window.StartupCompleted;
            var workspace = Path.Combine(Root!, "remembered");
            var otherRoot = Path.Combine(Root!, "other");
            Directory.CreateDirectory(workspace);
            Directory.CreateDirectory(otherRoot);
            var path = Path.Combine(workspace, "space and ' quote.md");
            var other = Path.Combine(otherRoot, "other.md");
            var standalone = Path.Combine(Root!, "standalone.markdown");
            const string source = "# Ordinary source\r\n\r\nRetained bytes\r\n";
            await File.WriteAllTextAsync(path, source);
            await File.WriteAllTextAsync(other, "Other source");
            await File.WriteAllTextAsync(standalone, "Standalone source");
            await window.SwitchWorkspaceAsync(workspace);
            await window.SwitchWorkspaceAsync(otherRoot);
            await ForwardAsync(path);
            await WaitForPathAsync(window, path);
            Require(window.ActiveWorkspaceRootForTesting == workspace, "Forwarded file did not activate its remembered workspace.");
            window.FilterExplorerForTesting("no match");
            window.SetEditorTextForTesting(source + "Dirty buffer");
            await window.ProtectIfNeededForTestingAsync();
            await ForwardAsync(path);
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (window.FindControl<TextBox>("ExplorerFilter")!.Text != "" && DateTime.UtcNow < deadline) await Task.Delay(25);
            Require(window.FindControl<TextBox>("ExplorerFilter")!.Text == "" &&
                window.FindControl<TreeView>("FileTree")!.SelectedItem is WorkspaceExplorerNode node && node.RelativePath == Path.GetFileName(path), "External opening failed to reveal the filtered file.");
            Require(window.TabsForTesting.Count(tab => tab.Session.Path == path) == 1 && window.EditorTextForTesting == source + "Dirty buffer", "Repeated activation lost or duplicated the dirty tab.");
            window.UnsavedChoiceForTesting = _ => Task.FromResult("Cancel");
            Require(!await window.OpenExternalDocumentAsync(other) && window.ActiveWorkspaceRootForTesting == workspace, "Cancelled activation switched workspace.");
            await ForwardAsync(standalone);
            await WaitForPathAsync(window, standalone);
            Require(window.ActiveWorkspaceRootForTesting == workspace && window.TabsForTesting.Any(tab => tab.Session.Path == path && tab.Session.Text == source + "Dirty buffer"), "Standalone activation changed the workspace or dropped dirty text.");
            Require(await File.ReadAllTextAsync(path) == source, "Activation wrote the original source.");
            var state = new SqliteStateStore(new AppDataPaths(Path.Combine(Root!, "state")));
            Require((await state.GetRecoverableDraftsAsync()).Any(draft => draft.Text == source + "Dirty buffer"), "Activation lost protected recovery.");
            await File.WriteAllTextAsync(Path.Combine(Root!, "result.json"), JsonSerializer.Serialize(new
            {
                Version = window.BuildIdentityForTesting.InformationalVersion
              , RealSecondaryProcessForwarding = true
              , RememberedWorkspaceActivated = true
              , FilteredFileSelectedAndRevealed = true
              , DirtyExistingTabRetainedWithoutDuplicate = true
              , CancelRetainsWorkspace = true
              , StandaloneKeepsWorkspaceAndDirtyTabs = true
              , SavedBytesAndRecoveryRetained = true
              , HumanExplorerDefaultChoiceAccepted = false
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception exception) { await File.WriteAllTextAsync(Path.Combine(Root!, "error.txt"), exception.ToString()); }
        finally { window.UnsavedChoiceForTesting = _ => Task.FromResult("Discard"); window.Close(); }
    }

    private static async Task ForwardAsync(string path)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add(path);
        using var process = Process.Start(start) ?? throw new IOException("Secondary process did not start.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await process.WaitForExitAsync(timeout.Token);
        Require(process.ExitCode == 0, "Secondary process did not acknowledge activation.");
    }

    private static async Task WaitForPathAsync(MainWindow window, string path)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (window.PathContextForTesting != path && DateTime.UtcNow < deadline) await Task.Delay(25);
        Require(window.PathContextForTesting == path, "Forwarded document was not displayed.");
    }

    private static void Require(bool condition, string message) { if (!condition) throw new IOException(message); }
}
