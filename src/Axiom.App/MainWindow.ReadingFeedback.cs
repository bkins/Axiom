using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Axiom.Core;

namespace Axiom.App;

public partial class MainWindow
{
    private readonly DispatcherTimer _messageTimer = new() { Interval = TimeSpan.FromSeconds(8) };
    private readonly DispatcherTimer _statisticsTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private string? _persistentFailure;
    private string _latestInformation = "Ready";
    private string? _statisticsText;
    private int _sourceWordCount;

    private void InitializeReadingFeedback()
    {
        _messageTimer.Tick += (_, _) =>
        {
            _messageTimer.Stop();
            if (_persistentFailure is null) StatusText.Text = "Ready";
        };
        _statisticsTimer.Tick += (_, _) => { _statisticsTimer.Stop(); RefreshDocumentStatistics(); };
        Editor.TextArea.Caret.PositionChanged += (_, _) => RefreshCaretStatistics();
        Closed += (_, _) => { _messageTimer.Stop(); _statisticsTimer.Stop(); };
    }

    private void ShowInformation(string message)
    {
        _latestInformation = message;
        if (_persistentFailure is not null) return;
        StatusText.Text = message;
        ToolTip.SetTip(StatusText, message);
        _messageTimer.Stop();
        _messageTimer.Start();
    }

    private void ShowPersistentFailure(string message)
    {
        _persistentFailure = message + " Review the operation and retry when the cause is resolved. Details are available; dismissal does not resolve the underlying problem.";
        _messageTimer.Stop();
        StatusText.Text = _persistentFailure;
        ToolTip.SetTip(StatusText, _persistentFailure);
        StatusDetailsButton.IsVisible = DismissStatusButton.IsVisible = true;
    }

    private async void DismissStatus_OnClick(object? sender, RoutedEventArgs eventArgs) => await ExecuteCommandAsync("view.dismissStatus");

    private void DismissStatus()
    {
        _persistentFailure = null;
        StatusDetailsButton.IsVisible = DismissStatusButton.IsVisible = false;
        ShowInformation(_latestInformation);
    }

    private async void StatusDetails_OnClick(object? sender, RoutedEventArgs eventArgs) =>
        await ExecuteCommandAsync("view.statusDetails");

    private void ScheduleDocumentStatistics()
    {
        _statisticsTimer.Stop();
        _statisticsTimer.Start();
        RefreshCaretStatistics();
    }

    private void RefreshDocumentStatistics()
    {
        if (_statisticsText != Editor.Text)
        {
            _statisticsText = Editor.Text;
            _sourceWordCount = DocumentStatistics.CountSourceWords(_statisticsText);
        }
        var newlines = DocumentStatistics.DescribeNewlines(Editor.Text);
        DocumentFormatText.Text = $"{_session.ObservedEncoding} | {newlines}";
        ToolTip.SetTip(DocumentFormatText, "Observed saved-file encoding and current-buffer newlines. Save writes UTF-8 without BOM; this display does not convert encoding.");
        RefreshCaretStatistics();
    }

    private void RefreshCaretStatistics()
    {
        if (Editor.Document is null) return;
        var location = Editor.Document.GetLocation(Math.Clamp(Editor.CaretOffset, 0, Editor.Document.TextLength));
        DocumentStatisticsText.Text = $"{_sourceWordCount:N0} words | Ln {location.Line}, Col {location.Column}";
        ToolTip.SetTip(DocumentStatisticsText, "Source words: Unicode letter/digit runs, including code, link destinations and alt text; the canonical identity marker is excluded. Line/column are one-based UTF-16 positions.");
    }

    internal void ShowFailureForTesting(string message) => ShowPersistentFailure(message);
    internal void ShowInformationForTesting(string message) => ShowInformation(message);
    internal void DismissFailureForTesting() => DismissStatus();
    internal void RefreshStatisticsForTesting() => RefreshDocumentStatistics();
}
