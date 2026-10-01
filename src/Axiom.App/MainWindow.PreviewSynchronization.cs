using System.Globalization;
using System.Text.Json;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Axiom.App;

public partial class MainWindow
{
    private readonly DispatcherTimer _scrollSyncTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private bool _scrollSyncEnabled;
    private bool _scrollSyncRunning;
    private bool _editorScrollPending;
    private long _suppressEditorScrollUntil;
    private int _lastPreviewUserScroll;

    private async Task CapturePreviewAnchorAsync()
    {
        var tab = _activeTab;
        var rendered = _previewLinkStructure;
        if (rendered is null) return;
        var result = await Preview.InvokeScript("(() => { const blocks = [...document.querySelectorAll('.source-block')]; let block = blocks[0]; for (const candidate of blocks) { if (candidate.getBoundingClientRect().top <= 18) block = candidate; else break; } if (!block) return null; const fraction = Math.max(0, Math.min(1, (12 - block.getBoundingClientRect().top) / Math.max(1, block.offsetHeight))); return { start: +block.dataset.sourceStart, end: +block.dataset.sourceEnd, fraction }; })()");
        if (result is null || tab != _activeTab || rendered != _previewLinkStructure) return;
        using var json = JsonDocument.Parse(result);
        if (json.RootElement.ValueKind != JsonValueKind.Object) return;
        var start = json.RootElement.GetProperty("start").GetInt32();
        var end = json.RootElement.GetProperty("end").GetInt32();
        if (start < 0 || end < start || end >= rendered.SourceText.Length) return;
        tab.PreviewAnchorPrefix = rendered.SourceText.Substring(start, Math.Min(128, end - start + 1));
        tab.PreviewAnchorRelativeOffset = (int)Math.Round((end - start) * json.RootElement.GetProperty("fraction").GetDouble());
    }

    private async Task<bool> RestorePreviewAnchorAsync()
    {
        if (_activeTab.PreviewAnchorPrefix is not { Length: > 0 } prefix) return false;
        var source = Editor.Text;
        var found = source.IndexOf(prefix, StringComparison.Ordinal);
        if (found < 0 || found != source.LastIndexOf(prefix, StringComparison.Ordinal)) return false;
        await ScrollPreviewToSourceAsync(found + _activeTab.PreviewAnchorRelativeOffset);
        return true;
    }

    private void InitializePreviewSynchronization()
    {
        Editor.PointerWheelChanged += (_, _) => _suppressEditorScrollUntil = 0;
        Editor.TextArea.TextView.ScrollOffsetChanged += (_, _) =>
        {
            if (_scrollSyncEnabled && !_readingMode && !_loading && Environment.TickCount64 > _suppressEditorScrollUntil)
                _editorScrollPending = true;
        };
        _scrollSyncTimer.Tick += async (_, _) => await SynchronizeScrollAsync();
        _scrollSyncTimer.Start();
        Closed += (_, _) => _scrollSyncTimer.Stop();
    }

    private async void ToggleScrollSync_OnClick(object? sender, RoutedEventArgs eventArgs) => await ExecuteCommandAsync("view.scrollSync");

    private void ToggleScrollSynchronization()
    {
        _scrollSyncEnabled = !_scrollSyncEnabled;
        ScrollSyncButton.Content = _scrollSyncEnabled ? "Scroll sync: on" : "Scroll sync: off";
        _editorScrollPending = _scrollSyncEnabled;
        UpdateChrome(_scrollSyncEnabled ? "Editor and preview scrolling synchronized. Toggle off for independent reading." : "Editor and preview scroll independently.");
    }

    private async void ShowPreviewAtCaret_OnClick(object? sender, RoutedEventArgs eventArgs) =>
        await ExecuteCommandAsync("view.previewAtCaret");

