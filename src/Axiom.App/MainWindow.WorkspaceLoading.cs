using Avalonia.Interactivity;
using Avalonia.Threading;
using Axiom.Core;
using Axiom.Infrastructure;

namespace Axiom.App;

public partial class MainWindow
{
    private CancellationTokenSource? _workspaceLoadCancellation;
    private int _workspaceLoadGeneration;

    private async Task<WorkspaceLoadSnapshot?> PrepareWorkspaceAsync(string folder)
    {
        _workspaceLoadCancellation?.Cancel();
        _workspaceLoadCancellation?.Dispose();
        _workspaceLoadCancellation = new();
        var token = _workspaceLoadCancellation.Token;
        var generation = ++_workspaceLoadGeneration;
        WorkspaceLoadingPanel.IsVisible = true;
        WorkspaceLoadingCancel.IsEnabled = true;
        WorkspaceLoadingStatus.Text = "Loading workspace… Your current documents remain open.";
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            var snapshot = await Task.Run(async () =>
            {
                if (BeforeWorkspaceDiscoveryForTesting is { } wait) await wait(folder, token);
                var root = WorkspaceService.NormalizeRootPath(folder);
                var discovery = await _files.ListMarkdownFilesAsync(root, token);
                var duplicates = await _documents.FindDuplicateIdentitiesAsync(root, token);
                var folders = WorkspaceFolderDiscovery.List(root, token);
                var tree = WorkspaceTreeBuilder.Build(root, discovery.Files, folders);
                var classifications = _classificationService is null ? null : await _classificationService.DiscoverAsync(root, token);
                var relationships = _recovery is IConnectedMetadataStore connected ? await connected.GetRelationshipsAsync(token) : [];
                token.ThrowIfCancellationRequested();
                return new WorkspaceLoadSnapshot(root, discovery, duplicates, tree, classifications, relationships);
            }, token);
            if (generation != _workspaceLoadGeneration || token.IsCancellationRequested) return null;
            return snapshot;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            if (generation == _workspaceLoadGeneration) StatusText.Text = "Workspace loading cancelled; current documents retained.";
            return null;
        }
        catch (Exception exception)
        {
            if (generation == _workspaceLoadGeneration) ReportError("workspace.load.failed", exception, folder);
            throw;
        }
        finally
        {
            if (generation == _workspaceLoadGeneration) WorkspaceLoadingPanel.IsVisible = false;
        }
    }

    private async Task AdoptWorkspaceAsync(WorkspaceLoadSnapshot snapshot)
    {
        WorkspaceLoadingPanel.IsVisible = true;
        WorkspaceLoadingStatus.Text = "Opening discovered workspace…";
        WorkspaceLoadingCancel.IsEnabled = false;
        try
        {
            ++_workspaceRefreshGeneration;
            var priorRoot = _folder;
            var opened = await Task.Run(async () =>
            {
                try
                {
                    var record = await _workspaces.OpenAsync(snapshot.Root);
                    return (Workspace: record, Pins: await _workspaces.GetPinsAsync(record));
                }
                catch
                {
                    if (priorRoot is not null) await _workspaces.OpenAsync(priorRoot);
                    throw;
                }
            });
            var workspace = opened.Workspace;
            var expanded = Traverse(_workspaceTree).Where(node => node.IsExpanded).Select(node => node.RelativePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            _workspaceSearchCancellation?.Cancel();
            ++_workspaceSearchGeneration;
            _workspaceSearchResults.Clear();
            WorkspaceSearchStatus.Text = "";
            _folder = workspace.RootPath;
            _activeWorkspace = workspace;
            BeginClassificationWorkspace();
            _classificationCatalog = snapshot.Classifications;
            _relationships = snapshot.Relationships;
            ApplyClassificationFilter();
            UpdateMetadataPanel();
            BeginBacklinkWorkspace(_folder);
            _workspaceChanges.Watch(_folder);
            _workspaceTree.Clear();
            foreach (var node in snapshot.Tree) { ApplyExpansion(node, expanded); _workspaceTree.Add(node); }
            _duplicates = snapshot.Duplicates;
            _workspacePins.Clear();
            foreach (var pin in opened.Pins) _workspacePins.Add(pin);
            UpdatePinButton();
            DuplicateWarning.Text = snapshot.Duplicates.Count == 0 && snapshot.Discovery.SkippedDirectories.Count == 0 ? "" :
                $"{snapshot.Duplicates.Count} duplicate identity groups; {snapshot.Discovery.SkippedDirectories.Count} skipped directories.";
            StatusText.Text = $"Opened workspace {workspace.DisplayName} with {snapshot.Discovery.Files.Count} Markdown documents.";
            UpdateEmptyStates();
            UpdateDurableStatus();
            ScheduleBacklinkRebuild();
        }
        finally { WorkspaceLoadingPanel.IsVisible = false; }
    }

    private void WorkspaceLoadingCancel_OnClick(object? sender, RoutedEventArgs args) => _workspaceLoadCancellation?.Cancel();
    internal void CancelWorkspaceLoadingForTesting() => _workspaceLoadCancellation?.Cancel();
    internal Func<string, CancellationToken, Task>? BeforeWorkspaceDiscoveryForTesting { get; set; }
}
