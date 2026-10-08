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
            var assetPath = Path.Combine(workspace, "unrelated.txt");
            await File.WriteAllTextAsync(assetPath, "Non-Markdown remains for external Git.");
            await window.SwitchWorkspaceAsync(workspace);
            await window.OpenDocumentAsync(sourcePath);
            var saved = window.EditorTextForTesting;
            window.SetEditorTextForTesting(saved + "Unsaved buffer\n");
            await window.ProtectIfNeededForTestingAsync();
            var dirtyCommand = window.ExecuteCommandAsync("workspace.git");
            await dirtyCommand;
            var browser = window.GitViewForTesting ?? throw new IOException("Git changes panel is missing.");
            Require(Rows(browser).All(file => file.RelativePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                                          || file.RelativePath.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase)), "Git selection included a non-Markdown file.");
            Require(Windows().Count() == 1, "Git changes did not open as an embedded panel.");
            Select(browser, "notes.md");
            Require(!FindButton(browser, "Stage selected…").IsEnabled, "Git allowed staging a dirty editor buffer.");
            Click(browser, "Close");
            await dirtyCommand;
            Require(window.EditorTextForTesting.Contains("Unsaved buffer", StringComparison.Ordinal) && await File.ReadAllTextAsync(sourcePath) == saved, "Git inspection saved or discarded dirty text.");
            window.SetEditorTextForTesting(saved);
            var command = window.ExecuteCommandAsync("workspace.git");
            await command;
            browser = window.GitViewForTesting!;
            Select(browser, "notes.md");
            Require(FindButton(browser, "Stage selected…").IsEnabled, "Clean saved file could not be staged.");
            window.SetEditorTextForTesting(saved + "Edited while Git panel is open\n");
            Require(!FindButton(browser, "Stage selected…").IsEnabled && Windows().Count() == 1, "Embedded panel blocked editing or retained stale staging availability.");
            window.SetEditorTextForTesting(saved);
            Click(browser, "Saved diff");
            await WaitAsync(() => browser.DiffText.Text?.Contains("Baseline", StringComparison.Ordinal) == true);
            Require(Windows().Count() == 1, "Saved diff opened a separate window.");
            Click(browser, "Stage selected…");
            Click(await WaitForWindowAsync<ChoiceDialog>(), "Continue");
            await WaitAsync(() => Rows(browser).Any(file => file.RelativePath == "notes.md" && file.IndexStatus == "M"));
            Require(browser.StagedFiles.SelectedItems!.Cast<GitChangedFile>().Any(file => file.RelativePath == "notes.md"), "Staging did not carry selection into Staged Changes.");
            Require(browser.ChangedFiles.Items.Cast<GitChangedFile>().All(file => file.RelativePath != "notes.md"), "Fully staged notes remained in Changes.");
            Require(Rows(browser).All(file => file.RelativePath != "unrelated.txt") && browser.UnsupportedFiles.Items.Cast<GitChangedFile>().Any(file => file.RelativePath == "unrelated.txt") && File.Exists(assetPath), "Git refresh exposed an unsupported selection or changed the unrelated file.");
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
            await WaitAsync(() => browser.DiffText.Text?.Contains("Staged index", StringComparison.Ordinal) == true && browser.DiffText.Text.Contains("Selected saved notes", StringComparison.Ordinal));
            browser.CommitMessage.Text = "Native selected document commit";
            await Task.Delay(100);
            window.UpdateLayout();
            using (var screenshot = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize((int)window.ClientSize.Width, (int)window.ClientSize.Height), new Vector(96,96)))
            {
                screenshot.Render(window); screenshot.Save(Path.Combine(root, "embedded-git-avalonia-surface.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            }
            Click(browser, "Commit selected staged…");
            Click(await WaitForWindowAsync<ChoiceDialog>(), "Cancel");
            await WaitAsync(() => FindButton(browser, "Commit selected staged…").IsEnabled);
            Require(Git(workspace, "rev-parse", "HEAD") == originalHead, "Cancelled native commit changed HEAD.");
            Click(browser, "Commit selected staged…");
            Click(await WaitForWindowAsync<ChoiceDialog>(), "Commit");
            await WaitAsync(() => FindButton(browser, "Close").IsEnabled && browser.GetLogicalDescendants().OfType<TextBlock>().Any(text => text.Text?.StartsWith("Committed selected staged documents:", StringComparison.Ordinal) == true));
            Require(Git(workspace, "rev-parse", "HEAD") != originalHead, "Native commit did not publish the reviewed branch.");
            Require(Git(workspace, "show", "HEAD:notes.md") == saved.TrimEnd('\r', '\n'), "Native selected commit changed selected bytes.");
            Require(Git(workspace, "show", "HEAD:other.md") == "Other baseline" && Git(workspace, "show", ":other.md") == "Unrelated staged edit", "Native commit included or lost unrelated staging.");
            Require(await File.ReadAllTextAsync(sourcePath) == saved && await File.ReadAllTextAsync(otherPath) == "Unrelated staged edit", "Git changed working files.");
            Click(browser, "Close");
            Require((await command).Outcome == CommandOutcome.Completed, "Workspace Git command failed.");
            var nextWorkspace = Path.Combine(root, "workspace-without-git"); Directory.CreateDirectory(nextWorkspace);
            Require(await window.SwitchWorkspaceAsync(nextWorkspace) && window.GitViewForTesting is null, "Workspace switch retained a stale Git panel.");
            await File.WriteAllTextAsync(Path.Combine(root, "result.json"), JsonSerializer.Serialize(new
            {
                Version = window.BuildIdentityForTesting.InformationalVersion
              , Workspace = workspace
              , NativeStatusAndSavedStagedDiffs = true
              , EmbeddedChangesDiffAndCommitMessage = true
              , EditingAndDirtyAvailabilityWhilePanelOpen = true
              , WorkspaceSwitchClearsStalePanel = true
              , DirtyBufferStageBlocked = true
              , ActualStageUnstageAndCommitDialogs = true
              , CommitCancellationPreservedHead = true
              , SelectedOnlyBranchCommit = true
              , UnrelatedIndexAndWorkingBytesPreserved = true
              , UnsupportedRowsNotSelectable = true
              , StageCarriesCommitSelection = true
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
    private static Button FindButton(Control window, string label) => window.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, label));
    private static void Click(Control window, string label) => FindButton(window, label).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static IReadOnlyList<GitChangedFile> Rows(WorkspaceGitView view) => view.ChangedFiles.Items.Cast<GitChangedFile>().Concat(view.StagedFiles.Items.Cast<GitChangedFile>()).ToArray();
    private static void Select(WorkspaceGitView view, string path)
    {
        var staged = view.StagedFiles.Items.Cast<GitChangedFile>().SingleOrDefault(file => file.RelativePath == path);
        var working = view.ChangedFiles.Items.Cast<GitChangedFile>().SingleOrDefault(file => file.RelativePath == path);
        if (staged is not null) view.StagedFiles.SelectedItem = staged;
        if (working is not null) view.ChangedFiles.SelectedItem = working;
    }
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
