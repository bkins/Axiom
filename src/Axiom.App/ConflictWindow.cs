using Avalonia.Controls;
using Avalonia.Layout;

namespace Axiom.App;

internal sealed class ConflictWindow : Window
{
    public ConflictWindow(string localText, string externalText)
    {
        Title = "Resolve external change";
        Width = 1100;
        Height = 720;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var local = new TextBox { Text = localText, IsReadOnly = true, AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.NoWrap };
        var external = new TextBox { Text = externalText, IsReadOnly = true, AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.NoWrap };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var pair in new[] { ("Use my edits", "local"), ("Reload disk version", "external"), ("Decide later", "cancel") })
        {
            var button = new Button { Content = pair.Item1 };
            button.Click += (_, _) => Close(pair.Item2);
            buttons.Children.Add(button);
        }
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), ColumnDefinitions = new ColumnDefinitions("*,*"), Margin = new Avalonia.Thickness(16) };
        var leftHeader = new TextBlock { Text = "Your Axiom buffer", FontWeight = Avalonia.Media.FontWeight.SemiBold, Margin = new Avalonia.Thickness(0, 0, 8, 8) };
        var rightHeader = new TextBlock { Text = "Current file on disk", FontWeight = Avalonia.Media.FontWeight.SemiBold, Margin = new Avalonia.Thickness(8, 0, 0, 8) };
        Grid.SetColumn(rightHeader, 1);
        Grid.SetRow(local, 1); local.Margin = new Avalonia.Thickness(0, 0, 4, 12);
        Grid.SetRow(external, 1); Grid.SetColumn(external, 1); external.Margin = new Avalonia.Thickness(4, 0, 0, 12);
        Grid.SetRow(buttons, 2); Grid.SetColumnSpan(buttons, 2);
        grid.Children.Add(leftHeader); grid.Children.Add(rightHeader); grid.Children.Add(local); grid.Children.Add(external); grid.Children.Add(buttons);
        Content = grid;
    }
}

