using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Axiom.Core;

namespace Axiom.App;

public partial class MainWindow
{
    private WorkbenchViewMode _viewMode;
    private WorkbenchViewSnapshot? _viewSnapshot;

    private void InitializeViewModes()
    {
        foreach (var mode in Enum.GetValues<WorkbenchViewMode>())
            RegisterCommand("view.mode." + mode.ToString().ToLowerInvariant(), mode == WorkbenchViewMode.Standard ? "Exit focused/full-screen view" : "Enter " + (mode == WorkbenchViewMode.Zen ? "Zen / distraction-free" : mode.ToString()) + " view", mode == WorkbenchViewMode.FullScreen ? "F11" : null, false, (_, _) =>
            {
                if (mode == WorkbenchViewMode.FullScreen && _viewMode == mode) ExitViewMode(); else ApplyViewMode(mode);
                return Task.FromResult(new CommandResult(CommandOutcome.Completed, "View: " + _viewMode + ". Source and recovery retained."));
            });
        RegisterCommand("view.mode.compact", "Toggle compact workbench density", null, false, async (_, _) =>
        {
            if (ManagementState is null) return new(CommandOutcome.Unavailable, "Settings storage is unavailable.");
            ExitViewMode();
            var value = _editorPreferences with { CompactWorkbench = !_editorPreferences.CompactWorkbench };
            await ManagementState.SetEditorPreferencesAsync(value, _appearance); ApplyEditorPreferences(value);
            return new(CommandOutcome.Completed, "Compact density " + (value.CompactWorkbench ? "enabled" : "disabled") + ".");
        });
        var tools = ((MenuFlyout)FileCommandsButton.Flyout!).Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Tools"));
        var views = new MenuItem { Header = "Viewing modes" };
        foreach (var id in new[] { "view.mode.fullscreen", "view.mode.zen", "view.mode.presentation", "view.mode.compact", "view.mode.standard" })
        {
            var command = _commands.Commands.Single(command => command.Id == id);
            var item = new MenuItem { Header = command.Title }; item.Click += async (_, _) => await ExecuteCommandAsync(id); views.Items.Add(item);
        }
        tools.Items.Add(views);
    }

    private void ApplyViewMode(WorkbenchViewMode mode)
    {
        ExitViewMode();
        if (mode == WorkbenchViewMode.Standard) return;
        var columns = new ColumnDefinitions();
        foreach (var column in EditorPreviewLayout.ColumnDefinitions) columns.Add(new ColumnDefinition { Width = column.Width, MinWidth = column.MinWidth, MaxWidth = column.MaxWidth });
        _viewSnapshot = new(WindowState, WorkbenchLayout.ColumnDefinitions[0].Width.Value, WorkspaceSidebar.IsVisible, ToolWindowHost.IsVisible,
            ToolWindowRail.IsVisible, HelpPanelHost.IsVisible, DocumentTabs.IsVisible, TopCommandBar.IsVisible, _readingMode, columns, _editingColumns,
            Editor.TextArea.TextView.HorizontalOffset, Editor.TextArea.TextView.VerticalOffset, _activeTab);
        _viewMode = mode;
        ViewModeExitBar.IsVisible = true; ViewModeText.Text = mode == WorkbenchViewMode.Zen ? "Zen / distraction-free — active document" : mode + " view";
        if (mode == WorkbenchViewMode.FullScreen) WindowState = WindowState.FullScreen;
        else
        {
            WorkspaceSidebar.IsVisible = WorkspaceSplitter.IsVisible = false;
            WorkbenchLayout.ColumnDefinitions[0].Width = WorkbenchLayout.ColumnDefinitions[1].Width = new GridLength(0);
            SetToolWindowVisible(false); ToolWindowRail.IsVisible = false; WorkbenchLayout.ColumnDefinitions[5].Width = new GridLength(0);
            HelpPanelHost.IsVisible = false; TopCommandBar.IsVisible = false; DocumentTabs.IsVisible = false;
            if (mode == WorkbenchViewMode.Presentation) { ApplyReadingMode(true); Preview.IsVisible = true; }
            else
            {
                ApplyReadingMode(false); EditorPane.IsVisible = true; Preview.IsVisible = EditorPreviewSplitter.IsVisible = false;
                EditorPreviewLayout.ColumnDefinitions = new ColumnDefinitions("*,0,0"); Editor.TextArea.Focus();
            }
        }
        PresentationPreviousButton.IsVisible = PresentationNextButton.IsVisible = mode == WorkbenchViewMode.Presentation;
        UpdateChrome(mode + " view — Escape or Exit view restores the previous layout. Unsaved work remains protected.");
    }

