using Avalonia.Controls;
using Avalonia.Layout;

namespace Axiom.App;

internal sealed class WikiTargetWindow : Window
{
    internal WikiTargetWindow(string root, IReadOnlyList<string> candidates)
    {
        Title = "Choose wiki target";
        Width = 600;
        Height = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var list = new ListBox { ItemsSource = candidates };
        list.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<string>((path, _) => new TextBlock { Text = path is null ? "" : Path.GetRelativePath(root, path) });
        var open = new Button { Content = "Open selected", IsEnabled = false };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        list.SelectionChanged += (_, _) => open.IsEnabled = list.SelectedItem is string;
        open.Click += (_, _) => Close(list.SelectedItem as string);
        cancel.Click += (_, _) => Close(null);
        var grid = new Grid { Margin = new Avalonia.Thickness(20), RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 12 };
        grid.Children.Add(new TextBlock { Text = "Several documents match. Choose explicitly; no target is selected automatically." });
        grid.Children.Add(list);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { open, cancel } };
        grid.Children.Add(buttons);
        Grid.SetRow(list, 1);
        Grid.SetRow(buttons, 2);
        Content = grid;
    }
}
