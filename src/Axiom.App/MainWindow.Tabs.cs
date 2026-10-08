using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Axiom.Core;

namespace Axiom.App;

public partial class MainWindow
{
    private readonly ObservableCollection<DocumentTab> _tabs = [];
    private readonly SemaphoreSlim _recoveryGate = new(1, 1);
    private readonly SemaphoreSlim _tabSelectionGate = new(1, 1);
    private DocumentTab _activeTab = null!;
    private async Task<CommandResult> CycleDocumentTabAsync(int direction)
    {
        if (_tabs.Count < 2) return new(CommandOutcome.Unavailable, "Open another document tab first.");
        var index = (_tabs.IndexOf(_activeTab) + direction + _tabs.Count) % _tabs.Count;
        var target = _tabs[index];
        await SelectTabAsync(target);
        return new(_activeTab == target ? CommandOutcome.Completed : CommandOutcome.Cancelled, "Tab selection reviewed; working buffers retained.");
    }
    private int _untitledNumber;
    private bool _selectingTab;
    private bool _initialPlaceholder = true;

    private DocumentTab? FindTab(string path) => _tabs.FirstOrDefault(tab =>
        tab.Session.Path is { } candidate &&
        Path.GetFullPath(candidate).Equals(Path.GetFullPath(path), WorkspacePathBoundary.Comparison));

    private async Task<CommandResult> SaveAllDocumentsAsync()
    {
        if (!await ResolvePendingMetadataAsync())
            return new(CommandOutcome.Cancelled, "Save All cancelled; working buffers retained.");
        var saved = 0;
        foreach (var tab in _tabs.ToArray())
        {
            if (!tab.Session.IsDirty) continue;
            if (!await SaveDocumentAsync(false, tab))
            {
                var message = $"Save All stopped after saving {saved} documents; remaining unsaved buffers retained. Review {tab.Label}.";
                UpdateChrome(message);
                return new(CommandOutcome.Cancelled, message);
            }
            saved++;
        }
        var completed = saved == 0 ? "All open documents are already saved." : $"Saved all {saved} modified open documents.";
        UpdateChrome(completed);
        return new(CommandOutcome.Completed, completed);
    }

    private async void SaveAll_OnClick(object? sender, RoutedEventArgs eventArgs) =>
        await ExecuteCommandAsync("document.saveAll");

    private DocumentStructure GetTabStructure(DocumentTab tab)
    {
        if (tab == _activeTab) return GetCurrentStructure();
        if (tab.Structure is null || tab.Structure.SourceText != tab.Session.Text)
            tab.Structure = _structureParser.Parse(tab.Session.Text, tab.Structure);
        return tab.Structure;
    }

    private DocumentTab AddTab(DocumentSession session)
    {
        var tab = new DocumentTab(session, ++_untitledNumber);
        var selecting = _selectingTab;
        _selectingTab = true;
        _tabs.Add(tab);
        _selectingTab = selecting;
        RefreshTabLabels();
        return tab;
    }

    private async void DocumentTabs_OnSelectionChanged(object? sender, SelectionChangedEventArgs eventArgs)
    {
        if (!_selectingTab && !_sessionTransitionRunning && !_navigationRunning && DocumentTabs.SelectedItem is DocumentTab tab)
            await SelectTabAsync(tab);
    }

    private async Task SelectTabAsync(DocumentTab tab)
    {
        await _tabSelectionGate.WaitAsync();
        try
        {
            if (!_tabs.Contains(tab)) return;
            if (tab == _activeTab) { RevealActiveExplorerDocument(); return; }
            if (!await ResolvePendingMetadataAsync())
            {
                _selectingTab = true;
                DocumentTabs.SelectedItem = _activeTab;
                _selectingTab = false;
                return;
            }
            var previous = _activeTab;
            CaptureEditorState(previous);
            ++_previewRenderGeneration;
            if (_previewReady)
            {
                try
                {
                    var result = await Preview.InvokeScript("(() => { const root = document.scrollingElement || document.documentElement; const range = Math.max(0, root.scrollHeight - window.innerHeight); return range === 0 ? 0 : root.scrollTop / range; })()");
                    if (TryParsePreviewScrollRatio(result, out var ratio)) previous.PreviewScrollRatio = ratio;
                    await CapturePreviewAnchorAsync();
                }
                catch (Exception exception) { _log.Error("preview.tab.capture.failed", exception, previous.Session.Path); }
            }
            CaptureEditorState(previous);
            ActivateTab(tab, "Selected document; working buffers retained.");
        }
        finally { _tabSelectionGate.Release(); }
    }

