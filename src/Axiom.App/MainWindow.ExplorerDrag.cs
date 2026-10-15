using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Avalonia.Threading;
using Axiom.Core;

namespace Axiom.App;

public partial class MainWindow
{
    private static readonly DataFormat<string> ExplorerMoveFormat = DataFormat.CreateStringApplicationFormat("Axiom.WorkspaceMove");
    private PointerPressedEventArgs? _explorerDragPress;
    private WorkspaceExplorerNode? _explorerDragNode;
    private Point _explorerDragOrigin;
    private string? _explorerDragToken;
    private string? _explorerDragRoot;
    private TreeViewItem? _explorerDropTarget;
    private bool _explorerDragRunning;
    private bool _suppressExplorerOpen;
    private readonly DispatcherTimer _explorerScrollTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private ScrollViewer? _explorerScrollViewer;
    private int _explorerScrollDirection;

    private static TreeViewItem? ExplorerItem(object? source) => source is Control control
        ? control as TreeViewItem ?? control.GetVisualAncestors().OfType<TreeViewItem>().FirstOrDefault() : null;

    private void InitializeExplorerDrag()
    {
        FileTree.AddHandler(PointerPressedEvent, Explorer_OnPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        FileTree.AddHandler(PointerMovedEvent, Explorer_OnPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        DragDrop.SetAllowDrop(FileTree, true);
        FileTree.AddHandler(DragDrop.DragOverEvent, Explorer_DragOver, RoutingStrategies.Bubble);
        FileTree.AddHandler(DragDrop.DropEvent, Explorer_Drop, RoutingStrategies.Bubble);
        FileTree.AddHandler(DragDrop.DragLeaveEvent, (_, _) => { StopExplorerDragScroll(); ClearExplorerDropTarget(); });
        _explorerScrollTimer.Tick += (_, _) =>
        {
            if ((_explorerDragToken is null || _folder != _explorerDragRoot) && (_tabDragToken is null || _folder != _tabDragRoot) || _mutationBlocked || _sessionTransitionRunning || _navigationRunning || _explorerScrollViewer is null)
            { StopExplorerDragScroll(); return; }
            var maximum = Math.Max(0, _explorerScrollViewer.Extent.Height - _explorerScrollViewer.Viewport.Height);
            _explorerScrollViewer.Offset = new Vector(_explorerScrollViewer.Offset.X, Math.Clamp(_explorerScrollViewer.Offset.Y + _explorerScrollDirection * 16, 0, maximum));
        };
        Closed += (_, _) => StopExplorerDragScroll();
    }

    private void Explorer_OnPointerPressed(object? sender, PointerPressedEventArgs args)
    {
        _explorerDragPress = null;
        if (_workspaceBatchRunning || args.KeyModifiers != KeyModifiers.None || SelectedWorkspaceItems().Length > 1) return;
        _suppressExplorerOpen = false;
        if (!args.GetCurrentPoint(FileTree).Properties.IsLeftButtonPressed || _folder is null || _mutationBlocked || _sessionTransitionRunning || _navigationRunning) return;
        var item = ExplorerItem(args.Source);
        if (args.Source is Control control && (control is Avalonia.Controls.Primitives.ToggleButton || control.GetVisualAncestors().OfType<Avalonia.Controls.Primitives.ToggleButton>().Any())) return;
        if (item?.DataContext is not WorkspaceExplorerNode { Heading: null, IsPlaceholder: false, IsGroup: false } node) return;
        _explorerDragPress = args;
        _explorerDragNode = node;
        _explorerDragRoot = _folder;
        _explorerDragOrigin = args.GetPosition(FileTree);
    }

    private async void Explorer_OnPointerMoved(object? sender, PointerEventArgs args)
    {
        if (_explorerDragRunning || _explorerDragPress is not { } press || _explorerDragNode is null || !args.GetCurrentPoint(FileTree).Properties.IsLeftButtonPressed) return;
        var distance = args.GetPosition(FileTree) - _explorerDragOrigin;
        if (Math.Abs(distance.X) < 6 && Math.Abs(distance.Y) < 6) return;
        _explorerDragRunning = true;
        _suppressExplorerOpen = true;
        _explorerDragToken = Guid.NewGuid().ToString("N");
        var transfer = new DataTransfer();
        var item = new DataTransferItem();
        item.Set(ExplorerMoveFormat, _explorerDragToken);
        transfer.Add(item);
        try { await DragDrop.DoDragDropAsync(press, transfer, DragDropEffects.Move); }
        catch (Exception exception) { ReportError("workspace.drag.failed", exception, _folder); }
        finally { StopExplorerDragScroll(); _explorerDragRunning = false; _explorerDragToken = null; _explorerDragPress = null; _explorerDragNode = null; ClearExplorerDropTarget(); }
    }

    private WorkspaceExplorerNode? ExplorerDropFolder(DragEventArgs args)
    {
        var tabMove = args.DataTransfer.Contains(TabMoveFormat) && _tabDragToken is not null && args.DataTransfer.TryGetValue(TabMoveFormat) == _tabDragToken && _tabDragSource?.Session.Path is not null && _folder == _tabDragRoot;
        if (!tabMove && (_explorerDragToken is null || args.DataTransfer.TryGetValue(ExplorerMoveFormat) != _explorerDragToken || _folder != _explorerDragRoot)) return null;
        var item = ExplorerItem(args.Source);
        if (item is null) return new WorkspaceExplorerNode("Workspace root", "");
        return item.DataContext as WorkspaceExplorerNode is { IsDocument: false, Heading: null, IsGroup: false, IsPlaceholder: false } folder ? folder : null;
    }

    private void Explorer_DragOver(object? sender, DragEventArgs args)
    {
        if (!args.DataTransfer.Contains(ExplorerMoveFormat) && !args.DataTransfer.Contains(TabMoveFormat)) return;
        args.Handled = true;
        ClearExplorerDropTarget();
        _explorerScrollViewer = FileTree.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (_explorerScrollViewer is { } scroll && (_explorerDragToken is not null && _folder == _explorerDragRoot || _tabDragToken is not null && _folder == _tabDragRoot))
        {
            var position = args.GetPosition(scroll);
            _explorerScrollDirection = position.X < 0 || position.X > scroll.Bounds.Width ? 0 : position.Y < 32 ? -1 : position.Y > scroll.Bounds.Height - 32 ? 1 : 0;
            if (_explorerScrollDirection == 0) StopExplorerDragScroll();
            else _explorerScrollTimer.Start();
        }
        var folder = ExplorerDropFolder(args);
        var source = args.DataTransfer.Contains(TabMoveFormat) ? _tabDragSource?.Session.Path : _explorerDragNode is { } node ? Path.Combine(_folder!, node.RelativePath) : null;
        var valid = folder is not null && source is not null && !_mutationBlocked && !_sessionTransitionRunning && !_navigationRunning &&
            !WorkspacePathBoundary.Contains(source, Path.Combine(_folder!, folder.RelativePath), includeRoot: true);
        args.DragEffects = valid ? DragDropEffects.Move : DragDropEffects.None;
        if (!valid) return;
        _explorerDropTarget = ExplorerItem(args.Source);
        _explorerDropTarget?.Classes.Add("move-target");
        UpdateChrome("Move into " + folder!.RelativePath + " — confirmation follows on drop.");
    }

    private void Explorer_Drop(object? sender, DragEventArgs args)
    {
        if (!args.DataTransfer.Contains(ExplorerMoveFormat) && !args.DataTransfer.Contains(TabMoveFormat)) return;
        args.Handled = true;
        var folder = ExplorerDropFolder(args);
        var node = _explorerDragNode;
        StopExplorerDragScroll();
        ClearExplorerDropTarget();
        if (args.DataTransfer.Contains(TabMoveFormat))
        {
            if (folder is not null && _folder is not null && _tabDragSource?.Session.Path is { } source && !_mutationBlocked && !_sessionTransitionRunning && !_navigationRunning)
                LastExplorerMove = ReviewMoveAsync(source, Path.Combine(_folder, folder.RelativePath, Path.GetFileName(source)), false, CancellationToken.None);
            return;
        }
        if (folder is null || node is null || _folder is null || _mutationBlocked || _sessionTransitionRunning || _navigationRunning) return;
        LastExplorerMove = PerformExplorerDropAsync(folder, node);
    }

    private async Task PerformExplorerDropAsync(WorkspaceExplorerNode folder, WorkspaceExplorerNode node)
    {
        var destination = Path.Combine(folder.RelativePath, Path.GetFileName(node.RelativePath));
        // Use the dispatcher so drag gestures obey the same mutation block and
        // execution gate as context menus and keyboard commands.
        var context = CaptureCommandContext() with { ExplorerRelativePath = node.RelativePath, ExplorerIsFolder = !node.IsDocument };
        _requestedMoveDestination = destination;
        try { await ExecuteCommandInContextAsync("workspace.manage.move", context); }
        finally { _requestedMoveDestination = null; }
    }

    private string? _requestedMoveDestination;
    internal Task LastExplorerMove { get; private set; } = Task.CompletedTask;
    internal bool ExplorerEdgeScrollRunningForTesting => _explorerScrollTimer.IsEnabled;

    internal async Task<double> RouteExplorerEdgeScrollForTestingAsync()
    {
        _explorerDragToken = Guid.NewGuid().ToString("N");
        _explorerDragRoot = _folder;
        using var transfer = new DataTransfer();
        var item = new DataTransferItem();
        item.Set(ExplorerMoveFormat, _explorerDragToken);
        transfer.Add(item);
        var scroll = FileTree.GetVisualDescendants().OfType<ScrollViewer>().First();
        var point = scroll.TranslatePoint(new Point(scroll.Bounds.Width / 2, scroll.Bounds.Height - 2), FileTree)!.Value;
        FileTree.RaiseEvent(new DragEventArgs(DragDrop.DragOverEvent, transfer, FileTree, point, KeyModifiers.None));
        await Task.Delay(220);
        var offset = scroll.Offset.Y;
        FileTree.RaiseEvent(new DragEventArgs(DragDrop.DragLeaveEvent, transfer, FileTree, point, KeyModifiers.None));
        _explorerDragToken = null;
        return offset;
    }

    internal async Task RouteExplorerDropForTestingAsync(string source, string targetFolder, bool folder)
    {
        _explorerDragToken = Guid.NewGuid().ToString("N");
        _explorerDragRoot = _folder;
        _explorerDragNode = new WorkspaceExplorerNode(Path.GetFileName(source), source, !folder);
        FileTree.UpdateLayout();
        var target = FileTree.GetVisualDescendants().OfType<TreeViewItem>().Single(item => item.DataContext is WorkspaceExplorerNode { IsDocument: false, IsGroup: false } node && node.RelativePath == targetFolder);
        using var transfer = new DataTransfer();
        var item = new DataTransferItem();
        item.Set(ExplorerMoveFormat, _explorerDragToken);
        transfer.Add(item);
        try
        {
            target.RaiseEvent(new DragEventArgs(DragDrop.DropEvent, transfer, target, new Point(1, 1), KeyModifiers.None));
            await LastExplorerMove;
        }
        finally { _explorerDragToken = null; _explorerDragNode = null; }
    }
    private void ClearExplorerDropTarget()
    {
        _explorerDropTarget?.Classes.Remove("move-target");
        _explorerDropTarget = null;
    }

    private void StopExplorerDragScroll()
    {
        _explorerScrollTimer.Stop();
        _explorerScrollDirection = 0;
        _explorerScrollViewer = null;
    }
}
