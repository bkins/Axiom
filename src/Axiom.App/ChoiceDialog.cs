using Avalonia.Controls;
using Avalonia.Layout;

namespace Axiom.App;

internal sealed class ChoiceDialog : Window
{
    private ChoiceDialog(string title, string message, IReadOnlyList<string> choices)
    {
        Title = title;
        Width = 540;
        MinHeight = 190;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var choice in choices)
        {
            var button = new Button { Content = choice, MinWidth = 92 };
            button.Click += (_, _) => Close(choice);
            buttons.Children.Add(button);
        }
        Content = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            Margin = new Avalonia.Thickness(22),
            Children =
            {
                new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                buttons
            }
        };
        Grid.SetRow(buttons, 1);
        Closing += (_, eventArgs) =>
        {
            if (string.IsNullOrEmpty(Result)) Result = choices.Last();
        };
    }

    private string? Result { get; set; }

    public static async Task<string> ShowAsync(Window owner, string title, string message, params string[] choices)
    {
        var dialog = new ChoiceDialog(title, message, choices);
        var result = await dialog.ShowDialog<string>(owner);
        return result ?? choices.Last();
    }

    private new void Close(object? result)
    {
        Result = result?.ToString();
        base.Close(Result);
    }
}

