using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Axiom.Core;

namespace Axiom.App;

internal static class WorkspaceSafetyProbe
{
    internal static string? Root { get; private set; }
    internal static void Configure(string root)
    {
        Root = Path.GetFullPath(root);
        if (Directory.Exists(Root)) throw new IOException("Choose fresh safety evidence directory.");
        Directory.CreateDirectory(Root);
        Environment.SetEnvironmentVariable("AXIOM_STATE_ROOT", Path.Combine(Root, "state"));
    }

    internal static async Task StartAsync(MainWindow window)
    {
        try
        {
            await window.StartupCompleted;
            var workspace = Path.Combine(Root!, "workspace");
            var target = Path.Combine(workspace, "target"); Directory.CreateDirectory(target);
            await File.WriteAllTextAsync(Path.Combine(target, "anchor.md"), "# Anchor");
            await File.WriteAllTextAsync(Path.Combine(workspace, "one.md"), "# One");
            await File.WriteAllTextAsync(Path.Combine(workspace, "two.md"), "# Two");
            var outside = Path.Combine(Root!, "outside.md");
            await File.WriteAllTextAsync(outside, "# Outside\n![asset](asset.png)\n");
            await File.WriteAllBytesAsync(Path.Combine(Root!, "asset.png"), [1, 2, 3]);
            await window.SwitchWorkspaceAsync(workspace);
            await window.OpenDocumentAsync(outside);
            await Task.Delay(1600); window.Topmost = true; window.Activate(); window.UpdateLayout(); await Task.Delay(250);
            var tabs = window.FindControl<ListBox>("DocumentTabs")!;
            var tab = tabs.GetVisualDescendants().OfType<ListBoxItem>().Single(item => item.DataContext is DocumentTab document && document.Session.Path == outside);
            var tree = window.FindControl<TreeView>("FileTree")!;
            TreeViewItem Target(string relative) => tree.GetVisualDescendants().OfType<TreeViewItem>().Single(item => item.DataContext is WorkspaceExplorerNode node && node.RelativePath == relative && !node.IsPinned);
            await NativeProbeInput.DragAsync(tab, Target("target"));
            await Answer(window, "Review move and references", "Move and repair");
            await window.LastExplorerMove.WaitAsync(TimeSpan.FromSeconds(15));
            var moved = Path.Combine(target, "outside.md");
            Require(!File.Exists(outside) && File.Exists(moved), "Native external-tab drop did not move the file.");
            Require((await File.ReadAllTextAsync(moved)).Contains("../../asset.png"), "External document outgoing image was not repaired.");
            Require(window.EditorTextForTesting.Contains("../../asset.png"), "Moved active buffer did not match repaired disk.");
            var editor = window.FindControl<AvaloniaEdit.TextEditor>("Editor")!;
            editor.Text += "Unsaved retained";
            var blocked = window.MoveExplorerItemForTestingAsync(Path.Combine("target", "outside.md"), "outside.md", false);
            await Answer(window, "Save or resolve affected documents", "OK");
            Require((await blocked).Outcome == CommandOutcome.Cancelled && editor.Text.EndsWith("Unsaved retained") && File.Exists(moved), "Dirty move did not retain buffer/file.");
            editor.Text = await File.ReadAllTextAsync(moved);
            await window.OpenDocumentAsync(Path.Combine(workspace, "one.md"));
            await Task.Delay(150); tree.Focus();
            await NativeProbeInput.ClickAsync(Target("one.md"));
            await Task.Delay(200); Target("one.md").Focus(); NativeProbeInput.Key(0x28, shift: true); await Task.Delay(180);
            Require(tree.SelectedItems.Count >= 2, "Native Shift+Down did not select multiple rows.");
            Require(window.FindControl<TextBlock>("WorkspaceSelectionCount")!.Text!.Contains("2 selected"), "Selection count not visible.");
            NativeProbeInput.Key(0x20, control: true); await Task.Delay(100);
            Require(tree.SelectedItems.Count == 1, "Ctrl+Space did not toggle the focused row.");
            NativeProbeInput.Key(0x20, control: true); await Task.Delay(100);
            Require(tree.SelectedItems.Count == 2, "Ctrl+Space did not restore the focused row.");
            var nodes = new[] { new WorkspaceExplorerNode("one.md", "one.md", true), new WorkspaceExplorerNode("two.md", "two.md", true) };
            var batch = window.RunWorkspaceBatchForTestingAsync("Move", nodes, target);
            await Answer(window, "Move selected workspace items", "Continue");
            await Answer(window, "Review move and references", "Move and repair");
            await Answer(window, "Review move and references", "Move and repair");
            Require((await batch).Outcome == CommandOutcome.Completed && File.Exists(Path.Combine(target, "one.md")) && File.Exists(Path.Combine(target, "two.md")), "Batch move failed.");
            await window.OpenDocumentAsync(Path.Combine(target, "one.md"));
            editor.Select(2, 3);
            Require(window.ApplyMarkdownEditForTesting(MarkdownEditKind.Strikethrough), "Strikethrough command unavailable.");
            Require(editor.Text == "# ~~One~~", "Strikethrough selection did not wrap correctly.");
            editor.Undo(); Require(editor.Text == "# One", "Strikethrough did not Undo in one step.");
            var recycleFolder = Path.Combine(workspace, "recycle-me"); Directory.CreateDirectory(recycleFolder);
            await File.WriteAllTextAsync(Path.Combine(recycleFolder, "note.md"), "protected folder note");
            await File.WriteAllBytesAsync(Path.Combine(recycleFolder, "asset.bin"), [7, 8, 9]);
            var recycle = window.RunWorkspaceBatchForTestingAsync("Recycle", [new("two.md", Path.Combine("target", "two.md"), true), new("recycle-me", "recycle-me")]);
            await Answer(window, "Recycle selected workspace items", "Continue");
            Require((await recycle).Outcome == CommandOutcome.Completed && !Directory.Exists(recycleFolder) && !File.Exists(Path.Combine(target, "two.md")), "File/folder batch recycling failed.");
            Require(Directory.GetFiles(Path.Combine(Root!, "state", "State", "Backups"), "RecycledFolder-*.zip").Length == 1, "Verified folder recycle ZIP missing.");
            await File.WriteAllTextAsync(Path.Combine(Root!, "result.json"), JsonSerializer.Serialize(new { NativeExternalTabDrop = true, ReviewedOutgoingImageRepair = true, DirtyMoveRefusal = true, NativeShiftMultiSelect = true, VisibleSelectionCount = true, TwoItemBatchMove = true, ProtectedFileFolderRecycle = true, StrikethroughOneUndo = true, Version = window.BuildIdentityForTesting.DisplayText }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception error) { await File.WriteAllTextAsync(Path.Combine(Root!, "error.txt"), error.ToString()); }
        finally { window.UnsavedChoiceForTesting = _ => Task.FromResult("Discard"); window.Close(); }
    }

    private static async Task Answer(Window window, string title, string choice)
    {
        for (var attempt = 0; attempt < 250; attempt++)
        {
            var dialog = window.OwnedWindows.SingleOrDefault(child => child.Title == title);
            if (dialog is not null)
            { dialog.GetLogicalDescendants().OfType<Button>().Single(button => button.Content?.ToString() == choice).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); return; }
            await Task.Delay(40);
        }
        throw new TimeoutException("Missing dialog: " + title);
    }
    private static void Require(bool success, string message) { if (!success) throw new IOException(message); }
}
