using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Axiom.Core;

namespace Axiom.App;

internal sealed class ShortcutSettingsWindow : Window
{
    internal ShortcutSettingsWindow(IReadOnlyList<AxiomCommand> commands, ShortcutConfiguration current)
    {
        Title = "Keyboard shortcuts";
        Width = 760;
        Height = 580;
        MinWidth = 480;
        MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var bindings = current.Resolve(commands);
        var entries = new Dictionary<string, TextBox>();
        var rows = new StackPanel { Spacing = 8 };
        foreach (var command in commands)
        {
            var input = new TextBox { Text = bindings[command.Id] ?? ""
              , IsReadOnly = command.Id is "editor.undo" or "editor.redo" };
            Avalonia.Automation.AutomationProperties.SetName(input, command.Title + " shortcut");
            entries.Add(command.Id, input);
            var label = new TextBlock { Text = command.Title
              , VerticalAlignment = VerticalAlignment.Center
              , TextWrapping = Avalonia.Media.TextWrapping.Wrap };
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,200")
              , ColumnSpacing = 12
              , Children = { label, input } };
            Grid.SetColumn(input, 1);
            rows.Children.Add(row);
        }
        var errors = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        Avalonia.Automation.AutomationProperties.SetName(errors, "Shortcut validation result");
        var apply = new Button { Content = "Apply"
              , IsDefault = true };
        var cancel = new Button { Content = "Cancel"
              , IsCancel = true };
        var reset = new Button { Content = "Reset defaults" };
        reset.Click += (_, _) => { foreach (var command in commands) entries[command.Id].Text = command.DefaultShortcut ?? ""; errors.Text = "Defaults staged. Apply to save or Cancel to retain existing shortcuts."; };
        cancel.Click += (_, _) => Close(null);
        apply.Click += (_, _) =>
        {
            try
            {
                var overrides = new Dictionary<string, string?>();
                foreach (var command in commands)
                {
                    var value = entries[command.Id].Text?.Trim();
                    if (string.IsNullOrEmpty(value)) value = null;
                    if (value != command.DefaultShortcut) overrides.Add(command.Id, value);
                }
                var staged = new ShortcutConfiguration { Overrides = overrides };
                staged.Resolve(commands);
                Close(staged);
            }
            catch (ArgumentException exception) { errors.Text = exception.Message; }
        };
        var guidance = new TextBlock { Text = "Use Ctrl/Alt/Shift plus a letter, digit or F1–F12. Blank removes an application binding. Editing/navigation keys and Ctrl+Alt (AltGr) are reserved; Undo/Redo retain native bindings. Apply saves changes."
              , TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal
              , Spacing = 10
              , Children = { apply, reset, cancel } };
        var scroll = new ScrollViewer { Content = rows };
        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto")
          , RowSpacing = 12
          , Margin = new Thickness(20)
          
              , Children = { guidance, scroll, errors, buttons }
        };
        Grid.SetRow(scroll, 1);
        Grid.SetRow(errors, 2);
        Grid.SetRow(buttons, 3);
        Content = grid;
    }
}