    private async Task HandlePreviewSourceAsync(Uri request)
    {
        var parts = request.AbsolutePath.Trim('/').Split('/');
        if (request.Host != "show" || parts.Length != 2 || parts[0] != _previewLinkToken ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var offset) ||
            _previewLinkStructure?.SourceText != Editor.Text || _sessionTransitionRunning || _navigationRunning) return;
        if (offset < 0 || offset > Editor.Document.TextLength) return;
        if (_readingMode) await ToggleReadingModeAsync();
        Editor.CaretOffset = offset;
        Editor.Select(offset, 0);
        Editor.ScrollToLine(Editor.Document.GetLineByOffset(offset).LineNumber);
        Editor.Focus();
        UpdateChrome("Moved to this preview block's Markdown source.");
    }

    internal async Task ScrollPreviewToSourceAsync(int offset)
    {
        if (!_previewReady || _previewLinkStructure?.SourceText != Editor.Text || _previewLinkToken is null) return;
        var token = _previewLinkToken;
        var safeOffset = Math.Clamp(offset, 0, Editor.Document.TextLength).ToString(CultureInfo.InvariantCulture);
        var script = $$"""
            (() => {
              if (document.documentElement.dataset.linkToken !== '{{token}}') return false;
              const blocks = [...document.querySelectorAll('.source-block')];
              const target = {{safeOffset}};
              let block = blocks[0];
              for (const candidate of blocks) { if (+candidate.dataset.sourceStart <= target) block = candidate; else break; }
              if (!block) return false;
              const first = +block.dataset.sourceStart, last = +block.dataset.sourceEnd;
              const fraction = Math.max(0, Math.min(1, (target - first) / Math.max(1, last - first)));
              if (window.__axiomReading) window.__axiomReading.ignoreUntil = performance.now() + 350;
              window.scrollTo(0, window.scrollY + block.getBoundingClientRect().top + fraction * block.offsetHeight - 12);
              return true;
            })()
            """;
        try { await Preview.InvokeScript(script); }
        catch (Exception exception) { _log.Error("preview.source.scroll.failed", exception, _session.Path); }
    }

    private async Task SynchronizeScrollAsync()
    {
        if (!_scrollSyncEnabled || _scrollSyncRunning || !_previewReady || _readingMode || _loading ||
            _sessionTransitionRunning || _navigationRunning || _previewLinkStructure?.SourceText != Editor.Text || _previewLinkToken is null) return;
        _scrollSyncRunning = true;
        var tab = _activeTab;
        var token = _previewLinkToken;
        try
        {
            if (_editorScrollPending)
            {
                _editorScrollPending = false;
                Editor.TextArea.TextView.EnsureVisualLines();
                var line = Editor.TextArea.TextView.VisualLines.FirstOrDefault()?.FirstDocumentLine.LineNumber ?? 1;
                await ScrollPreviewToSourceAsync(Editor.Document.GetLineByNumber(line).Offset);
                return;
            }
            var result = await Preview.InvokeScript($$"""
                (() => {
                  if (document.documentElement.dataset.linkToken !== '{{token}}') return null;
                  if (!window.__axiomReading) {
                    const state = window.__axiomReading = { counter: 0, ignoreUntil: 0, inputUntil: 0 };
                    for (const name of ['wheel','touchstart','pointerdown','keydown'])
                      window.addEventListener(name, () => { state.inputUntil = performance.now() + 1200; state.ignoreUntil = 0; }, { passive: true });
                    window.addEventListener('scroll', () => {
                      if (performance.now() > state.ignoreUntil && performance.now() < state.inputUntil) state.counter++;
                    }, { passive: true });
                  }
                  const blocks = [...document.querySelectorAll('.source-block')];
                  let block = blocks[0];
                  for (const candidate of blocks) { if (candidate.getBoundingClientRect().top <= 18) block = candidate; else break; }
                  if (!block) return null;
                  const fraction = Math.max(0, Math.min(1, (12 - block.getBoundingClientRect().top) / Math.max(1, block.offsetHeight)));
                  return { counter: window.__axiomReading.counter, offset: Math.round(+block.dataset.sourceStart + fraction * (+block.dataset.sourceEnd - +block.dataset.sourceStart)) };
                })()
                """);
            if (tab != _activeTab || token != _previewLinkToken || _previewLinkStructure?.SourceText != Editor.Text || result is null) return;
            using var json = JsonDocument.Parse(result);
            if (json.RootElement.ValueKind != JsonValueKind.Object) return;
            var counter = json.RootElement.GetProperty("counter").GetInt32();
            if (counter == _lastPreviewUserScroll) return;
            _lastPreviewUserScroll = counter;
            var offset = Math.Clamp(json.RootElement.GetProperty("offset").GetInt32(), 0, Editor.Document.TextLength);
            _suppressEditorScrollUntil = Environment.TickCount64 + 350;
            Editor.ScrollToLine(Editor.Document.GetLineByOffset(offset).LineNumber);
        }
        catch (Exception exception) { _log.Error("preview.scroll.sync.failed", exception, _session.Path); }
        finally { _scrollSyncRunning = false; }
    }

    private void ResetPreviewSynchronization()
    {
        _editorScrollPending = false;
        _lastPreviewUserScroll = 0;
        _suppressEditorScrollUntil = Environment.TickCount64 + 500;
    }

    internal Task SynchronizeScrollForTestingAsync() => SynchronizeScrollAsync();
    internal void ToggleScrollSyncForTesting() => ToggleScrollSync_OnClick(null, new RoutedEventArgs());
}
