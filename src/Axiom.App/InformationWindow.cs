using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Input.Platform;

namespace Axiom.App;

internal sealed class InformationWindow : Window
{
    public InformationWindow(string title, Func<Task<string>> load, Func<Task<string>>? action = null, string? actionLabel = null)
    {
        Title = title;
        Width = 760;
        Height = 480;
        MinWidth = 520;
        MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var text = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        Avalonia.Automation.AutomationProperties.SetName(text, title + " information");
        var refresh = new Button { Content = "Refresh" };
        var copy = new Button { Content = "Copy" };
        var close = new Button { Content = "Close", IsCancel = true };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { refresh, copy, close } };
        if (action is not null)
        {
            var button = new Button { Content = actionLabel ?? "Run" };
            button.Click += async (_, _) =>
            {
                button.IsEnabled = false;
                try { text.Text = await action(); }
                catch (Exception exception) { text.Text = exception.Message; }
                finally { button.IsEnabled = true; }
            };
            actions.Children.Insert(0, button);
        }
        refresh.Click += async (_, _) => await RefreshAsync();
        copy.Click += async (_, _) =>
        {
            try { if (Clipboard is not null) await Clipboard.SetTextAsync(text.Text); }
            catch (Exception exception) { text.Text += "\nCopy failed: " + exception.Message; }
        };
        close.Click += (_, _) => Close();
        Opened += async (_, _) => await RefreshAsync();
        Content = new DockPanel { Margin = new Avalonia.Thickness(18), Children = { actions, text } };
        DockPanel.SetDock(actions, Dock.Bottom);

        async Task RefreshAsync()
        {
            refresh.IsEnabled = false;
            try { text.Text = await load(); }
            catch (Exception exception) { text.Text = "Unable to load information: " + exception.Message; }
            finally { refresh.IsEnabled = true; }
        }
    }
}
