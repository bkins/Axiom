using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using AvaloniaEdit;
using Axiom.Core;
using Axiom.Infrastructure;

namespace Axiom.App;

internal static class WorkspaceNavigationProbe
{
    internal static string? Root { get; private set; }
    internal static void Configure(string root)
    {
        Root = Path.GetFullPath(root);
        if (Directory.Exists(Root)) throw new IOException("Choose a fresh navigation evidence directory.");
        Directory.CreateDirectory(Root);
        Environment.SetEnvironmentVariable("AXIOM_STATE_ROOT", Path.Combine(Root, "state"));
    }

    internal static async Task StartAsync(MainWindow window)
    {
        try
        {
            await window.StartupCompleted;
            var workspaceRoot = Path.Combine(Root!, "workspace");
            Directory.CreateDirectory(Path.Combine(workspaceRoot, "nested", "deep"));
            Directory.CreateDirectory(Path.Combine(workspaceRoot, "target"));
            await File.WriteAllTextAsync(Path.Combine(workspaceRoot, "target", "existing.md"), "# Target\n");
            var path = Path.Combine(workspaceRoot, "nested", "deep", "source.md");
            var source = "# Saved\n\nSome source text.\n";
            await File.WriteAllTextAsync(path, source);
            await File.WriteAllBytesAsync(Path.Combine(workspaceRoot, "nested", "deep", "image.png"), [1, 2, 3]);
            await window.SwitchWorkspaceAsync(workspaceRoot);
            var editor = window.FindControl<TextEditor>("Editor")!;
            editor.TextArea.Focus();
            await window.OpenDocumentAsync(path);
            var tree = window.FindControl<TreeView>("FileTree")!;
            Require(tree.SelectedItem is WorkspaceExplorerNode { IsDocument: true } selected && selected.RelativePath == Path.GetRelativePath(workspaceRoot, path), "Opening did not select the canonical explorer file.");
            Require(editor.TextArea.IsKeyboardFocusWithin, "Explorer selection stole editor focus.");
            window.FilterExplorerForTesting("unrelated");
            await window.NewTabForTestingAsync();
            await window.SelectTabForTestingAsync(window.TabsForTesting.Single(tab => tab.Session.Path == path));
            Require(window.FindControl<TextBox>("ExplorerFilter")!.Text == "unrelated", "Tab selection cleared the filter.");
            window.FilterExplorerForTesting("");
            var target = window.ExplorerNodesForTesting.Single(node => node.Name == "target");
            tree.SelectedItem = target;
            await window.ExecuteCommandAsync("document.new");
            Require(window.TabsForTesting.Last().SuggestedSaveFolder == Path.Combine(workspaceRoot, "target") && window.TabsForTesting.Last().Session.Path is null, "New did not retain its unsaved folder default.");
            var draftTab = window.TabsForTesting.Last();
            window.SetEditorTextForTesting("Protected new draft");
            await window.ProtectIfNeededForTestingAsync();
            await window.ExecuteExplorerCommandForTestingAsync("workspace.pinItem", Path.GetRelativePath(workspaceRoot, path));
            Require(window.TabsForTesting.Last() == draftTab && window.EditorTextForTesting == "Protected new draft", "Pinning another file switched or changed the draft.");
            Require(window.ExplorerNodesForTesting[0].IsGroup && window.ExplorerNodesForTesting[0].Children.Single().IsPinned, "Pinned file absent at explorer top.");
            var menu = window.WorkspaceContextMenuForTesting("nested", true);
            Require(menu.Items.OfType<MenuItem>().Single(item => item.Header?.ToString() == "Duplicate workspace document").IsEnabled == false, "Folder duplicate incorrectly enabled.");
            await window.ExecuteExplorerCommandForTestingAsync("workspace.copyFile", Path.GetRelativePath(workspaceRoot, path));
            var paste = window.ExecuteExplorerCommandForTestingAsync("workspace.pasteFile", "target", true);
            await AnswerAsync(window, "Paste file", "Continue", "target/copy.md");
            await AnswerAsync(window, "Confirm file paste", "Paste");
            Require((await paste).Outcome == CommandOutcome.Completed, "Context paste failed.");
            Require(await File.ReadAllTextAsync(Path.Combine(workspaceRoot, "target", "copy.md")) == source, "Ordinary file paste changed the saved bytes.");
            var sourceTab = window.TabsForTesting.Single(tab => tab.Session.Path == path);
            await window.SelectTabForTestingAsync(sourceTab);
            window.SetEditorTextForTesting(source + "Unsaved descendant");
            await window.ProtectIfNeededForTestingAsync();
            window.UnsavedChoiceForTesting = _ => Task.FromResult("Cancel");
            var cancelled = window.MoveExplorerItemForTestingAsync("nested", "target/nested", true);
            await AnswerAsync(window, "Save or resolve affected documents", "OK");
            Require((await cancelled).Outcome == CommandOutcome.Cancelled && File.Exists(path) && window.EditorTextForTesting.EndsWith("Unsaved descendant"), "Dirty descendant cancellation did not retain source and buffer.");
            window.UnsavedChoiceForTesting = _ => Task.FromResult("Discard");
            window.SetEditorTextForTesting(source);
            var moved = window.RouteExplorerDropForTestingAsync("nested", "target", true);
            await AnswerAsync(window, "Review move and references", "Move and repair");
            await moved;
            var destination = Path.Combine(workspaceRoot, "target", "nested", "deep", "source.md");
            Require(sourceTab.Session.Path == destination && await File.ReadAllTextAsync(destination) == source, "Folder move lost the original tab/path/bytes.");
            Require(await File.ReadAllBytesAsync(Path.Combine(workspaceRoot, "target", "nested", "deep", "image.png")) is [1, 2, 3], "Folder image did not move intact.");
            var state = new SqliteStateStore(new AppDataPaths(Path.Combine(Root!, "state")));
            Require((await state.GetRecoverableDraftsAsync()).Any(draft => draft.Text == "Protected new draft"), "Unrelated protected draft was lost.");
            Require(window.WorkspacePinsForTesting.Single().RelativePath == Path.GetRelativePath(workspaceRoot, destination), "Folder pin did not update.");
            Require(!File.Exists(path), "Old folder source remains.");
            await File.WriteAllTextAsync(Path.Combine(Root!, "result.json"), JsonSerializer.Serialize(new
            {
                Version = window.BuildIdentityForTesting.DisplayText
              , NativeExplorerSelectionFocusAndFilter = true
              , UnsavedNewFolderDefault = true
              , PinContextRetainsInactiveDirtyTab = true
              , ContextCopyPasteSavedBytes = true
              , DirtyDescendantMoveCancellation = true
              , ReviewedFolderMoveTabsPinsAssets = true
              , NativeRoutedInternalDrop = true
              , UnrelatedRecoveryRetained = true
              , HumanChecks = "Physical drag gesture, context-menu invocation, keyboard/accessibility, themes/DPI and release acceptance remain pending. Move commit uses the same command path as drop; this does not certify an OS drag gesture."
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception exception) { await File.WriteAllTextAsync(Path.Combine(Root!, "error.txt"), exception.ToString()); }
        finally { window.UnsavedChoiceForTesting = _ => Task.FromResult("Discard"); window.Close(); }
    }

    private static async Task AnswerAsync(Window owner, string title, string button, string? text = null)
    {
        var until = DateTime.UtcNow.AddSeconds(12);
        while (DateTime.UtcNow < until)
        {
            var dialog = owner.OwnedWindows.SingleOrDefault(dialog => dialog.Title == title);
            if (dialog is not null)
            {
                if (text is not null) dialog.GetLogicalDescendants().OfType<TextBox>().Single().Text = text;
                dialog.GetLogicalDescendants().OfType<Button>().Single(candidate => candidate.Content?.ToString() == button).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                return;
            }
            await Task.Delay(40);
        }
        throw new TimeoutException("Native dialog did not open: " + title);
    }

    private static void Require(bool condition, string message) { if (!condition) throw new IOException(message); }
}
