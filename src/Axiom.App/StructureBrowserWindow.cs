using Avalonia.Controls;
using Avalonia.Layout;
using Axiom.Core;

namespace Axiom.App;

internal sealed class StructureBrowserWindow : Window
{
    internal StructureBrowserWindow(DocumentStructure structure)
    {
        Title = "Document structure";
        Width = 800;
        Height = 600;
        MinWidth = 480;
        MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var list = new ListBox { ItemsSource = structure.Nodes };
        list.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<DocumentStructureNode>((node, _) => new TextBlock
        {
            Text = node is null ? "" : $"{node.Kind} · line {node.Source.LineNumber}, column {node.Source.ColumnNumber} · {node.Source.Length:N0} characters\n{(node.Checked is { } done ? done ? "[x] " : "[ ] " : "")}{node.Text[..Math.Min(node.Text.Length, 160)]}"
          , TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });
        Avalonia.Automation.AutomationProperties.SetName(list, "Structured Markdown nodes with exact source positions");
        var navigate = new Button { Content = "Show source", IsEnabled = false };
        var close = new Button { Content = "Close", IsCancel = true };
        list.SelectionChanged += (_, _) => navigate.IsEnabled = list.SelectedItem is DocumentStructureNode;
        navigate.Click += (_, _) => Close(list.SelectedItem as DocumentStructureNode);
        close.Click += (_, _) => Close(null);
        var grid = new Grid { Margin = new Avalonia.Thickness(20), RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 12 };
        grid.Children.Add(new TextBlock { Text = "Read-only headings, sections, tasks, links, code and tables from the current buffer. Choose Show source to select the exact source range.", TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        grid.Children.Add(list);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { navigate, close } };
        grid.Children.Add(buttons);
        Grid.SetRow(list, 1);
        Grid.SetRow(buttons, 2);
        Content = grid;
    }
}
