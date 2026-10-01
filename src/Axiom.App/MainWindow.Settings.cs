using Avalonia.Interactivity;
using Avalonia.Media;
using Axiom.Core;

namespace Axiom.App;

public partial class MainWindow
{
    private EditorPreferences _editorPreferences = new();

    private async Task InitializeEditorPreferencesAsync()
    {
        if (ManagementState is not null) _editorPreferences = await ManagementState.GetEditorPreferencesAsync();
        ApplyEditorPreferences(_editorPreferences);
    }

    private async void Settings_OnClick(object? sender, RoutedEventArgs args)
    {
        try
        {
            var window = new SettingsWindow(_editorPreferences, _appearance, () => _ = ShowHelpAsync("Settings and editor preferences"));
            var choice = await window.ShowDialog<(EditorPreferences Preferences, AppearancePreference Appearance)?>(this);
            if (choice is null) return;
            if (ManagementState is null) throw new InvalidOperationException("Application settings storage is unavailable.");
            await ManagementState.SetEditorPreferencesAsync(choice.Value.Preferences, choice.Value.Appearance);
            _editorPreferences = choice.Value.Preferences;
            ApplyEditorPreferences(_editorPreferences);
            _appearance = choice.Value.Appearance;
            SetRequestedAppearance(_appearance);
            ApplyAppearanceVisuals();
        }
        catch (Exception exception) { ReportError("settings.apply.failed", exception, null); }
    }

    private void ApplyEditorPreferences(EditorPreferences preferences)
    {
        preferences.Validate();
        foreach (var button in new[] { FileCommandsButton, EditCommandsButton, FormatCommandsButton })
        {
            button.Classes.Set("quiet-menu", preferences.MenuStyle == MenuStyle.Quiet);
            button.Classes.Set("rounded-menu", preferences.MenuStyle == MenuStyle.Rounded);
        }
        var requested = preferences.FontFamily.Split(',')[0].Trim();
        var available = FontManager.Current.SystemFonts.Any(font => font.Name.Equals(requested, StringComparison.OrdinalIgnoreCase));
        Editor.FontFamily = new FontFamily(available ? preferences.FontFamily : "Consolas, monospace");
        Editor.FontSize = preferences.FontSize;
        Editor.WordWrap = preferences.WordWrap;
        Editor.Options.IndentationSize = preferences.TabWidth;
        if (!available) UpdateChrome("Requested editor font is unavailable; using Consolas/monospace fallback.");
    }

    internal EditorPreferences EditorPreferencesForTesting => _editorPreferences;
    internal void ApplyEditorPreferencesForTesting(EditorPreferences value) => ApplyEditorPreferences(value);
}
