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
        var currentLine = new CheckBox { Content = "Subtle current-line highlight", IsChecked = current.HighlightCurrentLine };
        var nonPrinting = new CheckBox { Content = "Show spaces, tabs and line endings", IsChecked = current.ShowNonPrintingCharacters };
        var previewLines = new CheckBox { Content = "Show source line numbers beside Go to editor in Preview", IsChecked = current.ShowPreviewLineNumbers };
        var previewNavigation = new CheckBox { Content = "Show Go to editor navigation in Preview", IsChecked = current.ShowPreviewNavigation };
        var spelling = new CheckBox { Content = "Offline English (US) spell-check", IsChecked = current.SpellCheck };
        var personalWords = new TextBox { Text = current.PersonalDictionary, AcceptsReturn = true, Height = 80, TextWrapping = TextWrapping.Wrap, PlaceholderText = "One personal dictionary word per line" };
        AutomationProperties.SetName(personalWords, "Personal dictionary words, one per line");
        var chrome = new CheckBox { Content = "Compact title bar (Windows); turn off for standard system title bar", IsChecked = current.CompactTitleBar };
        var compactDensity = new CheckBox { Content = "Compact workbench spacing", IsChecked = current.CompactWorkbench };
        var toolbarCommands = current.ToolbarCommands;
        var commandPlacement = current.CommandPlacement;
        var discovery = new ComboBox { ItemsSource = new[] { "Right panel", "Bottom results" }, SelectedIndex = (int)current.DiscoveryPlacement };
        AutomationProperties.SetName(discovery, "Discovery layout");
        var toolbarButton = new Button { Content = "Customize toolbar visibility and order…" };
        toolbarButton.Click += async (_, _) => { var dialog = new ToolbarCustomizationWindow(toolbarCommands, commandPlacement); var choice = await dialog.ShowDialog<string?>(this); if (choice is not null) { toolbarCommands = choice; commandPlacement = dialog.Placement; } };
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
                var value = current with { FontFamily = font.Text ?? string.Empty, FontSize = (int)(size.Value ?? 15), WordWrap = wrap.IsChecked == true, TabWidth = (int)(tabs.Value ?? 4), MenuStyle = (MenuStyle)(menuStyle.SelectedItem ?? MenuStyle.Quiet), CompactTitleBar = chrome.IsChecked == true, HighlightCurrentLine = currentLine.IsChecked == true, ShowNonPrintingCharacters = nonPrinting.IsChecked == true, CommandPlacement = commandPlacement, DiscoveryPlacement = (DiscoveryPlacement)discovery.SelectedIndex };
                value = value with { ShowPreviewLineNumbers = previewLines.IsChecked == true, ShowPreviewNavigation = previewNavigation.IsChecked == true, SpellCheck = spelling.IsChecked == true, PersonalDictionary = string.Join('\n', (personalWords.Text ?? "").Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase)) };
                value = value with { ToolbarCommands = toolbarCommands, CompactWorkbench = compactDensity.IsChecked == true };
                value.Validate();
                Close((value, (AppearancePreference)(theme.SelectedItem ?? AppearancePreference.System)));
            }
            catch (ArgumentException exception) { message.Text = exception.Message; }
        };
        var cancel = new Button { Content = "Cancel" };
        cancel.Click += (_, _) => Close();
        var reset = new Button { Content = "Reset editor defaults" };
        reset.Click += (_, _) => { var defaults = new EditorPreferences(); font.Text = defaults.FontFamily; size.Value = defaults.FontSize; wrap.IsChecked = defaults.WordWrap; tabs.Value = defaults.TabWidth; currentLine.IsChecked = defaults.HighlightCurrentLine; nonPrinting.IsChecked = defaults.ShowNonPrintingCharacters; previewLines.IsChecked = defaults.ShowPreviewLineNumbers; previewNavigation.IsChecked = defaults.ShowPreviewNavigation; spelling.IsChecked = defaults.SpellCheck; };
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
            new TextBlock { Text = "Font size (8–40)" }, size, wrap, currentLine, nonPrinting, previewNavigation, previewLines, spelling,
            new TextBlock { Text = "Personal dictionary (up to 1,000 words; remove a line to remove a word)" }, personalWords,
            new TextBlock { Text = "Tab width (1–8 columns)" }, tabs,
            new TextBlock { Text = "Application appearance" }, theme,
            new TextBlock { Text = "Menu style" }, menuStyle, chrome, compactDensity, toolbarButton,
            new TextBlock { Text = "Discovery layout" }, discovery, message,
            new WrapPanel { Children = { apply, cancel, reset, helpButton } }
        }
            }
        };
    }
}
