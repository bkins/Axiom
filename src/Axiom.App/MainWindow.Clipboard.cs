using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Axiom.Core;
using Axiom.Infrastructure;

namespace Axiom.App;

public partial class MainWindow
{
    internal Func<Task<Bitmap?>>? ClipboardImageForTesting { get; set; }
    private async Task<CommandResult> PasteEditorClipboardAsync(CancellationToken cancellationToken)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null) return new(CommandOutcome.Unavailable, "Clipboard is unavailable.");
        var source = Editor.Text;
        var draft = _session.DraftKey;
        var text = await clipboard.TryGetTextAsync();
        if (source != Editor.Text || draft != _session.DraftKey) return new(CommandOutcome.Cancelled, "The document changed while reading the clipboard.");
        if (text is not null)
        {
            Editor.TextArea.Focus(); Editor.Paste();
            return new(CommandOutcome.Completed, "Pasted clipboard text.");
        }
        return await PasteClipboardImageAsync(cancellationToken);
    }
    private async Task<CommandResult> PasteClipboardImageAsync(CancellationToken cancellationToken = default)
    {
        if (!CanInsertImageReference() || _activeWorkspace is not { } workspace || ManagementState is null || _mutationBlocked)
            return new(CommandOutcome.Unavailable, "Save this document inside the active workspace and resolve pending operations before pasting an image.");
        if (SelectionTouchesIdentityLine(Editor.Text, Editor.SelectionStart, Editor.SelectionLength))
            return new(CommandOutcome.Cancelled, "Choose a position outside the document identity line.");
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null) return new(CommandOutcome.Unavailable, "Clipboard is unavailable.");
        _sessionTransitionRunning = true;
        SetSessionCommandsEnabled(false);
        Editor.IsEnabled = false;
        try
        {
            using var bitmap = ClipboardImageForTesting is { } fixture ? await fixture() : await clipboard.TryGetBitmapAsync();
            if (bitmap is null) return new(CommandOutcome.Cancelled, "Clipboard has no supported bitmap image.");
            if ((long)bitmap.PixelSize.Width * bitmap.PixelSize.Height > 16_000_000) throw new IOException("Clipboard image exceeds 16 million pixels. Resize it before pasting.");
            using var encoded = new MemoryStream();
            bitmap.Save(encoded, PngBitmapEncoderOptions.Default);
            if (encoded.Length > 20 * 1024 * 1024) throw new IOException("Clipboard PNG exceeds the 20 MiB asset limit.");
            var folder = Path.Combine(Path.GetDirectoryName(_session.Path!)!, "assets");
            WorkspaceFileOperations.ValidateAncestors(workspace.RootPath, folder);
            var operations = new WorkspaceFileOperations(ManagementState, log: _log);
            if (!Directory.Exists(folder))
                await operations.ExecuteAsync(workspace, DocumentMutationKind.CreateFolder, null, Path.GetRelativePath(workspace.RootPath, folder), cancellationToken: cancellationToken);
            var relative = Path.GetRelativePath(workspace.RootPath, Path.Combine(folder, "clipboard-" + Guid.NewGuid().ToString("N") + ".png"));
            var path = await new WorkspaceImportService(operations).CopyReviewedAsync(workspace, relative, encoded.ToArray(), true, cancellationToken);
            if (path is null || !InsertImageReference(path))
                return new(CommandOutcome.Failed, "Clipboard asset was copied but its reference could not be inserted. Inspect " + relative);
            UpdateChrome("Pasted image into " + relative + ". Save the Markdown deliberately; Undo removes the reference and retains the asset.");
            return new(CommandOutcome.Completed, "Clipboard image pasted; Markdown is unsaved.");
        }
        catch (Exception exception)
        {
            _mutationBlocked = (await ManagementState.GetPendingMutationsAsync()).Count > 0;
            ReportError("editor.clipboard.image.failed", exception, _session.Path);
            return new(CommandOutcome.Failed, exception.Message, exception);
        }
        finally { _sessionTransitionRunning = false; Editor.IsEnabled = true; SetSessionCommandsEnabled(true); Editor.TextArea.Focus(); }
    }
}
