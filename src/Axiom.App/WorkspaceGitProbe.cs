using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Axiom.Core;

namespace Axiom.App;

internal static class WorkspaceGitProbe
{
    internal static string? Root { get; private set; }
    internal static void Configure(string root)
    {
        root = Path.GetFullPath(root);
        if (Directory.Exists(root)) throw new IOException("Choose a fresh Git evidence directory.");
        Directory.CreateDirectory(root);
        Root = root;
        Environment.SetEnvironmentVariable("AXIOM_STATE_ROOT", Path.Combine(root, "state"));
    }

    internal static async Task StartAsync(MainWindow window)
    {
        var root = Root!;
        window.UnsavedChoiceForTesting = _ => Task.FromResult("Discard");
        try
        {
            await window.StartupCompleted;
            var workspace = Path.Combine(Path.GetTempPath(), "AxiomGitWorkspace-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workspace);
            Git(workspace, "init", "-q");
            Git(workspace, "config", "user.name", "Axiom native test");
            Git(workspace, "config", "user.email", "test@axiom.local");
            Git(workspace, "config", "core.autocrlf", "false");
            var sourcePath = Path.Combine(workspace, "notes.md");
            var otherPath = Path.Combine(workspace, "other.md");
            await File.WriteAllTextAsync(sourcePath, "# Baseline\n");
            await File.WriteAllTextAsync(otherPath, "Other baseline");
            Git(workspace, "add", ".");
            Git(workspace, "commit", "-qm", "Baseline");
            var originalHead = Git(workspace, "rev-parse", "HEAD");
            await File.WriteAllTextAsync(sourcePath, "# Selected saved notes\n\nCafé\n");
            await File.WriteAllTextAsync(otherPath, "Unrelated staged edit");
            Git(workspace, "add", "other.md");
            await window.SwitchWorkspaceAsync(workspace);
            await window.OpenDocumentAsync(sourcePath);
            var saved = window.EditorTextForTesting;
            window.SetEditorTextForTesting(saved + "Unsaved buffer\n");
            await window.ProtectIfNeededForTestingAsync();
            var dirtyCommand = window.ExecuteCommandAsync("workspace.git");
            var browser = await WaitForWindowAsync<WorkspaceGitWindow>();
            Select(browser, "notes.md");
            Require(!FindButton(browser, "Stage selected…").IsEnabled, "Git allowed staging a dirty editor buffer.");
            Click(browser, "Close");
            await dirtyCommand;
            Require(window.EditorTextForTesting.Contains("Unsaved buffer", StringComparison.Ordinal) && await File.ReadAllTextAsync(sourcePath) == saved, "Git inspection saved or discarded dirty text.");
            window.SetEditorTextForTesting(saved);
            var command = window.ExecuteCommandAsync("workspace.git");
            browser = await WaitForWindowAsync<WorkspaceGitWindow>();
            Select(browser, "notes.md");
            Click(browser, "Saved diff");
            var diff = await WaitForWindowAsync<LineDiffWindow>();
            Require(diff.GetLogicalDescendants().OfType<TextBox>().Single().Text!.Contains("Baseline", StringComparison.Ordinal), "Saved Git diff missed HEAD source.");
            Click(diff, "Close");
            Click(browser, "Stage selected…");
            Click(await WaitForWindowAsync<ChoiceDialog>(), "Continue");
            await WaitAsync(() => Rows(browser).Any(file => file.RelativePath == "notes.md" && file.IndexStatus == "M"));
            Select(browser, "notes.md");
            Click(browser, "Unstage selected…");
            Click(await WaitForWindowAsync<ChoiceDialog>(), "Continue");
            await WaitAsync(() => Rows(browser).Any(file => file.RelativePath == "notes.md" && file.IndexStatus == " "));
            Select(browser, "notes.md");
            Click(browser, "Stage selected…");
            Click(await WaitForWindowAsync<ChoiceDialog>(), "Continue");
            await WaitAsync(() => Rows(browser).Any(file => file.RelativePath == "notes.md" && file.IndexStatus == "M"));
            Select(browser, "notes.md");
            Click(browser, "Staged diff");
            diff = await WaitForWindowAsync<LineDiffWindow>();
            Require(diff.GetLogicalDescendants().OfType<TextBox>().Single().Text!.Contains("Selected saved notes", StringComparison.Ordinal), "Staged diff missed selected content.");
            Click(diff, "Close");
            Click(browser, "Commit selected staged…");
            Click(await WaitForWindowAsync<TextEntryDialog>(), "Cancel");
            await WaitAsync(() => FindButton(browser, "Commit selected staged…").IsEnabled);
            Require(Git(workspace, "rev-parse", "HEAD") == originalHead, "Cancelled native commit changed HEAD.");
            Click(browser, "Commit selected staged…");
            var message = await WaitForWindowAsync<TextEntryDialog>();
            message.GetLogicalDescendants().OfType<TextBox>().Single().Text = "Native selected document commit";
            Click(message, "Continue");
            Click(await WaitForWindowAsync<ChoiceDialog>(), "Commit");
            await WaitAsync(() => FindButton(browser, "Close").IsEnabled && browser.GetLogicalDescendants().OfType<TextBlock>().Any(text => text.Text?.StartsWith("Committed selected staged documents:", StringComparison.Ordinal) == true));
            Require(Git(workspace, "rev-parse", "HEAD") != originalHead, "Native commit did not publish the reviewed branch.");
            Require(Git(workspace, "show", "HEAD:notes.md") == saved.TrimEnd('\r', '\n'), "Native selected commit changed selected bytes.");
            Require(Git(workspace, "show", "HEAD:other.md") == "Other baseline" && Git(workspace, "show", ":other.md") == "Unrelated staged edit", "Native commit included or lost unrelated staging.");
            Require(await File.ReadAllTextAsync(sourcePath) == saved && await File.ReadAllTextAsync(otherPath) == "Unrelated staged edit", "Git changed working files.");
            Click(browser, "Close");
            Require((await command).Outcome == CommandOutcome.Completed, "Workspace Git command failed.");
            await File.WriteAllTextAsync(Path.Combine(root, "result.json"), JsonSerializer.Serialize(new
            {
                Version = window.BuildIdentityForTesting.InformationalVersion
              , Workspace = workspace
              , NativeStatusAndSavedStagedDiffs = true
              , DirtyBufferStageBlocked = true
              , ActualStageUnstageAndCommitDialogs = true
              , CommitCancellationPreservedHead = true
              , SelectedOnlyBranchCommit = true
              , UnrelatedIndexAndWorkingBytesPreserved = true
              , HumanChecks = "Physical keyboard/focus, screen reader, DPI/themes and human release acceptance remain pending."
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception exception) { await File.WriteAllTextAsync(Path.Combine(root, "error.txt"), exception.ToString()); }
        finally
        {
            foreach (var open in Windows().Where(open => open != window).ToArray()) open.Close();
            window.SetEditorTextForTesting(window.TabsForTesting.Last().Session.SavedText);
            window.Close();
        }
    }

    private static string Git(string root, params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = root
          , UseShellExecute = false
          , CreateNoWindow = true
          , RedirectStandardOutput = true
          , RedirectStandardError = true
          , StandardOutputEncoding = System.Text.Encoding.UTF8
          , StandardErrorEncoding = System.Text.Encoding.UTF8
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(30000)) { process.Kill(true); throw new IOException("Native fixture Git timed out."); }
        if (process.ExitCode != 0) throw new IOException(error);
        return output.TrimEnd('\r', '\n');
    }
    private static IEnumerable<Window> Windows() => ((IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!).Windows;
    private static Button FindButton(Window window, string label) => window.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, label));
    private static void Click(Window window, string label) => FindButton(window, label).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static IReadOnlyList<GitChangedFile> Rows(Window window) => window.GetLogicalDescendants().OfType<ListBox>().Single().ItemsSource!.Cast<GitChangedFile>().ToArray();
    private static void Select(Window window, string path) => window.GetLogicalDescendants().OfType<ListBox>().Single().SelectedItem = Rows(window).Single(file => file.RelativePath == path);
    private static async Task<TWindow> WaitForWindowAsync<TWindow>() where TWindow : Window
    {
        await WaitAsync(() => Windows().OfType<TWindow>().Any());
        return Windows().OfType<TWindow>().Single();
    }
    private static async Task WaitAsync(Func<bool> predicate)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(25);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (predicate()) return;
            await Task.Delay(100);
        }
        throw new IOException("Native workspace Git check timed out.");
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new IOException(message);
    }
}
