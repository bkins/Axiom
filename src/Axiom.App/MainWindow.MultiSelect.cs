using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia.Platform.Storage;
using Axiom.Core;
using Axiom.Infrastructure;

namespace Axiom.App;

public partial class MainWindow
{
    private bool _workspaceBatchRunning;
    private WorkspaceExplorerNode? _workspaceRangeAnchor;
    private WorkspaceExplorerNode? _workspaceRangeEnd;

    private void WorkspaceSelectionKey(object? sender, KeyEventArgs args)
    {
        if (args.KeyModifiers == KeyModifiers.Control && args.Key == Key.Space &&
            ExplorerItem(args.Source)?.DataContext is WorkspaceExplorerNode focused)
        {
            args.Handled = true;
            if (FileTree.SelectedItems.Contains(focused)) FileTree.SelectedItems.Remove(focused); else FileTree.SelectedItems.Add(focused);
            return;
        }
        if (args.KeyModifiers != KeyModifiers.Shift || args.Key is not (Key.Up or Key.Down)) { _workspaceRangeAnchor = null; _workspaceRangeEnd = null; return; }
        var visible = FileTree.GetVisualDescendants().OfType<TreeViewItem>()
            .Where(item => item.IsEffectivelyVisible && item.Bounds.Height > 0 && item.DataContext is WorkspaceExplorerNode { Heading: null, IsGroup: false, IsPlaceholder: false })
            .OrderBy(item => item.TranslatePoint(default, FileTree)?.Y ?? 0).ToArray();
        var current = _workspaceRangeEnd ?? ExplorerItem(args.Source)?.DataContext as WorkspaceExplorerNode ?? FileTree.SelectedItem as WorkspaceExplorerNode;
        var index = Array.FindIndex(visible, item => ReferenceEquals(item.DataContext, current));
        if (index < 0) return;
        args.Handled = true;
        _workspaceRangeAnchor ??= current;
        var next = Math.Clamp(index + (args.Key == Key.Down ? 1 : -1), 0, visible.Length - 1);
        _workspaceRangeEnd = (WorkspaceExplorerNode)visible[next].DataContext!;
        var anchor = Array.FindIndex(visible, item => ReferenceEquals(item.DataContext, _workspaceRangeAnchor));
        if (anchor < 0) anchor = index;
        FileTree.SelectedItems.Clear();
        for (var at = Math.Min(anchor, next); at <= Math.Max(anchor, next); at++) FileTree.SelectedItems.Add(visible[at].DataContext!);
        visible[next].Focus(); visible[next].BringIntoView();
    }
    private WorkspaceExplorerNode[] SelectedWorkspaceItems() => FileTree.SelectedItems.OfType<WorkspaceExplorerNode>()
        .Where(node => !node.IsGroup && !node.IsPlaceholder && node.Heading is null)
        .DistinctBy(node => node.RelativePath, WorkspacePathBoundary.Comparer)
        .Where(node => !FileTree.SelectedItems.OfType<WorkspaceExplorerNode>().Any(parent => parent is { IsDocument: false, Heading: null, IsGroup: false, IsPlaceholder: false } && parent != node &&
            WorkspacePathBoundary.Contains(Path.Combine(_folder!, parent.RelativePath), Path.Combine(_folder!, node.RelativePath))))
        .ToArray();

    private void UpdateSelectionCount() => WorkspaceSelectionCount.Text = $"{(_folder is null ? 0 : SelectedWorkspaceItems().Length)} selected — Ctrl/Shift selects multiple";
    private async void WorkspaceBatchOpen_OnClick(object? sender, RoutedEventArgs args) => await RunSelectedItemsAsync("Open");
    private async void WorkspaceBatchMove_OnClick(object? sender, RoutedEventArgs args) => await RunSelectedItemsAsync("Move");
    private async void WorkspaceBatchRecycle_OnClick(object? sender, RoutedEventArgs args) => await RunSelectedItemsAsync("Recycle");

