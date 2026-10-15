using Avalonia.Platform.Storage;
using Axiom.Core;
using Axiom.Infrastructure;

namespace Axiom.App;

public partial class MainWindow
{
    private async Task<CommandResult> ReviewMoveAsync(string source, string? destination, bool rename, CancellationToken token)
    {
        if (_activeWorkspace is not { } workspace || ManagementState is null || _mutationBlocked || _sessionTransitionRunning || _navigationRunning)
            return new(CommandOutcome.Unavailable, "Finish the current operation and open a workspace first.");
        _sessionTransitionRunning = true;
        SetSessionCommandsEnabled(false);
        var original = _activeTab;
        try
        {
            if (destination is null)
            {
                if (rename)
                {
                    var name = await TextEntryDialog.ShowAsync(this, "Rename", "Enter the new name in the same folder.", Path.GetFileName(source));
                    if (name is null) return new(CommandOutcome.Cancelled, "Rename cancelled.");
                    if (name != Path.GetFileName(name)) throw new IOException("Rename requires a filename only.");
                    destination = Path.Combine(Path.GetDirectoryName(source)!, name);
                }
                else
                {
                    var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Move into workspace folder", AllowMultiple = false,
                        SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(workspace.RootPath) });
                    if (folders.FirstOrDefault()?.TryGetLocalPath() is not { } folder) return new(CommandOutcome.Cancelled, "Move cancelled.");
                    destination = Path.Combine(folder, Path.GetFileName(source));
                }
            }
            var service = new ReviewedWorkspaceMove(ManagementState);
            var plan = await service.ReviewAsync(workspace, source, destination, token);
            var changed = plan.Files.Where(file => file.Edits.Count > 0).ToArray();
            var affected = _tabs.Where(tab => tab.Session.Path is { } path &&
                (path.Equals(source, WorkspacePathBoundary.Comparison) || plan.Folder && WorkspacePathBoundary.Contains(source, path) ||
                 changed.Any(file => file.Source.Equals(path, WorkspacePathBoundary.Comparison)))).ToArray();
            // No dirty buffer is silently saved, discarded or rewritten by a multi-file operation.
            if (affected.Any(tab => tab.Session.IsDirty || tab.Session.Conflict is not null))
            {
                await ChoiceDialog.ShowAsync(this, "Save or resolve affected documents", "This move affects open documents with unsaved edits or conflicts. Save them or resolve conflicts explicitly, then review the move again. Nothing was moved or saved.", "OK");
                return new(CommandOutcome.Cancelled, "Unsaved documents retained; save affected documents before moving.");
            }
            var details = string.Join("\n", changed.SelectMany(file => new[] { Path.GetRelativePath(workspace.RootPath, file.Source) }
                .Concat(file.Edits.Select(edit => "  " + edit.Before + " → " + edit.After))));
            var message = $"Source: {source}\nDestination: {destination}\n\n{changed.Length} documents have relative link/image repairs:\n{details}\n\n" +
                string.Join("\n", plan.Warnings.Distinct()) + "\n\nRepair changes the listed saved files. Move only retains their original bytes. Existing targets are never overwritten.";
            var choice = await ChoiceDialog.ShowAsync(this, "Review move and references", message, "Move and repair", "Move only", "Cancel");
            if (choice == "Cancel") return new(CommandOutcome.Cancelled, "Move cancelled; files retained.");
            Editor.IsEnabled = false;
            _monitor.Watch(null);
            await service.ApplyAsync(plan, choice == "Move and repair", token);
            foreach (var tab in affected)
            {
                var file = plan.Files.SingleOrDefault(file => file.Source.Equals(tab.Session.Path, WorkspacePathBoundary.Comparison));
                if (file is not null) tab.Session.UpdatePath(file.Destination);
                else if (plan.Folder && tab.Session.Path is { } path && WorkspacePathBoundary.Contains(source, path)) tab.Session.UpdatePath(Path.Combine(destination, Path.GetRelativePath(source, path)));
                if (tab.Session.Path is { } saved)
                {
                    var read = await _files.ReadAsync(saved);
                    if (tab.Document.Text != read.Text) tab.Document.Replace(0, tab.Document.TextLength, read.Text);
                    tab.Session.Reload(read);
                }
            }
            await LoadFolderAsync(workspace.RootPath);
            if (_tabs.Contains(original)) await SelectTabAsync(original);
            SetEditorText(_session.Text);
            RefreshTabLabels();
            _backLocations.Clear(); _forwardLocations.Clear();
            await PersistSessionLocationsAsync(requireClean: false);
            RevealActiveExplorerDocument();
            return new(CommandOutcome.Completed, "Move completed" + (choice == "Move and repair" ? "; reviewed references repaired." : "; references retained."));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return new(CommandOutcome.Cancelled, "Move cancelled; any interrupted receipt is retained for restart."); }
        catch (Exception error)
        {
            ReportError("workspace.reviewedMove.failed", error, source);
            await ChoiceDialog.ShowAsync(this, "Move not completed", error.Message, "OK");
            return new(CommandOutcome.Failed, error.Message, error);
        }
        finally
        {
            _mutationBlocked = new ReviewedWorkspaceMove(ManagementState).Pending || (await ManagementState.GetPendingMutationsAsync()).Count > 0;
            Editor.IsEnabled = true; _sessionTransitionRunning = false; SetSessionCommandsEnabled(true); RefreshDocumentMonitors();
        }
    }

    private Task<CommandResult> MoveCommandAsync(CommandContext context, bool rename, CancellationToken token)
    {
        var node = ExplorerCommandNode(context);
        var source = node is not null && _folder is not null ? Path.Combine(_folder, node.RelativePath) : context.DocumentPath;
        if (source is null) return Task.FromResult(new CommandResult(CommandOutcome.Unavailable, "Save or select a document first."));
        return ReviewMoveAsync(source, _requestedMoveDestination is null || _folder is null ? null : Path.Combine(_folder, _requestedMoveDestination), rename, token);
    }
}