    private void CaptureEditorState(DocumentTab tab)
    {
        tab.CaretOffset = Editor.CaretOffset;
        tab.SelectionStart = Editor.SelectionStart;
        tab.SelectionLength = Editor.SelectionLength;
        if (!tab.HasPendingScrollRestore && !_readingMode)
        {
            tab.HorizontalOffset = Editor.TextArea.TextView.HorizontalOffset;
            tab.VerticalOffset = Editor.TextArea.TextView.VerticalOffset;
        }
        tab.Structure = _structure;
    }

    private void ActivateTab(DocumentTab tab, string status)
    {
        _previewLinkToken = null;
        _previewLinkStructure = null;
        _pendingHeadingFragment = null;
        _loading = true;
        _activeTab = tab;
        _session = tab.Session;
        _structure = tab.Structure;
        Editor.Document = tab.Document;
        ScheduleSpelling();
        Editor.Select(Math.Min(tab.SelectionStart, tab.Document.TextLength),
            Math.Min(tab.SelectionLength, Math.Max(0, tab.Document.TextLength - tab.SelectionStart)));
        Editor.CaretOffset = Math.Min(tab.CaretOffset, tab.Document.TextLength);
        Editor.UpdateLayout();
        Editor.ScrollToHorizontalOffset(tab.HorizontalOffset);
        Editor.ScrollToVerticalOffset(tab.VerticalOffset);
        tab.HasPendingScrollRestore = true;
        Dispatcher.UIThread.Post(() =>
        {
            if (_activeTab != tab || _closingApproved || !IsVisible) return;
            Editor.UpdateLayout();
            Editor.TextArea.TextView.EnsureVisualLines();
            ((ILogicalScrollable)Editor.TextArea.TextView).Offset = new Avalonia.Vector(tab.HorizontalOffset, tab.VerticalOffset);
            tab.HasPendingScrollRestore = false;
        }, DispatcherPriority.Loaded);
        _loading = false;
        _selectingTab = true;
        DocumentTabs.SelectedItem = tab;
        _selectingTab = false;
        ConflictBanner.IsVisible = _session.Conflict is not null;
        RecoveryText.Text = tab.RecoveryStatus;
        UpdateEmptyStates();
        RenderOutline();
        RevealActiveExplorerDocument();
        RenderPreview();
        RefreshDocumentMonitors();
        UpdateChrome(status);
        RefreshIncomingReferences();
        UpdateMetadataPanel();
    }

    private async Task LoadSessionAsync(DocumentSession session, string status)
    {
        var tab = AddTab(session);
        await SelectTabAsync(tab);
        UpdateChrome(status);
    }

    private async Task LoadRecoveredSessionAsync(DocumentSession session)
    {
        if (session.Path is { } path && FindTab(path) is { } existing)
        {
            if (existing.Session.IsDirty)
            {
                UpdateChrome("This path already has unsaved edits open. The protected draft remains available for later recovery.");
                return;
            }
            var recovered = new DocumentTab(session, existing.UntitledNumber);
            recovered.CaretOffset = existing.CaretOffset;
            recovered.SelectionStart = existing.SelectionStart;
            recovered.SelectionLength = existing.SelectionLength;
            recovered.HorizontalOffset = existing.HorizontalOffset;
            recovered.VerticalOffset = existing.VerticalOffset;
            recovered.PreviewScrollRatio = existing.PreviewScrollRatio;
            _selectingTab = true;
            _tabs[_tabs.IndexOf(existing)] = recovered;
            _selectingTab = false;
            await SelectTabAsync(recovered);
            UpdateChrome("Recovered protected work in its existing tab. The saved file has not been overwritten.");
            return;
        }
        await LoadSessionAsync(session, "Recovered protected work. The saved file has not been overwritten.");
    }

    private void ResetTabs(string status)
    {
        _backLocations.Clear();
        _forwardLocations.Clear();
        UpdateNavigationCommands();
        _selectingTab = true;
        _tabs.Clear();
        var tab = AddTab(_documents.NewDocument());
        _selectingTab = false;
        _initialPlaceholder = true;
        ActivateTab(tab, status);
    }

