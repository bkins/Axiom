using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
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
                try { await LoadFolderAsync(active.RootPath); await RestoreCleanSessionAsync(); }
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
            if (active)
            {
                await RunSessionTransitionAsync(async () =>
                {
                    await ManagementState.ForgetWorkspaceAsync(workspace.WorkspaceId);
                    _workspaceSearchCancellation?.Cancel();
                    ++_workspaceSearchGeneration;
                    _workspaceSearchResults.Clear();
                    _activeWorkspace = null;
                    _folder = null;
                    ++_backlinkGeneration;
                    _backlinkCancellation?.Cancel();
                    _savedLinkIndex = null;
                    _linkIndexRoot = null;
                    _backlinkRefreshTimer.Stop();
                    _workspaceChanges.Watch(null);
                    _workspaceTree.Clear();
                    _workspacePins.Clear();
                    ResetTabs("Workspace forgotten; its files remain on disk.");
                });
            }
            else
            {
                await ManagementState.ForgetWorkspaceAsync(workspace.WorkspaceId);
                UpdateChrome("Workspace forgotten; its files remain on disk.");
            }
        }
        catch (Exception exception) { ReportError("workspace.catalog.failed", exception, null); }
    }

    internal async Task<bool> SwitchWorkspaceAsync(string root)
    {
        if (_folder is not null && WorkspaceService.NormalizeRootPath(root).Equals(_folder, StringComparison.OrdinalIgnoreCase))
        {
            _workspaceLoadCancellation?.Cancel();
            ++_workspaceLoadGeneration;
            WorkspaceLoadingPanel.IsVisible = false;
            return true;
        }
        if (_sessionTransitionRunning || _navigationRunning || _metadataApplying) return false;
        var snapshot = await PrepareWorkspaceAsync(root);
        if (snapshot is null) return false;
        return await RunSessionTransitionAsync(async () =>
        {
            await AdoptWorkspaceAsync(snapshot);
            ResetTabs("Workspace opened; choose a Markdown document.");
            await RestoreCleanSessionAsync();
        });
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
        if (_sessionTransitionRunning || _navigationRunning) return;
        _sessionTransitionRunning = true;
        SetSessionCommandsEnabled(false);
        try
        {
            var destinationRelative = (string?)null;
            if (kind == DocumentMutationKind.Move)
            {
                var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title                  = "Move document — choose a folder inside this workspace"
                  , AllowMultiple          = false
                  , SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(
                      Path.GetDirectoryName(Path.Combine(workspace.RootPath, sourceRelative!))!)
                });
                if (folders.FirstOrDefault()?.TryGetLocalPath() is not { } folder) return;
                destinationRelative = WorkspaceMutationPolicy.MoveDestination(workspace.RootPath, sourceRelative!, folder);
            }
            else if (kind != DocumentMutationKind.Recycle)
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
            if (kind == DocumentMutationKind.Recycle) message += "\nAxiom captures the document and its original path before recycling. If capture fails, deletion is prevented. Use File → Recover Deleted Documents to restore; Windows Recycle Bin is also separate. Pins are removed until restore; this is not editor Undo.";
            if (await ChoiceDialog.ShowAsync(this, "Confirm " + kind, message, "Continue", "Cancel") != "Continue") return;
            var affectedTab = sourceRelative is null ? null : FindTab(Path.Combine(workspace.RootPath, sourceRelative));
            if (affectedTab is not null) await SelectTabAsync(affectedTab);
            var affectsActive = sourceRelative is not null && _session.Path is not null && string.Equals(Path.Combine(workspace.RootPath, sourceRelative), _session.Path, StringComparison.OrdinalIgnoreCase);
            if (affectsActive && !await ConfirmSafeToReplaceSessionAsync()) return;
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
                if (affectedTab is not null && kind is DocumentMutationKind.Rename or DocumentMutationKind.Move && path is not null) affectedTab.Session.UpdatePath(path);
                if (affectedTab is not null && kind == DocumentMutationKind.Recycle) await CloseTabAsync(affectedTab);
                await LoadFolderAsync(workspace.RootPath);
                if (kind == DocumentMutationKind.Create && path is not null) await OpenDocumentAsync(path);
                else if (kind == DocumentMutationKind.Duplicate && path is not null &&
                         await ChoiceDialog.ShowAsync(this, "Document duplicated", $"Created {path}. Open the copy?", "Open", "Keep current") == "Open")
                    await OpenDocumentAsync(path);
                UpdateChrome($"{kind} completed: {destinationRelative ?? sourceRelative}");
            }
            finally { Editor.IsEnabled = true; RefreshDocumentMonitors(); }
        }
        catch (Exception exception)
        {
            _mutationBlocked = (await ManagementState.GetPendingMutationsAsync()).Count > 0;
            ReportError("document.management.failed", exception, sourceRelative);
            await ChoiceDialog.ShowAsync(this, "Operation not completed", exception.Message, "OK");
        }
        finally
        {
            _sessionTransitionRunning = false;
            SetSessionCommandsEnabled(true);
            RefreshDocumentMonitors();
        }
    }

    private async void BulkEnroll_OnClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (_activeWorkspace is null || ManagementState is null || _mutationBlocked) { UpdateChrome("Open an available workspace and resolve pending operations first."); return; }
        try
        {
            _monitor.Watch(null);
            var window = new BulkEnrollmentWindow(_activeWorkspace, new BulkEnrollmentService(new WorkspaceFileOperations(ManagementState, log: _log)), ConfirmEnrollmentTabsAsync);
            await window.ShowDialog<bool>(this);
            await LoadFolderAsync(_activeWorkspace.RootPath);
            foreach (var tab in _tabs.ToArray())
                if (tab.Session.Path is { } path) await HandleExternalChangeAsync(path);
            _mutationBlocked = (await ManagementState.GetPendingMutationsAsync()).Count > 0;
        }
        catch (Exception exception) { ReportError("enrollment.batch.failed", exception, _folder); }
        finally { RefreshDocumentMonitors(); }
    }
}