    private void ExitViewMode()
    {
        if (_viewSnapshot is not { } snapshot) return;
        var targetTab = _activeTab;
        var horizontal = _viewMode == WorkbenchViewMode.Presentation ? (targetTab == snapshot.Tab ? snapshot.HorizontalOffset : targetTab.HorizontalOffset) : Editor.TextArea.TextView.HorizontalOffset;
        var vertical = _viewMode == WorkbenchViewMode.Presentation ? (targetTab == snapshot.Tab ? snapshot.VerticalOffset : targetTab.VerticalOffset) : Editor.TextArea.TextView.VerticalOffset;
        _viewSnapshot = null; _viewMode = WorkbenchViewMode.Standard;
        WindowState = snapshot.WindowState;
        WorkspaceSidebar.IsVisible = WorkspaceSplitter.IsVisible = snapshot.WorkspaceVisible;
        WorkbenchLayout.ColumnDefinitions[0].Width = new GridLength(snapshot.WorkspaceWidth);
        WorkbenchLayout.ColumnDefinitions[1].Width = new GridLength(snapshot.WorkspaceVisible ? 5 : 0);
        ToolWindowRail.IsVisible = snapshot.RailVisible; WorkbenchLayout.ColumnDefinitions[5].Width = new GridLength(snapshot.RailVisible ? 44 : 0);
        SetToolWindowVisible(snapshot.ToolVisible); HelpPanelHost.IsVisible = snapshot.HelpVisible;
        TopCommandBar.IsVisible = snapshot.TopBarVisible; DocumentTabs.IsVisible = snapshot.TabsVisible;
        ApplyReadingMode(snapshot.ReadingMode); Preview.IsVisible = true;
        _editingColumns = snapshot.PreviousEditingColumns;
        EditorPreviewLayout.ColumnDefinitions = snapshot.EditorColumns;
        EditorPane.IsVisible = EditorPreviewSplitter.IsVisible = !snapshot.ReadingMode;
        ViewModeExitBar.IsVisible = false;
        Dispatcher.UIThread.Post(() =>
        {
            if (_viewMode != WorkbenchViewMode.Standard || targetTab != _activeTab) return;
            Editor.UpdateLayout();
            ((Avalonia.Controls.Primitives.ILogicalScrollable)Editor.TextArea.TextView).Offset = new Avalonia.Vector(horizontal, vertical);
            if (!snapshot.ReadingMode) Editor.TextArea.Focus();
        }, DispatcherPriority.Loaded);
        UpdateChrome("Previous layout restored; document buffers and Undo retained.");
    }

    private void ExitViewMode_OnClick(object? sender, RoutedEventArgs args) => ExitViewMode();
    private async Task InstallPreviewViewKeysAsync()
    {
        try
        {
        await Preview.InvokeScript("""
            (() => { if (window.__axiomViewKeys) return; window.__axiomViewKeys = true;
                window.addEventListener('keydown', event => {
                    if (event.ctrlKey || event.altKey || event.metaKey || !['Escape','F11'].includes(event.key)) return;
                    const token = document.documentElement.dataset.linkToken;
                    if (!token) return;
                    event.preventDefault(); location.href = 'axiom-view://' + (event.key === 'Escape' ? 'exit' : 'fullscreen') + '/' + token;
                }); })();
            """);
        }
        catch (Exception exception) { _log.Error("preview.view.keys.failed", exception, _session.Path); }
    }
    private async Task MovePresentationAsync(double direction)
    {
        try { await Preview.InvokeScript("window.scrollBy({top:window.innerHeight * " + direction.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",behavior:'smooth'})"); }
        catch (Exception exception) { _log.Error("presentation.navigation.failed", exception, _session.Path); UpdateChrome("Preview navigation is unavailable; Exit view remains available."); }
    }
    private async void PresentationPrevious_OnClick(object? sender, RoutedEventArgs args) => await MovePresentationAsync(-.85);
    private async void PresentationNext_OnClick(object? sender, RoutedEventArgs args) => await MovePresentationAsync(.85);
    internal WorkbenchViewMode ViewModeForTesting => _viewMode;
}