    private async Task<CommandResult> RunSelectedItemsAsync(string action)
    {
        if (_folder is null || _activeWorkspace is not { } workspace || ManagementState is null || _sessionTransitionRunning || _navigationRunning || _mutationBlocked || _workspaceBatchRunning)
            return new(CommandOutcome.Unavailable, "Open a workspace and finish the current operation first.");
        var nodes = SelectedWorkspaceItems();
        if (nodes.Length == 0) return new(CommandOutcome.Unavailable, "Select workspace files or folders first.");
        var sources = nodes.Select(node => Path.Combine(workspace.RootPath, node.RelativePath)).ToArray();
        string? destination = null;
        if (action == "Move")
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Move selected items into folder", AllowMultiple = false,
                SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(workspace.RootPath) });
            destination = folders.FirstOrDefault()?.TryGetLocalPath();
            if (destination is null) return new(CommandOutcome.Cancelled, "Batch move cancelled.");
        }
        var result = await RunWorkspaceBatchAsync(action, nodes, destination);
        UpdateChrome(result.Message);
        return result;
    }

    private async Task<CommandResult> RunWorkspaceBatchAsync(string action, WorkspaceExplorerNode[] nodes, string? destination)
    {
        if (_activeWorkspace is not { } workspace || ManagementState is null || _workspaceBatchRunning) return new(CommandOutcome.Unavailable, "Workspace unavailable or batch already running.");
        _workspaceBatchRunning = true;
        var sources = nodes.Select(node => Path.Combine(workspace.RootPath, node.RelativePath)).ToArray();
        var completed = new List<string>();
        try
        {
            if (action != "Open")
            {
                if (_tabs.Any(tab => tab.Session.Path is { } path && (tab.Session.IsDirty || tab.Session.Conflict is not null) &&
                    (action == "Move" || sources.Any(source => path.Equals(source, WorkspacePathBoundary.Comparison) || WorkspacePathBoundary.Contains(source, path)))))
                    throw new IOException("Save affected documents and resolve conflicts before the batch. Nothing was changed.");
                var targets = new HashSet<string>(WorkspacePathBoundary.Comparer);
                foreach (var source in sources)
                {
                    ReviewedWorkspaceMove.ValidateSource(source);
                    if (action != "Move") continue;
                    var target = Path.Combine(destination!, Path.GetFileName(source));
                    WorkspaceMutationPolicy.Resolve(workspace.RootPath, Path.GetRelativePath(workspace.RootPath, target), false);
                    if (!targets.Add(target) || File.Exists(target) || Directory.Exists(target) || WorkspacePathBoundary.Contains(source, target, true))
                        throw new IOException("Batch contains a collision or unsafe destination. Nothing was changed.");
                }
            }
            if (await ChoiceDialog.ShowAsync(this, action + " selected workspace items", string.Join("\n", nodes.Select(node => node.RelativePath)) +
                (destination is null ? "" : "\nDestination: " + destination) +
                "\n\nFolders include descendants; selected descendants are counted once. Items run in order and stop on cancellation or failure; earlier successful items remain completed. Moves each show their link-repair preview. Recycled files use Axiom recovery; recycled folders retain a verified ZIP under state Backups and can be restored through Windows Recycle Bin. Rename selected, Copy folder and Export selected have separate previews.", "Continue", "Cancel") != "Continue")
                return new(CommandOutcome.Cancelled, "Batch cancelled.");
            var operations = new WorkspaceFileOperations(ManagementState, log: _log);
            foreach (var node in nodes)
            {
                var source = Path.Combine(workspace.RootPath, node.RelativePath);
                if (action == "Open")
                {
                    var paths = node.IsDocument ? new[] { source } : (await _files.ListMarkdownFilesAsync(source)).Files;
                    foreach (var path in paths) await OpenDocumentAsync(path);
                }
                else if (action == "Move")
                {
                    var result = await ReviewMoveAsync(source, Path.Combine(destination!, Path.GetFileName(source)), false, CancellationToken.None);
                    if (result.Outcome != CommandOutcome.Completed) throw new IOException(result.Message);
                }
                else
                {
                    _sessionTransitionRunning = true; SetSessionCommandsEnabled(false);
                    try
                    {
                        if (node.IsDocument)
                            await operations.ExecuteAsync(workspace, DocumentMutationKind.Recycle, node.RelativePath, null, WorkspaceFileOperations.Hash(await File.ReadAllBytesAsync(source)));
                        else
                        {
                            var hash = await operations.ReviewFolderAsync(workspace, node.RelativePath);
                            var archive = await new ProtectedFolderRecycle(ManagementState).ExecuteAsync(workspace, node.RelativePath, hash);
                            completed.Add("Verified folder backup: " + archive);
                        }
                        foreach (var tab in _tabs.Where(tab => tab.Session.Path is { } path && (path.Equals(source, WorkspacePathBoundary.Comparison) || !node.IsDocument && WorkspacePathBoundary.Contains(source, path))).ToArray())
                            await CloseTabAsync(tab);
                    }
                    finally { _sessionTransitionRunning = false; SetSessionCommandsEnabled(true); }
                }
                completed.Add(node.RelativePath);
            }
            await RefreshWorkspaceTreeAsync();
            return new(CommandOutcome.Completed, $"{action}: {nodes.Length} items completed. " + string.Join("; ", completed.Where(item => item.StartsWith("Verified folder backup:", StringComparison.Ordinal))));
        }
        catch (Exception error)
        {
            await RefreshWorkspaceTreeAsync();
            var message = $"{action} stopped. Completed {completed.Count}: {string.Join(", ", completed)}\n{error.Message}\nRemaining items were not attempted.";
            await ChoiceDialog.ShowAsync(this, "Batch result", message, "OK");
            return new(CommandOutcome.Failed, message, error);
        }
        finally { _workspaceBatchRunning = false; _mutationBlocked = new ReviewedWorkspaceMove(ManagementState).Pending || (await ManagementState.GetPendingMutationsAsync()).Count > 0; }
    }

    internal Task<CommandResult> RunWorkspaceBatchForTestingAsync(string action, WorkspaceExplorerNode[] nodes, string? destination = null) => RunWorkspaceBatchAsync(action, nodes, destination);
}
