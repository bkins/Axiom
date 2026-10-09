using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Axiom.Core;

namespace Axiom.App;

internal sealed class ToolbarCustomizationWindow : Window
{
    internal ListBox Choices { get; }
    internal ObservableCollection<ToolbarChoice> Items { get; } = [];
    internal CommandPlacement Placement { get; private set; }
    internal ToolbarCustomizationWindow(string commands, CommandPlacement placement = CommandPlacement.Top)
    {
        Title = "Customize toolbar"; Width = 470; Height = 550; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        void Load(string value)
        {
            var visible = ToolbarLayout.Read(value);
            Items.Clear();
            foreach (var id in visible.Concat(ToolbarLayout.Commands.Select(command => command.Id).Except(visible)))
                Items.Add(new(ToolbarLayout.Commands.Single(command => command.Id == id), visible.Contains(id)));
        }
        Load(commands);
        Placement = placement;
        var location = new ComboBox { ItemsSource = new[] { "Top commands", "Side commands" }, SelectedIndex = (int)placement };
        Avalonia.Automation.AutomationProperties.SetName(location, "Command placement");
        location.SelectionChanged += (_, _) => Placement = (CommandPlacement)location.SelectedIndex;
        Choices = new ListBox { ItemsSource = Items, ItemTemplate = new FuncDataTemplate<ToolbarChoice>((item, _) =>
        {
            if (item is null) return new TextBlock();
            var checkbox = new CheckBox { Content = item.Command.Title, IsChecked = item.Visible };
            checkbox.IsCheckedChanged += (_, _) => item.Visible = checkbox.IsChecked == true;
            return checkbox;
        }) };
        Avalonia.Automation.AutomationProperties.SetName(Choices, "Toolbar commands; check to show, select a row to reorder");
        void Move(int direction) { var index = Choices.SelectedIndex; if (index < 0 || index + direction < 0 || index + direction >= Items.Count) return; Items.Move(index, index + direction); Choices.SelectedIndex = index + direction; }
        var up = new Button { Content = "Move up" }; up.Click += (_, _) => Move(-1);
        var down = new Button { Content = "Move down" }; down.Click += (_, _) => Move(1);
        var reset = new Button { Content = "Reset" }; reset.Click += (_, _) => { Load(ToolbarLayout.DefaultCommands); location.SelectedIndex = 0; };
        var apply = new Button { Content = "Apply" }; apply.Click += (_, _) => Close(string.Join(',', Items.Where(item => item.Visible).Select(item => item.Command.Id)));
        var cancel = new Button { Content = "Cancel" }; cancel.Click += (_, _) => Close();
        KeyDown += (_, args) => { if (args.Key == Avalonia.Input.Key.Escape) { Close(); args.Handled = true; } };
        var info = new TextBlock { Text = "Show/hide commands and change their order. The main menu stays available. Hidden commands remain in the palette and retain their keyboard shortcuts.", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var buttons = new WrapPanel { Children = { up, down, reset, apply, cancel } }; foreach (var button in buttons.Children) button.Margin = new Thickness(0,0,8,6);
        var grid = new Grid { Margin = new Thickness(16), RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"), RowSpacing = 10, Children = { info, location, Choices, buttons } };
        Grid.SetRow(location,1); Grid.SetRow(Choices,2); Grid.SetRow(buttons,3); Content = grid;
    }
}
