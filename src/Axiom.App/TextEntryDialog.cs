using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;

namespace Axiom.App;

internal sealed class TextEntryDialog : Window
{
    private TextEntryDialog(string title, string message, string initial)
    {
        Title = title;
        Width = 600;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var entry = new TextBox { Text = initial };
        Avalonia.Automation.AutomationProperties.SetName(entry, title);
        var confirm = new Button { Content = "Continue", IsDefault = true };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        confirm.Click += (_, _) => Close(entry.Text);
        cancel.Click += (_, _) => Close(null);
        Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20)
                    ,
            Spacing = 12
                    ,
            Children =
                      {
                          new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap }
                        , entry
                        , new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { confirm, cancel } }
                      }
        };
        Opened += (_, _) => { entry.Focus(); entry.SelectAll(); };
        KeyDown += (_, eventArgs) => { if (eventArgs.Key == Key.Escape) Close(null); };
    }

    public static Task<string?> ShowAsync(Window owner, string title, string message, string initial) =>
        new TextEntryDialog(title, message, initial).ShowDialog<string?>(owner);
}
