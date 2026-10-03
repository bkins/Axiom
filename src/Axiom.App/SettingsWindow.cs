using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Axiom.Core;

namespace Axiom.App;

internal sealed class SettingsWindow : Window
{
    public SettingsWindow(EditorPreferences current, AppearancePreference appearance, Action help)
    {
        Title = "Axiom Settings";
        Width = 500;
        Height = 540;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var font = new TextBox { Text = current.FontFamily, PlaceholderText = "Font family" };
        var size = new NumericUpDown { Minimum = 8, Maximum = 40, Value = current.FontSize, Increment = 1 };
        var wrap = new CheckBox { Content = "Wrap long editor lines", IsChecked = current.WordWrap };
        var chrome = new CheckBox { Content = "Compact title bar (Windows); turn off for standard system title bar", IsChecked = current.CompactTitleBar };
        var tabs = new NumericUpDown { Minimum = 1, Maximum = 8, Value = current.TabWidth, Increment = 1 };
        var theme = new ComboBox { ItemsSource = Enum.GetValues<AppearancePreference>(), SelectedItem = appearance };
        AutomationProperties.SetName(font, "Editor font family");
        AutomationProperties.SetName(size, "Editor font size from 8 to 40");
        AutomationProperties.SetName(tabs, "Editor tab width from 1 to 8");
        AutomationProperties.SetName(theme, "Application appearance");
        var menuStyle = new ComboBox { ItemsSource = Enum.GetValues<MenuStyle>(), SelectedItem = current.MenuStyle };
        AutomationProperties.SetName(menuStyle, "Menu style: Quiet or Rounded");
        var message = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var apply = new Button { Content = "Apply" };
        apply.Click += (_, _) =>
        {
            try
            {
                var value = new EditorPreferences(font.Text ?? string.Empty, (int)(size.Value ?? 15), wrap.IsChecked == true, (int)(tabs.Value ?? 4), (MenuStyle)(menuStyle.SelectedItem ?? MenuStyle.Quiet)) { CompactTitleBar = chrome.IsChecked == true };
                value.Validate();
                Close((value, (AppearancePreference)(theme.SelectedItem ?? AppearancePreference.System)));
            }
            catch (ArgumentException exception) { message.Text = exception.Message; }
        };
        var cancel = new Button { Content = "Cancel" };
        cancel.Click += (_, _) => Close();
        var reset = new Button { Content = "Reset editor defaults" };
        reset.Click += (_, _) => { var defaults = new EditorPreferences(); font.Text = defaults.FontFamily; size.Value = defaults.FontSize; wrap.IsChecked = defaults.WordWrap; tabs.Value = defaults.TabWidth; };
        var helpButton = new Button { Content = "Help / F1" };
        helpButton.Click += (_, _) => help();
        KeyDown += (_, args) => { if (args.Key == Avalonia.Input.Key.F1) { help(); args.Handled = true; } };
        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 10,
                Children =
        {
            new TextBlock { Text = "Editor font family" }, font,
            new TextBlock { Text = "Font size (8–40)" }, size, wrap,
            new TextBlock { Text = "Tab width (1–8 columns)" }, tabs,
            new TextBlock { Text = "Application appearance" }, theme,
            new TextBlock { Text = "Menu style" }, menuStyle, chrome, message,
            new WrapPanel { Children = { apply, cancel, reset, helpButton } }
        }
            }
        };
    }
}
