using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Axiom.Core;

namespace Axiom.App;

public partial class MainWindow
{
    private static readonly DataFormat<string> TabMoveFormat = DataFormat.CreateStringApplicationFormat("Axiom.TabOrder");
    private PointerPressedEventArgs? _tabDragPress;
    private DocumentTab? _tabDragSource;
    private Point _tabDragOrigin;
    private string? _tabDragToken;
    private ListBoxItem? _tabDropTarget;

    private static ListBoxItem? TabContainer(object? source) => source is Control control
        ? control as ListBoxItem ?? control.GetVisualAncestors().OfType<ListBoxItem>().FirstOrDefault() : null;

    private void InitializeTabDrag()
    {
        DocumentTabs.AddHandler(PointerPressedEvent, (_, args) =>
        {
            _tabDragPress = null;
            if (!args.GetCurrentPoint(DocumentTabs).Properties.IsLeftButtonPressed || _sessionTransitionRunning || _navigationRunning || _metadataApplying) return;
            if (args.Source is Control control && (control is Button || control.GetVisualAncestors().OfType<Button>().Any())) return;
            if (TabContainer(args.Source)?.DataContext is not DocumentTab tab) return;
            _tabDragPress = args;
            _tabDragSource = tab;
            _tabDragOrigin = args.GetPosition(DocumentTabs);
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        DocumentTabs.AddHandler(PointerMovedEvent, async (_, args) =>
        {
            if (_tabDragToken is not null || _tabDragPress is not { } press || _tabDragSource is null || !args.GetCurrentPoint(DocumentTabs).Properties.IsLeftButtonPressed) return;
            var distance = args.GetPosition(DocumentTabs) - _tabDragOrigin;
            if (Math.Abs(distance.X) < 6 && Math.Abs(distance.Y) < 6) return;
            _tabDragToken = Guid.NewGuid().ToString("N");
            var transfer = new DataTransfer();
            var item = new DataTransferItem(); item.Set(TabMoveFormat, _tabDragToken); transfer.Add(item);
            try { await DragDrop.DoDragDropAsync(press, transfer, DragDropEffects.Move); }
            catch (Exception exception) { ReportError("tabs.drag.failed", exception, null); }
            finally { _tabDragToken = null; _tabDragPress = null; _tabDragSource = null; ClearTabDropTarget(); }
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        DragDrop.SetAllowDrop(DocumentTabs, true);
        DocumentTabs.AddHandler(DragDrop.DragOverEvent, (_, args) =>
        {
            if (!args.DataTransfer.Contains(TabMoveFormat)) return;
            args.Handled = true;
            ClearTabDropTarget();
            var target = TabContainer(args.Source);
            var valid = args.DataTransfer.TryGetValue(TabMoveFormat) == _tabDragToken && target?.DataContext is DocumentTab tab && _tabs.Contains(tab) && !_sessionTransitionRunning && !_navigationRunning && !_metadataApplying;
            args.DragEffects = valid ? DragDropEffects.Move : DragDropEffects.None;
            if (valid) { _tabDropTarget = target; target!.Classes.Add("tab-drop"); }
        });
        DocumentTabs.AddHandler(DragDrop.DragLeaveEvent, (_, _) => ClearTabDropTarget());
        DocumentTabs.AddHandler(DragDrop.DropEvent, async (_, args) =>
        {
            if (!args.DataTransfer.Contains(TabMoveFormat)) return;
            args.Handled = true;
            var target = TabContainer(args.Source)?.DataContext as DocumentTab;
            ClearTabDropTarget();
            if (args.DataTransfer.TryGetValue(TabMoveFormat) != _tabDragToken || _tabDragSource is not { } source || target is null) return;
            await MoveTabAsync(source, _tabs.IndexOf(target));
        });
        foreach (var (id, title, direction) in new[] { ("document.tabLeft", "Move tab left", -1), ("document.tabRight", "Move tab right", 1) })
            RegisterCommand(id, title, null, false, async (context, _) =>
            {
                var tab = _tabs.SingleOrDefault(candidate => candidate.Session.DraftKey == context.DraftKey);
                if (tab is null) return new(CommandOutcome.Unavailable, "This tab is no longer open.");
                await MoveTabAsync(tab, _tabs.IndexOf(tab) + direction);
                return new(CommandOutcome.Completed, "Tab order remembered; documents and drafts retained.");
            });
    }

    private async Task MoveTabAsync(DocumentTab tab, int targetIndex)
    {
        if (_sessionTransitionRunning || _navigationRunning || _metadataApplying || !_tabs.Contains(tab)) return;
        await _tabSelectionGate.WaitAsync();
        try
        {
            var sourceIndex = _tabs.IndexOf(tab);
            if (sourceIndex < 0) return;
            var destination = Math.Clamp(targetIndex, 0, _tabs.Count - 1);
            if (destination == sourceIndex) return;
            CaptureEditorState(_activeTab);
            _selectingTab = true;
            try { _tabs.Move(sourceIndex, destination); DocumentTabs.SelectedItem = _activeTab; }
            finally { _selectingTab = false; }
            await PersistSessionLocationsAsync(requireClean: false);
        }
        finally { _tabSelectionGate.Release(); }
    }

    private void ClearTabDropTarget()
    {
        _tabDropTarget?.Classes.Remove("tab-drop");
        _tabDropTarget = null;
    }

    internal Task ReorderTabForTestingAsync(DocumentTab tab, int index) => MoveTabAsync(tab, index);
}
