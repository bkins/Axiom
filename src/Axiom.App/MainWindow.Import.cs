using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Axiom.Core;
using Axiom.Infrastructure;

namespace Axiom.App;

public partial class MainWindow
{
    private bool _importRunning;
    internal Task LastFileDrop { get; private set; } = Task.CompletedTask;

    private void InitializeFileDrop()
    {
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, Files_DragOver, RoutingStrategies.Bubble);
        AddHandler(DragDrop.DropEvent, Files_Drop, RoutingStrategies.Bubble);
    }

    private void Files_DragOver(object? sender, DragEventArgs eventArgs)
    {
        if (!eventArgs.DataTransfer.Contains(DataFormat.File)) return;
        eventArgs.DragEffects = !_importRunning && !_sessionTransitionRunning && !_navigationRunning
            ? DragDropEffects.Copy : DragDropEffects.None;
        eventArgs.Handled = true;
    }

    private void Files_Drop(object? sender, DragEventArgs eventArgs)
    {
        if (!eventArgs.DataTransfer.Contains(DataFormat.File)) return;
        eventArgs.Handled = true;
        var paths = eventArgs.DataTransfer.TryGetFiles()?.Select(file => file.TryGetLocalPath()).OfType<string>().ToArray() ?? [];
        LastFileDrop = HandleDroppedFilesAsync(paths);
    }

    internal async Task HandleDroppedFilesAsync(IReadOnlyList<string> paths)
    {
        if (_importRunning || _sessionTransitionRunning || _navigationRunning) return;
        _importRunning = true;
        _sessionTransitionRunning = true;
        SetSessionCommandsEnabled(false);
        try
        {
            foreach (var path in paths)
            {
                try
                {
                    if (WorkspaceImportService.IsMarkdown(path)) await OpenDocumentAsync(path);
                    else if (WorkspaceImportService.IsImage(path)) await ImportFileAsync(path, true);
                    else UpdateChrome($"Unsupported dropped file: {Path.GetFileName(path)}. Drop Markdown to open or an image to import.");
                }
                catch (Exception exception)
                {
                    ReportError("file.drop.failed", exception, path);
                    await ChoiceDialog.ShowAsync(this, "Dropped file not opened", $"{path}\n{exception.Message}", "Continue");
                }
            }
        }
        finally { _importRunning = false; _sessionTransitionRunning = false; SetSessionCommandsEnabled(true); }
    }

    private async void CopyIntoWorkspace_OnClick(object? sender, RoutedEventArgs eventArgs)
        => await ExecuteCommandAsync("workspace.importMarkdown");

    private async void ImportImage_OnClick(object? sender, RoutedEventArgs eventArgs)
        => await ExecuteCommandAsync("workspace.importImage");

    private async Task<CommandResult> ImportIntoWorkspaceAsync(bool image, CancellationToken cancellationToken)
    {
        if (_importRunning || _sessionTransitionRunning || _navigationRunning) return new(CommandOutcome.Unavailable, "Another document operation is running.");
        _importRunning = true;
        _sessionTransitionRunning = true;
        SetSessionCommandsEnabled(false);
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = image ? "Import an image into workspace" : "Copy Markdown into workspace — originals stay untouched"
              , AllowMultiple = false
              , FileTypeFilter = [new FilePickerFileType(image ? "Images" : "Markdown") { Patterns = image ? [.. MarkdownImageReferenceService.FilePickerPatterns] : ["*.md", "*.markdown"] }]
            });
            if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return new(CommandOutcome.Cancelled, "Import source cancelled.");
            return new(await ImportFileAsync(path, image, cancellationToken) ? CommandOutcome.Completed : CommandOutcome.Cancelled, "Workspace import reviewed.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return new(CommandOutcome.Cancelled, "Import cancelled; any prepared receipt remains protected for reconciliation."); }
        catch (Exception exception)
        {
            ReportError(image ? "image.import.failed" : "file.import.failed", exception, null);
            await ChoiceDialog.ShowAsync(this, "File not imported", exception.Message, "OK");
            return new(CommandOutcome.Failed, exception.Message, exception);
        }
        finally { _importRunning = false; _sessionTransitionRunning = false; SetSessionCommandsEnabled(true); }
    }

    private async Task<bool> ImportFileAsync(string source, bool image, CancellationToken cancellationToken = default)
    {
        if (_activeWorkspace is not { } workspace || ManagementState is null || _mutationBlocked)
            throw new IOException("Open a workspace and resolve pending operations before importing.");
        if (image && !CanInsertImageReference())
            throw new IOException("Save the active document inside the workspace before importing an image.");
        var service = new WorkspaceImportService(new WorkspaceFileOperations(ManagementState, log: _log));
        var content = await service.ReviewAsync(source, image, cancellationToken);
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = image ? "Choose an existing workspace asset folder" : "Choose an existing destination folder inside workspace"
          , AllowMultiple = false
          , SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(workspace.RootPath)
        });
        if (folders.FirstOrDefault()?.TryGetLocalPath() is not { } folder) return false;
        var name = await TextEntryDialog.ShowAsync(this, image ? "Image filename" : "Copied document filename", "Choose a filename. Duplicate names are rejected; existing files are never replaced.", Path.GetFileName(source));
        if (name is null) return false;
        if (name.Contains('/') || name.Contains('\\')) throw new ArgumentException("Enter a filename without folders.");
        if (image && !Path.GetExtension(name).Equals(Path.GetExtension(source), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Retain the original image extension.");
        var relative = Path.GetRelativePath(workspace.RootPath, Path.Combine(folder, name));
        var destination = WorkspaceMutationPolicy.Resolve(workspace.RootPath, relative, !image);
        WorkspaceFileOperations.ValidateAncestors(workspace.RootPath, destination);
        if (await ChoiceDialog.ShowAsync(this, "Confirm copy into workspace", $"Source: {source}\nDestination: {destination}\nCopy the reviewed {content.Length:N0} bytes? Original files stay untouched. Enrolled Markdown copies receive a new identity. Relative links are not rewritten." + (image ? "\nA relative image reference will be inserted in the active document. The imported file remains if you undo the insertion." : ""), "Copy", "Cancel") != "Copy") return false;
        try
        {
            var path = await service.CopyReviewedAsync(workspace, relative, content, image, cancellationToken);
            await LoadFolderAsync(workspace.RootPath);
            if (path is not null && image)
            {
                if (!InsertImageReference(path))
                    await ChoiceDialog.ShowAsync(this, "Image copied; reference not inserted", $"The image remains at {path}. {StatusText.Text}", "OK");
            }
            else if (path is not null) await OpenDocumentAsync(path);
            UpdateChrome($"Copied {relative}; original retained.");
            return true;
        }
        catch
        {
            _mutationBlocked = (await ManagementState.GetPendingMutationsAsync()).Count > 0;
            throw;
        }
    }
}
