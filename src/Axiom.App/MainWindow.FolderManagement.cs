using Avalonia.Platform.Storage;
using Axiom.Core;
using Axiom.Infrastructure;

namespace Axiom.App;

public partial class MainWindow
{
    private async Task<CommandResult> ManageFolderAsync(WorkspaceExplorerNode node, DocumentMutationKind kind,
        string? destinationRelative, CancellationToken cancellationToken)
    {
        if (_activeWorkspace is not { } workspace || ManagementState is null || _mutationBlocked || _sessionTransitionRunning || _navigationRunning)
            return new(CommandOutcome.Unavailable, "Open an available workspace and finish the current operation first.");
        var source = WorkspaceMutationPolicy.Resolve(workspace.RootPath, node.RelativePath, false);
        _sessionTransitionRunning = true;
        SetSessionCommandsEnabled(false);
        var original = _activeTab;
        try
        {
            var operations = new WorkspaceFileOperations(ManagementState, log: _log);
            var reviewedHash = await operations.ReviewFolderAsync(workspace, node.RelativePath, cancellationToken);
            if (destinationRelative is null && kind == DocumentMutationKind.Move)
            {
                var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "Move folder — choose a folder inside this workspace"
                  , AllowMultiple = false
                  , SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(workspace.RootPath)
                });
                if (folders.FirstOrDefault()?.TryGetLocalPath() is not { } folder) return new(CommandOutcome.Cancelled, "Folder move cancelled.");
                destinationRelative = Path.GetRelativePath(workspace.RootPath, Path.Combine(folder, Path.GetFileName(source)));
            }
            destinationRelative ??= await TextEntryDialog.ShowAsync(this, "Rename folder", "Enter a workspace-relative name in the same parent folder.", node.RelativePath);
            if (destinationRelative is null) return new(CommandOutcome.Cancelled, "Folder rename cancelled.");
            var destination = WorkspaceMutationPolicy.Resolve(workspace.RootPath, destinationRelative, false);
            if (kind == DocumentMutationKind.Rename && !string.Equals(Path.GetDirectoryName(source), Path.GetDirectoryName(destination), WorkspacePathBoundary.Comparison))
                throw new ArgumentException("Rename keeps the parent folder. Use Move for another folder.");
            if (await ChoiceDialog.ShowAsync(this, "Confirm folder move", $"Source: {source}\nDestination: {destination}\nAll descendants, including images and other files, move together. Relative references outside the subtree may require manual repair. Existing targets are never merged or overwritten. Review unsaved descendant documents next.", "Continue", "Cancel") != "Continue")
                return new(CommandOutcome.Cancelled, "Folder retained.");
            var affected = _tabs.Where(tab => tab.Session.Path is { } path && WorkspacePathBoundary.Contains(source, path)).ToArray();
            foreach (var tab in affected)
            {
                await SelectTabAsync(tab);
                if (!await ConfirmSafeToReplaceSessionAsync()) return new(CommandOutcome.Cancelled, "Folder retained; dirty document review cancelled.");
                if (tab.Session.Conflict is not null) return new(CommandOutcome.Unavailable, "Resolve descendant document conflicts first.");
            }
            // Saving during review legitimately changes the snapshot; preflight again
            // after all dirty buffers have been explicitly reviewed.
            reviewedHash = await operations.ReviewFolderAsync(workspace, node.RelativePath, cancellationToken);
            Editor.IsEnabled = false;
            _monitor.Watch(null);
            await operations.MoveFolderAsync(workspace, node.RelativePath, destinationRelative, reviewedHash, cancellationToken);
            foreach (var tab in affected)
                tab.Session.UpdatePath(Path.Combine(destination, Path.GetRelativePath(source, tab.Session.Path!)));
            _backLocations.Clear();
            _forwardLocations.Clear();
            await LoadFolderAsync(workspace.RootPath);
            if (_tabs.Contains(original)) await SelectTabAsync(original);
            RefreshTabLabels();
            RevealActiveExplorerDocument();
            UpdateChrome($"Folder moved to {destinationRelative}; references outside the subtree may need repair.");
            return new(CommandOutcome.Completed, "Folder and descendant state moved.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _mutationBlocked = (await ManagementState.GetPendingMutationsAsync()).Count > 0;
            return new(CommandOutcome.Cancelled, "Folder operation cancelled; inspect pending receipt before further changes.");
        }
        catch (Exception exception)
        {
            _mutationBlocked = (await ManagementState.GetPendingMutationsAsync()).Count > 0;
            ReportError("folder.move.failed", exception, source);
            await ChoiceDialog.ShowAsync(this, "Folder operation not completed", exception.Message, "OK");
            return new(CommandOutcome.Failed, exception.Message, exception);
        }
        finally { Editor.IsEnabled = true; _sessionTransitionRunning = false; SetSessionCommandsEnabled(true); RefreshDocumentMonitors(); }
    }
}
