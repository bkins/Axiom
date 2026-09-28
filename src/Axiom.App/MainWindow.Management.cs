using Avalonia.Controls;
using Avalonia.Interactivity;
using Axiom.Core;
using Axiom.Infrastructure;

namespace Axiom.App;

public partial class MainWindow
{
    private bool _mutationBlocked;
    private SqliteStateStore? ManagementState => _recovery as SqliteStateStore;

    private async Task InitializeManagementAsync()
    {
        if (ManagementState is null) return;
        try
        {
            var results = await new WorkspaceFileOperations(ManagementState, log: _log).ReconcilePendingAsync();
            if (results.Count > 0) UpdateChrome(string.Join("; ", results));
            _mutationBlocked = (await ManagementState.GetPendingMutationsAsync()).Count > 0;
            if (await _workspaces.GetActiveAsync() is { } active)
            {
                try { await LoadFolderAsync(active.RootPath); }
                catch (Exception exception) { ReportError("workspace.restore.failed", exception, active.RootPath); }
            }
        }
        catch (Exception exception) { _mutationBlocked = true; ReportError("mutation.reconcile.failed", exception, null); }
    }

    private async void WorkspaceCatalog_OnClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (ManagementState is null) return;
        try
        {
            var dialog = new WorkspaceCatalogWindow(await ManagementState.ListWorkspacesAsync());
            var choice = await dialog.ShowDialog<(WorkspaceRecord Workspace, bool Forget)?>(this);
            if (choice is null) return;
            if (!choice.Value.Forget)
            {
                await SwitchWorkspaceAsync(choice.Value.Workspace.RootPath);
                return;
            }
            var workspace = choice.Value.Workspace;
            if (await ChoiceDialog.ShowAsync(this, "Forget workspace", $"Forget {workspace.RootPath}? Only its remembered record and pins are removed. Files, identities, and protected drafts remain.", "Forget", "Cancel") != "Forget") return;
            var active = _activeWorkspace?.WorkspaceId == workspace.WorkspaceId;
            if (active && !await ConfirmSafeToReplaceSessionAsync()) return;
            await ManagementState.ForgetWorkspaceAsync(workspace.WorkspaceId);
            if (active)
            {
                _workspaceSearchCancellation?.Cancel();
                ++_workspaceSearchGeneration;
                _workspaceSearchResults.Clear();
                _activeWorkspace = null;
                _folder = null;
                _workspaceTree.Clear();
                _workspacePins.Clear();
                LoadSession(_documents.NewDocument(), "Workspace forgotten; its files remain on disk.");
            }
            else UpdateChrome("Workspace forgotten; its files remain on disk.");
        }
        catch (Exception exception) { ReportError("workspace.catalog.failed", exception, null); }
    }

    internal async Task<bool> SwitchWorkspaceAsync(string root)
    {
        // Validate before asking to discard the old buffer.
        await _files.ListMarkdownFilesAsync(root);
        if (!await ConfirmSafeToReplaceSessionAsync()) return false;
        await LoadFolderAsync(root);
        LoadSession(_documents.NewDocument(), "Workspace opened; choose a Markdown document.");
        return true;
    }

    private async void ManageDocument_OnClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (sender is not Control { Tag: string value } || !Enum.TryParse<DocumentMutationKind>(value, out var kind)) return;
        if (_activeWorkspace is null || ManagementState is null || _mutationBlocked)
        {
            UpdateChrome(_mutationBlocked ? "Restart and resolve pending file operations before making another change." : "Open a workspace before managing its files.");
            return;
        }
        var workspace = _activeWorkspace;
        var selected = FileTree.SelectedItem as WorkspaceTreeNode;
        var sourceRelative = selected is { IsDocument: true } ? selected.RelativePath :
            _session.Path is not null && IsWithinFolder(_session.Path, workspace.RootPath) ? Path.GetRelativePath(workspace.RootPath, _session.Path) : null;
        var isCreation = kind is DocumentMutationKind.Create or DocumentMutationKind.CreateFolder;
        if (!isCreation && sourceRelative is null) { UpdateChrome("Select a workspace document first."); return; }
        try
        {
            var destinationRelative = (string?)null;
            if (kind != DocumentMutationKind.Recycle)
            {
                var selectedFolder = selected is { IsDocument: false } ? selected.RelativePath : Path.GetDirectoryName(selected?.RelativePath) ?? "";
                var initial = kind switch
                {
                    DocumentMutationKind.Create => Path.Combine(selectedFolder, "Untitled.md"),
                    DocumentMutationKind.CreateFolder => Path.Combine(selectedFolder, "New Folder"),
                    DocumentMutationKind.Duplicate => Path.Combine(Path.GetDirectoryName(sourceRelative) ?? "", Path.GetFileNameWithoutExtension(sourceRelative) + " copy" + Path.GetExtension(sourceRelative)),
                    _ => sourceRelative!
                };
                destinationRelative = await TextEntryDialog.ShowAsync(this, kind.ToString(), $"Workspace: {workspace.RootPath}\nEnter a workspace-relative destination. Existing targets are never overwritten.", initial);
                if (destinationRelative is null) return;
                WorkspaceMutationPolicy.Resolve(workspace.RootPath, destinationRelative, kind != DocumentMutationKind.CreateFolder);
            }
            var message = $"{kind}\nSource: {(sourceRelative is null ? "New item" : Path.Combine(workspace.RootPath, sourceRelative))}\nDestination: {(destinationRelative is null ? "Windows Recycle Bin" : Path.Combine(workspace.RootPath, destinationRelative))}";
            if (kind == DocumentMutationKind.Move && Path.GetDirectoryName(sourceRelative) != Path.GetDirectoryName(destinationRelative))
                message += "\nRelative links and images may require manual repair. Axiom does not rewrite references or move assets.";
            if (kind == DocumentMutationKind.Recycle) message += "\nRestore content through Windows Recycle Bin. Pins are removed; this is not editor Undo.";
            if (await ChoiceDialog.ShowAsync(this, "Confirm " + kind, message, "Continue", "Cancel") != "Continue") return;
            var affectsActive = sourceRelative is not null && _session.Path is not null && string.Equals(Path.Combine(workspace.RootPath, sourceRelative), _session.Path, StringComparison.OrdinalIgnoreCase);
            if ((affectsActive || kind == DocumentMutationKind.Create) && !await ConfirmSafeToReplaceSessionAsync()) return;
            if (affectsActive && Editor.Text != _session.Text) SetEditorText(_session.Text);
            if (_session.Conflict is not null && affectsActive) { UpdateChrome("Resolve the active document conflict before managing its file."); return; }
            var operations = new WorkspaceFileOperations(ManagementState, log: _log);
            if (kind == DocumentMutationKind.Duplicate && _duplicates.Values.Any(paths => paths.Contains(Path.Combine(workspace.RootPath, sourceRelative!), StringComparer.OrdinalIgnoreCase)))
                throw new IOException("Resolve the source document's duplicate identity first.");
            var reviewedHash = sourceRelative is null ? null : WorkspaceFileOperations.Hash(await File.ReadAllBytesAsync(Path.Combine(workspace.RootPath, sourceRelative)));
            Editor.IsEnabled = false;
            if (affectsActive) _monitor.Watch(null);
            try
            {
                var path = await operations.ExecuteAsync(workspace, kind, sourceRelative, destinationRelative, reviewedHash);
                if (affectsActive && kind is DocumentMutationKind.Rename or DocumentMutationKind.Move && path is not null) _session.UpdatePath(path);
                if (affectsActive && kind == DocumentMutationKind.Recycle) LoadSession(_documents.NewDocument(), "Document recycled.");
                await LoadFolderAsync(workspace.RootPath);
                if (kind == DocumentMutationKind.Create && path is not null) await OpenDocumentAsync(path);
                else if (kind == DocumentMutationKind.Duplicate && path is not null &&
                         await ChoiceDialog.ShowAsync(this, "Document duplicated", $"Created {path}. Open the copy?", "Open", "Keep current") == "Open" && await ConfirmSafeToReplaceSessionAsync())
                    await OpenDocumentAsync(path);
                UpdateChrome($"{kind} completed: {destinationRelative ?? sourceRelative}");
            }
            finally { Editor.IsEnabled = true; _monitor.Watch(_session.Path); }
        }
        catch (Exception exception)
        {
            _mutationBlocked = (await ManagementState.GetPendingMutationsAsync()).Count > 0;
            ReportError("document.management.failed", exception, sourceRelative);
            await ChoiceDialog.ShowAsync(this, "Operation not completed", exception.Message, "OK");
        }
    }

    private async void BulkEnroll_OnClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (_activeWorkspace is null || ManagementState is null || _mutationBlocked) { UpdateChrome("Open an available workspace and resolve pending operations first."); return; }
        if (!await ConfirmSafeToReplaceSessionAsync()) return;
        if (Editor.Text != _session.Text) SetEditorText(_session.Text);
        if (_session.Conflict is not null) { UpdateChrome("Resolve the active conflict first."); return; }
        try
        {
            _monitor.Watch(null);
            var window = new BulkEnrollmentWindow(_activeWorkspace, new BulkEnrollmentService(new WorkspaceFileOperations(ManagementState, log: _log)));
            await window.ShowDialog<bool>(this);
            await LoadFolderAsync(_activeWorkspace.RootPath);
            if (_session.Path is not null && await _files.ExistsAsync(_session.Path)) await OpenDocumentAsync(_session.Path);
            _mutationBlocked = (await ManagementState.GetPendingMutationsAsync()).Count > 0;
        }
        catch (Exception exception) { ReportError("enrollment.batch.failed", exception, _folder); }
        finally { _monitor.Watch(_session.Path); }
    }
}
