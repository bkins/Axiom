using Avalonia.Interactivity;
using Avalonia.Threading;
using Axiom.Core;
using Axiom.Infrastructure;

namespace Axiom.App;

public partial class MainWindow
{
    private readonly WorkspaceChangeMonitor _workspaceChanges = new();
    private readonly DispatcherTimer _workspaceRefreshTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private int _workspaceRefreshGeneration;
    private bool _workspaceRefreshing;

    private void InitializeWorkspaceRefresh()
    {
        _workspaceChanges.Changed += (_, root) => Dispatcher.UIThread.Post(() =>
        {
            if (_closingApproved || !string.Equals(_folder, root, StringComparison.OrdinalIgnoreCase)) return;
            ScheduleBacklinkRebuild();
            _workspaceRefreshTimer.Stop();
            _workspaceRefreshTimer.Start();
        });
        _workspaceRefreshTimer.Tick += async (_, _) =>
        {
            _workspaceRefreshTimer.Stop();
            if (!Editor.IsEnabled || _workspaceRefreshing)
            {
                _workspaceRefreshTimer.Start();
                return;
            }
            await RefreshWorkspaceSafelyAsync();
        };
        Closed += (_, _) =>
        {
            ++_workspaceRefreshGeneration;
            _workspaceRefreshTimer.Stop();
            _workspaceChanges.Dispose();
        };
    }

    private async void RefreshWorkspace_OnClick(object? sender, RoutedEventArgs eventArgs) =>
        await ExecuteCommandAsync("workspace.refresh");

    private async Task RefreshWorkspaceSafelyAsync()
    {
        try { await RefreshWorkspaceTreeAsync(); }
        catch (Exception exception) { ReportError("workspace.refresh.failed", exception, _folder); }
    }

    internal async Task RefreshWorkspaceTreeAsync()
    {
        if (_folder is not { } root || _closingApproved) return;
        if (_workspaceRefreshing || !Editor.IsEnabled)
        {
            _workspaceRefreshTimer.Start();
            return;
        }
        _workspaceRefreshing = true;
        var generation = ++_workspaceRefreshGeneration;
        try
        {
            var discovery = await Task.Run(() => _files.ListMarkdownFilesAsync(root));
            var duplicates = await Task.Run(() => _documents.FindDuplicateIdentitiesAsync(root));
            var folders = await Task.Run(() => WorkspaceFolderDiscovery.List(root));
            if (_closingApproved || generation != _workspaceRefreshGeneration || root != _folder) return;
            var expanded = Traverse(_workspaceTree).Where(node => node.IsExpanded)
                                                    .Select(node => node.RelativePath)
                                                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var selected = (FileTree.SelectedItem as WorkspaceTreeNode)?.RelativePath;
            _workspaceTree.Clear();
            foreach (var node in WorkspaceTreeBuilder.Build(root, discovery.Files, folders))
            {
                ApplyExpansion(node, expanded);
                _workspaceTree.Add(node);
            }
            FileTree.SelectedItem = Traverse(_workspaceTree).FirstOrDefault(node => node.RelativePath == selected);
            _duplicates = duplicates;
            _workspaceSearchCancellation?.Cancel();
            ++_workspaceSearchGeneration;
            _workspaceSearchResults.Clear();
            WorkspaceSearchStatus.Text = "Workspace changed; run search again for current results.";
            var warnings = new List<string>();
            if (duplicates.Count > 0) warnings.Add($"Identity warning: {duplicates.Count} copied document IDs must be made distinct when opened.");
            if (discovery.SkippedDirectories.Count > 0) warnings.Add($"Skipped {discovery.SkippedDirectories.Count} inaccessible or linked directories.");
            DuplicateWarning.Text = string.Join(Environment.NewLine, warnings);
            UpdateEmptyStates();
            UpdateDurableStatus();
        }
        finally { _workspaceRefreshing = false; }
    }
}
