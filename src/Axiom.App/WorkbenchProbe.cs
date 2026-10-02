using System.Text.Json;
using Avalonia.Controls;
using Axiom.Core;
using Axiom.Infrastructure;

namespace Axiom.App;

internal static class WorkbenchProbe
{
    internal static string? Root { get; private set; }

    internal static void Configure(string root)
    {
        root = Path.GetFullPath(root);
        if (Directory.Exists(root)) throw new IOException("Choose a fresh workbench evidence directory.");
        Directory.CreateDirectory(root);
        Root = root;
        Environment.SetEnvironmentVariable("AXIOM_STATE_ROOT", Path.Combine(root, "state"));
    }

    internal static async Task StartAsync(MainWindow window)
    {
        var root = Root!;
        try
        {
            await window.StartupCompleted;
            string? walkthroughPath = null;
            window.WalkthroughLaunchForTesting = path => { walkthroughPath = path; return Task.CompletedTask; };
            Require((await window.ExecuteCommandAsync("help.walkthrough")).Outcome == CommandOutcome.Completed && walkthroughPath is not null && File.Exists(walkthroughPath), "Packaged walkthrough dispatch failed.");
            window.Width = 1100;
            window.Height = 680;
            window.Position = new Avalonia.PixelPoint(20, 20);
            var workspace = Path.Combine(root, "Axiom Tutorial");
            var notes = Path.Combine(workspace, "Notes");
            Directory.CreateDirectory(notes);
            var welcome = Path.Combine(workspace, "Welcome.md");
            var project = Path.Combine(notes, "Project Notes.md");
            var source = "# Welcome to Axiom\n\nYour workspace keeps Markdown files together. Open a file to read or edit it.\n\n## Explore your workspace\n\nExpand a folder to browse documents. Expand a document to browse its headings.\n\n## Write and preview\n\nThe editor is on the left; the rendered preview is on the right.\n\n- **Bold** and *italic* are standard Markdown.\n- Follow [Project Notes](Notes/Project%20Notes.md) to open another document.\n\n## Read without editing\n\nChoose Read or press Ctrl+Shift+R to hide the editor.\n\n## Find Help\n\nPress F1 to keep the offline User Guide beside your work.\n";
            await File.WriteAllTextAsync(welcome, source);
            await File.WriteAllTextAsync(project, "# Project Notes\n\nA small example project for the Axiom walkthrough.\n\n## Next steps\n\n- [ ] Draft a summary\n- [ ] Review the preview\n- [ ] Export a reading copy\n\n## Meeting notes\n\nMarkdown stays readable in any text editor.\n");
            await window.SwitchWorkspaceAsync(workspace);
            await window.OpenDocumentAsync(project);
            await window.OpenDocumentAsync(welcome);
            var file = window.ExplorerNodesForTesting.Single(node => node.IsDocument);
            await window.LoadExplorerOutlineAsync(file);
            file.IsExpanded = true;
            var folder = window.ExplorerNodesForTesting.Single(node => node.Name == "Notes");
            folder.IsExpanded = true;
            await window.LoadExplorerOutlineAsync(folder.Children[0]);
            folder.Children[0].IsExpanded = true;
            window.SetEditorTextForTesting(source + "\nProtected unsaved example.\n");
            var dirty = window.EditorTextForTesting;
            var caret = window.EditorCaretOffsetForTesting;
            for (var index = 0; index < 6; index++)
            {
                window.ToggleToolWindowForTesting(index);
                await Task.Delay(100);
                window.UpdateLayout();
                Require(window.FindControl<Border>("ToolWindowHost")!.IsVisible, "Tool view did not open.");
                Require(window.FindControl<TabControl>("SidebarTopics")!.SelectedContent is Control { Bounds.Width: > 200, Bounds.Height: > 50 }, "Selected tool content was not rendered.");
                Require(window.EditorTextForTesting == dirty && window.EditorCaretOffsetForTesting == caret, "Panel change altered working source.");
            }
            await window.SaveWorkbenchForTestingAsync();
            var state = new SqliteStateStore(new AppDataPaths(Path.Combine(root, "state")));
            var layout = await state.GetWorkbenchLayoutAsync();
            Require(layout.ToolVisible && layout.ToolIndex == 5, "Layout was not persisted.");
            await window.ProtectIfNeededForTestingAsync();
            Require((await state.GetRecoverableDraftsAsync()).Any(draft => draft.Text == dirty), "Recovery draft was lost.");
            window.FilterExplorerForTesting("project");
            Require(window.ExplorerNodesForTesting.Single().Name == "Notes", "Filter did not preserve folder hierarchy.");
            window.FilterExplorerForTesting("");
            Require(window.EditorTextForTesting == dirty, "Filter altered source.");
            await window.ExecuteCommandAsync("view.resetLayout");
            Require(!window.FindControl<Border>("ToolWindowHost")!.IsVisible, "Reset did not collapse tool view.");
            Require(await File.ReadAllTextAsync(welcome) == source, "Probe altered saved Markdown.");
            window.SetEditorTextForTesting(source);
            window.UnsavedChoiceForTesting = _ => Task.FromResult("Discard");
            await window.ProtectIfNeededForTestingAsync();
            await window.ExecuteCommandAsync("view.resetLayout");
            foreach (var node in window.ExplorerNodesForTesting)
            {
                node.IsExpanded = true;
                foreach (var child in node.Children.Where(child => child.IsDocument)) { await window.LoadExplorerOutlineAsync(child); child.IsExpanded = true; }
            }
            await File.WriteAllTextAsync(Path.Combine(root, "result.json"), JsonSerializer.Serialize(new
            {
                Version = window.BuildIdentityForTesting.DisplayText
              , NativeEmbeddedViews = true
              , FilterRetainedHierarchy = true
              , WorkingSourceCaretAndRecoveryPreserved = true
              , LayoutPersisted = true
              , ResetPreservedSource = true
              , SavedMarkdownUnchanged = true
              , PackagedWalkthroughDispatchVerified = true
              , HumanAcceptance = false
              , ScreenshotCapture = "Interactive capture follows ready.txt; actual Windows screenshot evidence is separate."
            }, new JsonSerializerOptions { WriteIndented = true }));
            await File.WriteAllTextAsync(Path.Combine(root, "ready.txt"), "Native workbench checks passed; screenshot fixture is ready.");
        }
        catch (Exception exception)
        {
            await File.WriteAllTextAsync(Path.Combine(root, "error.txt"), exception.ToString());
            window.Close();
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new IOException(message);
    }
}
