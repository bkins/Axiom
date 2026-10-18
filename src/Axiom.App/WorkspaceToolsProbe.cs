using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Axiom.Core;
using Axiom.Infrastructure;

namespace Axiom.App;

internal static class WorkspaceToolsProbe
{
    internal static string? Root { get; private set; }
    internal static void Configure(string root)
    {
        Root = Path.GetFullPath(root);
        if (Directory.Exists(Root)) throw new IOException("Choose fresh workspace-tools evidence.");
        Directory.CreateDirectory(Root); Environment.SetEnvironmentVariable("AXIOM_STATE_ROOT", Path.Combine(Root, "state"));
    }

    internal static async Task StartAsync(MainWindow window)
    {
        try
        {
            await window.StartupCompleted;
            var root = Path.Combine(Root!, "workspace"); Directory.CreateDirectory(Path.Combine(root, "source", "empty")); Directory.CreateDirectory(Path.Combine(root, "target"));
            var identity = Guid.NewGuid();
            await File.WriteAllTextAsync(Path.Combine(root, "source", "note.md"), DocumentIdCodec.Enroll("# Note\n[shared](../a.md)\n![asset](asset.bin)", identity));
            await File.WriteAllBytesAsync(Path.Combine(root, "source", "asset.bin"), [1, 2, 3]);
            await File.WriteAllTextAsync(Path.Combine(root, "a.md"), "# Alpha"); await File.WriteAllTextAsync(Path.Combine(root, "b.md"), "# Bravo");
            await File.WriteAllTextAsync(Path.Combine(root, "references.md"), "[A](a.md)");
            await window.SwitchWorkspaceAsync(root);
            await Task.Delay(900); window.Topmost = true; window.Activate(); window.UpdateLayout();
            var tree = window.FindControl<TreeView>("FileTree")!;
            TreeViewItem Item(string relative) => tree.GetVisualDescendants().OfType<TreeViewItem>().Single(item => item.DataContext is WorkspaceExplorerNode { IsPinned: false } node && node.RelativePath == relative);
            await NativeProbeInput.ClickAsync(Item("source"));
            var formatCancel = window.ExecuteCommandAsync("workspace.batch.export");
            await Answer(window, "Export format", "Cancel");
            Require((await formatCancel).Outcome == CommandOutcome.Cancelled, "Native format chooser cancellation did not complete.");
            var copy = window.ExecuteCommandAsync("workspace.batch.copy");
            await Answer(window, "Copy workspace folder", "Continue", "target/copy");
            await Answer(window, "Review folder copy", "Copy");
            Require((await copy).Outcome == CommandOutcome.Completed, "Folder-copy command failed.");
            var copiedPath = Path.Combine(root, "target", "copy", "note.md");
            var copied = await File.ReadAllTextAsync(copiedPath);
            Require(DocumentIdCodec.Find(copied) is { } copiedId && copiedId != identity && copied.Contains("../../a.md") && Directory.Exists(Path.Combine(root, "target", "copy", "empty")), "Copied identity/reference/empty-folder policy failed.");
            await NativeProbeInput.ClickAsync(Item("a.md")); await Task.Delay(150); Item("a.md").Focus(); NativeProbeInput.Key(0x28, shift: true); await Task.Delay(150);
            Require(tree.SelectedItems.Count == 2, "Native rename selection failed.");
            var rename = window.ExecuteCommandAsync("workspace.batch.rename");
            await Answer(window, "Rename", "Continue", "Renamed-{n}-{name}");
            await Answer(window, "Rename files", "Continue");
            await Answer(window, "Review move and references", "Move and repair");
            await Answer(window, "Review move and references", "Move and repair");
            Require((await rename).Outcome == CommandOutcome.Completed, "Batch rename command failed.");
            var renamed = Path.Combine(root, "Renamed-1-a.md");
            Require(File.Exists(renamed) && File.Exists(Path.Combine(root, "Renamed-2-b.md")) && (await File.ReadAllTextAsync(Path.Combine(root, "references.md"))).Contains("Renamed-1-a.md"), "Batch rename did not preserve links/names.");
            var partial = window.RenameWorkspaceSelectionAsync([new("a", "Renamed-1-a.md", true), new("b", "Renamed-2-b.md", true)], "Again-{n}-{name}");
            await Answer(window, "Rename files", "Continue");
            await Answer(window, "Review move and references", "Move and repair");
            await Answer(window, "Review move and references", "Cancel");
            var partialResult = await partial;
            renamed = Path.Combine(root, "Again-1-Renamed-1-a.md");
            Require(partialResult.Outcome == CommandOutcome.Cancelled && partialResult.Message.Contains("1/2") && File.Exists(renamed) && File.Exists(Path.Combine(root, "Renamed-2-b.md")), "Partial rename cancellation did not report/retain exact results.");
            await window.OpenDocumentAsync(renamed); window.SetEditorTextForTesting("# Unsaved export snapshot");
            var selection = new[] { new WorkspaceExplorerNode("a", "Again-1-Renamed-1-a.md", true), new WorkspaceExplorerNode("b", "Renamed-2-b.md", true) };
            var export = window.ExportWorkspaceSelectionAsync(selection, Path.Combine(Root!, "output"), "html");
            await Answer(window, "Export files", "Export");
            Require((await export).Outcome == CommandOutcome.Completed && (await File.ReadAllTextAsync(Path.Combine(Root!, "output", "Again-1-Renamed-1-a.html"))).Contains("Unsaved export snapshot"), "Bulk export did not include the open unsaved buffer.");
            Require(await File.ReadAllTextAsync(renamed) == "# Alpha" && window.EditorTextForTesting == "# Unsaved export snapshot", "Bulk export saved or changed source/buffer.");
            var cancel = window.ExportWorkspaceSelectionAsync(selection, Path.Combine(Root!, "cancelled-output"), "txt");
            await Answer(window, "Export files", "Cancel");
            Require((await cancel).Outcome == CommandOutcome.Cancelled && !Directory.Exists(Path.Combine(Root!, "cancelled-output")), "Cancelled export published output.");
            await File.WriteAllTextAsync(Path.Combine(Root!, "result.json"), JsonSerializer.Serialize(new { Version = window.BuildIdentityForTesting.DisplayText, NativeFolderSelection = true, NativeFormatChooserCancel = true, RegisteredFolderCopyReview = true, FreshCopiedIdentityAndReferences = true, NativeRangeRenameSelection = true, RegisteredBatchRenameAndLinkRepair = true, PartialRenameCancellation = true, OpenUnsavedBufferExport = true, SourceAndBufferRetained = true, CancelledExportUnpublished = true }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception error) { await File.WriteAllTextAsync(Path.Combine(Root!, "error.txt"), error.ToString()); }
        finally { window.UnsavedChoiceForTesting = _ => Task.FromResult("Discard"); window.Close(); }
    }

    private static async Task Answer(Window owner, string title, string choice, string? text = null)
    {
        for (var at = 0; at < 300; at++)
        {
            if (owner.OwnedWindows.SingleOrDefault(window => window.Title == title) is { } dialog)
            {
                if (text is not null) dialog.GetLogicalDescendants().OfType<TextBox>().Single().Text = text;
                dialog.Topmost = true; dialog.Activate(); await Task.Delay(200); dialog.UpdateLayout();
                await NativeProbeInput.ClickAsync(dialog.GetLogicalDescendants().OfType<Button>().Single(button => button.Content?.ToString() == choice)); return;
            }
            await Task.Delay(30);
        }
        throw new TimeoutException("Missing review dialog: " + title + "; open: " + string.Join("; ", owner.OwnedWindows.Select(window => window.Title + ": " + string.Join(" | ", window.GetLogicalDescendants().OfType<TextBlock>().Select(text => text.Text)))));
    }
    private static void Require(bool value, string message) { if (!value) throw new IOException(message); }
}
