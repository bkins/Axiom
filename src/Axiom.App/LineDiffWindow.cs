using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Axiom.Core;

namespace Axiom.App;

internal sealed class LineDiffWindow : Window
{
    internal LineDiffWindow(string previousTitle, string previous, string currentTitle, string current)
    {
        Title = "Compare document revisions";
        Width = 960;
        Height = 680;
        MinWidth = 480;
        MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var lines = new LineDiffService().Compare(previous, current);
        var summary = new TextBlock
        {
            Text         = $"Previous: {previousTitle}\nCurrent: {currentTitle}\n{lines.Count(line => line.Kind == LineDiffKind.Added)} added / {lines.Count(line => line.Kind == LineDiffKind.Removed)} removed lines. − removed; + added; · unchanged. Line endings: ↵ CRLF, ␊ LF, ␍ CR."
          , TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };
        var output = new TextBox
        {
            IsReadOnly      = true
          , AcceptsReturn   = true
          , FontFamily      = new Avalonia.Media.FontFamily("Consolas")
          , TextWrapping    = Avalonia.Media.TextWrapping.NoWrap
          , Text            = string.Join('\n', lines.Select(line =>
                $"{(line.Kind == LineDiffKind.Added ? '+' : line.Kind == LineDiffKind.Removed ? '−' : '·')} {line.PreviousLine?.ToString() ?? "-",6} {line.CurrentLine?.ToString() ?? "-",6}  " +
                line.Text.Replace("\r\n", "↵", StringComparison.Ordinal).Replace("\r", "␍", StringComparison.Ordinal).Replace("\n", "␊", StringComparison.Ordinal)))
        };
        Avalonia.Automation.AutomationProperties.SetName(output, "Read-only line comparison with previous and current line numbers");
        var close = new Button { Content = "Close"
              , IsCancel = true };
        close.Click += (_, _) => Close();
        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto")
          , RowSpacing     = 12
          , Margin         = new Thickness(20)
          , Children       = { summary, output, close }
        };
        Grid.SetRow(output, 1);
        Grid.SetRow(close, 2);
        Content = grid;
    }
}
