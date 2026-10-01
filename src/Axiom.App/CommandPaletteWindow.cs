using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Axiom.Core;

namespace Axiom.App;

internal sealed class CommandPaletteWindow : Window
{
    internal CommandPaletteWindow(CommandDispatcher commands, CommandContext context, IReadOnlyDictionary<string, string?> bindings)
    {
        Title = "Command palette";
        Width = 720;
        Height = 480;
        MinWidth = 420;
        MinHeight = 280;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var search = new TextBox { PlaceholderText = "Search commands…" };
        var results = new ListBox();
        var run = new Button { Content = "Run"
              , IsDefault = true };
        var cancel = new Button { Content = "Cancel"
              , IsCancel = true };
        Avalonia.Automation.AutomationProperties.SetName(search, "Search commands");
        Avalonia.Automation.AutomationProperties.SetName(results, "Matching commands and availability");
        void Refresh()
        {
            var query = search.Text?.Trim() ?? "";
            results.ItemsSource = commands.Commands.Where(command => command.Title.Contains(query, StringComparison.OrdinalIgnoreCase) || command.Id.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Select(command =>
                {
                    var availability = commands.GetAvailability(command.Id, context);
                    bindings.TryGetValue(command.Id, out var shortcut);
                    return new CommandPaletteEntry(command.Id, command.Title + (shortcut is null ? "" : " — " + shortcut) +
                        (availability.Enabled ? "" : " — " + availability.Reason), availability.Enabled);
                }).ToArray();
            results.SelectedIndex = results.ItemCount > 0 ? 0 : -1;
            run.IsEnabled = results.SelectedItem is CommandPaletteEntry { Enabled: true };
        }
        results.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<CommandPaletteEntry>((entry, _) =>
            new TextBlock { Text = entry?.Display
              , TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        search.TextChanged += (_, _) => Refresh();
        results.SelectionChanged += (_, _) => run.IsEnabled = results.SelectedItem is CommandPaletteEntry { Enabled: true };
        void RunSelection()
        {
            if (results.SelectedItem is CommandPaletteEntry { Enabled: true } entry) Close(entry.Id);
        }
        run.Click += (_, _) => RunSelection();
        cancel.Click += (_, _) => Close(null);
        results.DoubleTapped += (_, _) => RunSelection();
        KeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape) { Close(null); args.Handled = true; }
            else if (args.Key == Key.Enter) { RunSelection(); args.Handled = true; }
            else if (args.Key == Key.Down && search.IsKeyboardFocusWithin) { results.Focus(); args.Handled = true; }
        };
        var buttons = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal
          
              , Spacing = 10
          
              , Children = { run, cancel }
        };
        var layout = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto")
          , RowSpacing = 12
          , Margin = new Thickness(20)
          
              , Children = { search, results, buttons }
        };
        Grid.SetRow(results, 1);
        Grid.SetRow(buttons, 2);
        Content = layout;
        Opened += (_, _) => search.Focus();
        Refresh();
    }
}
