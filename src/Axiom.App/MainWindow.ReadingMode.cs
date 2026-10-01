using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Axiom.App;

public partial class MainWindow
{
    private bool _readingMode;
    private ColumnDefinitions? _editingColumns;
    private readonly SemaphoreSlim _readingModeGate = new(1, 1);

    private void ApplyReadingMode(bool enabled)
    {
        if (enabled == _readingMode) return;
        if (enabled)
        {
            CaptureEditorState(_activeTab);
            _editingColumns = new ColumnDefinitions();
            foreach (var column in EditorPreviewLayout.ColumnDefinitions)
                _editingColumns.Add(new ColumnDefinition
                {
                    Width    = column.Width
                  , MinWidth = column.MinWidth
                  , MaxWidth = column.MaxWidth
                });
        }
        _readingMode = enabled;
        EditorPane.IsVisible = EditorPreviewSplitter.IsVisible = !enabled;
        EditorPreviewLayout.ColumnDefinitions = enabled ? new ColumnDefinitions("0,0,*") : _editingColumns ?? new ColumnDefinitions("*,5,*");
        ReadingModeButton.Content = enabled ? "Show editor" : "Read";
        if (!enabled)
        {
            var tab = _activeTab;
            Dispatcher.UIThread.Post(() =>
            {
                if (_readingMode || tab != _activeTab) return;
                Editor.UpdateLayout();
                Editor.ScrollToHorizontalOffset(tab.HorizontalOffset);
                Editor.ScrollToVerticalOffset(tab.VerticalOffset);
                Editor.Focus();
            }, DispatcherPriority.Loaded);
        }
        else Preview.Focus();
    }

    internal async Task ToggleReadingModeAsync()
    {
        if (!await _readingModeGate.WaitAsync(0)) return;
        ReadingModeButton.IsEnabled = false;
        ApplyReadingMode(!_readingMode);
        try
        {
            if (_recovery is Axiom.Core.IReadingModeStore store) await store.SetReadingModeAsync(_readingMode);
            UpdateChrome(_readingMode ? "Reading mode; unsaved buffers remain protected. Show editor to edit." : "Editor restored; buffers and undo retained.");
        }
        catch (Exception exception) { ReportError("reading.preference.failed", exception, _session.Path); }
        finally { ReadingModeButton.IsEnabled = true; _readingModeGate.Release(); }
    }

    private async void ReadingMode_OnClick(object? sender, RoutedEventArgs args) => await ExecuteCommandAsync("view.reading");
    internal bool ReadingModeForTesting => _readingMode;
}