    private void RefreshDocumentMonitors() =>
        _monitor.WatchMany(_tabs.Select(tab => tab.Session.Path).OfType<string>());

    private void RefreshTabLabels()
    {
        foreach (var tab in _tabs)
        {
            var path = tab.Session.Path;
            var name = path is null ? $"Untitled {tab.UntitledNumber}" : Path.GetFileName(path);
            if (path is not null && _tabs.Count(other => other.Session.Path is { } otherPath &&
                    Path.GetFileName(otherPath).Equals(name, StringComparison.OrdinalIgnoreCase)) > 1)
                name = _folder is not null && IsWithinFolder(path, _folder) ? Path.GetRelativePath(_folder, path) : path;
            tab.Label = name + (tab.Session.IsDirty ? " *" : "") +
                (tab.Session.Conflict is not null ? " [Conflict]" : tab.IsMissing ? " [Missing]" : "");
        }
    }

    private async void CloseTab_OnClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (sender is Control { DataContext: DocumentTab tab })
            await ExecuteCommandInContextAsync("document.close", CaptureCommandContext() with { DraftKey = tab.Session.DraftKey, DocumentPath = tab.Session.Path });
        eventArgs.Handled = true;
    }

    private async void CloseActiveTab_OnClick(object? sender, RoutedEventArgs eventArgs) =>
        await ExecuteCommandAsync("document.close");

    private async Task<bool> CloseTabAsync(DocumentTab tab)
    {
        if (!await ResolvePendingMetadataAsync()) return false;
        if (!_tabs.Contains(tab)) return true;
        await SelectTabAsync(tab);
        if (!_tabs.Contains(tab) || _activeTab != tab) return false;
        if (!await ConfirmSafeToReplaceSessionAsync()) return false;
        await _recoveryGate.WaitAsync();
        try
        {
            _selectingTab = true;
            _tabs.Remove(tab);
            _selectingTab = false;
        }
        finally { _recoveryGate.Release(); }
        if (_tabs.Count == 0) ActivateTab(AddTab(_documents.NewDocument()), "New unsaved document");
        else ActivateTab(_tabs[Math.Max(0, _tabs.Count - 1)], "Closed document.");
        RefreshTabLabels();
        return true;
    }

    private async Task<bool> ConfirmAllTabsAsync()
    {
        if (!await ResolvePendingMetadataAsync()) return false;
        var original = _activeTab;
        foreach (var tab in _tabs.ToArray())
        {
            if (!tab.Session.IsDirty) continue;
            await SelectTabAsync(tab);
            if (!await ConfirmSafeToReplaceSessionAsync()) return false;
        }
        if (_tabs.Contains(original)) await SelectTabAsync(original);
        return true;
    }

    private async Task<bool> ConfirmEnrollmentTabsAsync(IReadOnlyList<string> paths)
    {
        foreach (var path in paths)
        {
            if (FindTab(path) is not { } tab) continue;
            await SelectTabAsync(tab);
            if (!await ConfirmSafeToReplaceSessionAsync()) return false;
            if (tab.Session.Conflict is not null)
            {
                UpdateChrome("Resolve the selected document conflict before enrollment.");
                return false;
            }
        }
        return true;
    }

    internal IReadOnlyList<DocumentTab> TabsForTesting => _tabs;
    internal Task SelectTabForTestingAsync(DocumentTab tab) => SelectTabAsync(tab);
    internal Task<bool> CloseTabForTestingAsync(DocumentTab tab) => CloseTabAsync(tab);
    internal Task HandleExternalChangeForTestingAsync(string path) => HandleExternalChangeAsync(path);
    internal void HandleRenameForTesting(string oldPath, string newPath) => _ = HandleRenameAsync(oldPath, newPath);
    internal Task HandleRenameForTestingAsync(string oldPath, string newPath) => HandleRenameAsync(oldPath, newPath);
    internal Task NewTabForTestingAsync() => LoadSessionAsync(_documents.NewDocument(), "New unsaved document");
    internal Func<DocumentSession, Task<string>>? UnsavedChoiceForTesting { get; set; }
    internal Func<RecoveryDraft, Task<string>>? RecoveryChoiceForTesting { get; set; }
    internal Task RestoreDraftsForTestingAsync() => RestoreDraftIfAvailableAsync();
    internal Task<bool> ConfirmAllTabsForTestingAsync() => ConfirmAllTabsAsync();
}
