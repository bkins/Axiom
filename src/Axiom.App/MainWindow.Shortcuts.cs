using Avalonia.Input;
using Avalonia.Interactivity;
using Axiom.Core;

namespace Axiom.App;

public partial class MainWindow
{
    private ShortcutConfiguration _shortcutConfiguration = new();
    private IReadOnlyDictionary<string, string?> _shortcutBindings = new Dictionary<string, string?>();

    internal void ConfigureShortcutsForTesting(ShortcutConfiguration configuration)
    {
        var resolved = configuration.Resolve(_commands.Commands);
        _shortcutConfiguration = configuration;
        _shortcutBindings = resolved;
    }

    internal Task<bool> ExecuteShortcutForTestingAsync(KeyEventArgs eventArgs) => TryExecuteShortcutAsync(eventArgs);

    private async Task InitializeShortcutsAsync()
    {
        try
        {
            if (ManagementState is not null) _shortcutConfiguration = await ManagementState.GetShortcutConfigurationAsync();
            _shortcutBindings = _shortcutConfiguration.Resolve(_commands.Commands);
        }
        catch (Exception exception)
        {
            _shortcutConfiguration = new();
            _shortcutBindings = _shortcutConfiguration.Resolve(_commands.Commands);
            ReportError("shortcuts.load.failed", exception, null);
        }
    }

    private async void ShortcutSettings_OnClick(object? sender, RoutedEventArgs eventArgs)
        => await ExecuteCommandAsync("application.shortcuts");

    private async Task<CommandResult> ConfigureShortcutsAsync()
    {
        try
        {
            var selected = await new ShortcutSettingsWindow(_commands.Commands, _shortcutConfiguration).ShowDialog<ShortcutConfiguration?>(this);
            if (selected is null) return new(CommandOutcome.Cancelled, "Shortcut changes cancelled.");
            if (ManagementState is null) throw new IOException("Shortcut settings storage is unavailable.");
            var resolved = selected.Resolve(_commands.Commands);
            await ManagementState.SetShortcutConfigurationAsync(selected, _commands.Commands);
            _shortcutConfiguration = selected;
            _shortcutBindings = resolved;
            UpdateChrome("Keyboard shortcuts applied.");
            return new(CommandOutcome.Completed, "Keyboard shortcuts applied.");
        }
        catch (Exception exception) { ReportError("shortcuts.apply.failed", exception, null); return new(CommandOutcome.Failed, exception.Message, exception); }
    }

    private async Task<bool> TryExecuteShortcutAsync(KeyEventArgs eventArgs)
    {
        if ((eventArgs.KeyModifiers & KeyModifiers.Meta) != 0) return false;
        var key = eventArgs.Key.ToString();
        if (key.Length == 2 && key[0] == 'D' && char.IsDigit(key[1])) key = key[1..];
        var gesture = string.Join('+', new[]
        {
            eventArgs.KeyModifiers.HasFlag(KeyModifiers.Control) ? "Ctrl" : null
          , eventArgs.KeyModifiers.HasFlag(KeyModifiers.Alt) ? "Alt" : null
          , eventArgs.KeyModifiers.HasFlag(KeyModifiers.Shift) ? "Shift" : null
        }.OfType<string>().Append(key));
        var binding = _shortcutBindings.FirstOrDefault(entry => entry.Value == gesture);
        if (binding.Key is null) return false;
        if (binding.Key.StartsWith("editor.", StringComparison.Ordinal) && !IsEditorCommandTarget()) return false;
        eventArgs.Handled = true;
        await ExecuteCommandAsync(binding.Key);
        return true;
    }
}
