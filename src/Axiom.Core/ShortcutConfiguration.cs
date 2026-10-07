namespace Axiom.Core;

public sealed record ShortcutConfiguration
{
    public int FormatVersion { get; init; } = 1;
    public Dictionary<string, string?> Overrides { get; init; } = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, string?> Resolve(IReadOnlyList<AxiomCommand> commands)
    {
        if (FormatVersion != 1) throw new ArgumentException("Unsupported shortcut configuration version.");
        var result = commands.ToDictionary(command => command.Id, command => command.DefaultShortcut, StringComparer.Ordinal);
        foreach (var entry in Overrides)
        {
            if (!result.ContainsKey(entry.Key)) continue; // Retired IDs do not bind a new command.
            if (entry.Key is "editor.undo" or "editor.redo")
                throw new ArgumentException("Undo/redo retain their native editor bindings.");
            result[entry.Key] = string.IsNullOrWhiteSpace(entry.Value) ? null : Normalize(entry.Value);
        }
        // New defaults yield to pre-existing explicit bindings without
        // rewriting the saved configuration or discarding unrelated custom shortcuts.
        foreach (var commandId in new[] { "view.search", "document.saveAll" })
        {
            if (!Overrides.ContainsKey(commandId) && result.TryGetValue(commandId, out var defaultGesture)
                && defaultGesture is not null && Overrides.Keys.Any(id => id != commandId
                    && result.TryGetValue(id, out var gesture) && gesture == Normalize(defaultGesture)))
                result[commandId] = null;
        }
        var used = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in result.Where(entry => entry.Value is not null).ToArray())
        {
            var gesture = Normalize(entry.Value!);
            if (Reserved.Contains(gesture)) throw new ArgumentException($"{gesture} is reserved for editing, navigation or command access.");
            if (!used.TryAdd(gesture, entry.Key)) throw new ArgumentException($"{gesture} conflicts with {used[gesture]}.");
            result[entry.Key] = gesture;
        }
        return result;
    }

    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
    {
        "Ctrl+A", "Ctrl+C", "Ctrl+V", "Ctrl+X", "Ctrl+F", "Ctrl+H", "Ctrl+B", "Ctrl+I", "Ctrl+K"
      , "Ctrl+Tab", "Ctrl+Shift+Tab", "Ctrl+Enter", "Ctrl+W", "Ctrl+Shift+P", "Alt+F", "Alt+E", "Alt+M"
      , "Alt+Left", "Alt+Right", "Alt+F4", "F5", "F10"
    };

    public static string Normalize(string value)
    {
        var parts = value.Split('+', StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || parts.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("Enter a gesture such as Ctrl+Shift+S.");
        var modifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in parts[..^1])
        {
            var modifier = part.ToUpperInvariant() switch { "CTRL" => "Ctrl", "ALT" => "Alt", "SHIFT" => "Shift", _ => null };
            if (modifier is null || !modifiers.Add(modifier))
                throw new ArgumentException("Use Ctrl, Alt and Shift once each, followed by a key.");
        }
        if (modifiers.Contains("Ctrl") && modifiers.Contains("Alt")) throw new ArgumentException("Ctrl+Alt is reserved for AltGr text input.");
        var key = parts[^1].ToUpperInvariant();
        var function = key.StartsWith('F') && int.TryParse(key[1..], out var number) && number is >= 1 and <= 12;
        if (function) key = "F" + int.Parse(key[1..]);
        if (!function && !(key.Length == 1 && char.IsAsciiLetterOrDigit(key[0])))
            throw new ArgumentException("Use a letter, digit or F1–F12 key.");
        if (!function && !modifiers.Contains("Ctrl") && !modifiers.Contains("Alt"))
            throw new ArgumentException("Text keys require Ctrl or Alt to preserve typing.");
        return string.Join('+', new[] { "Ctrl", "Alt", "Shift" }.Where(modifiers.Contains).Append(key));
    }
}
