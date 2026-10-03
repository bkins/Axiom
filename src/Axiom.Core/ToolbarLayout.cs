namespace Axiom.Core;

public static class ToolbarLayout
{
    public const string DefaultCommands = "navigation.back,navigation.forward,document.new,document.save,view.reading,help.open,application.settings,editor.insertImage,document.enroll,document.pin,view.scrollSync,view.previewAtCaret";
    public static IReadOnlyList<ToolbarCommandDefinition> Commands { get; } = Array.AsReadOnly(new[]
    {
        new ToolbarCommandDefinition("navigation.back", "Back", "Navigate back")
      , new("navigation.forward", "Forward", "Navigate forward")
      , new("document.new", "New document", "New document toolbar")
      , new("document.save", "Save", "Save document toolbar")
      , new("view.reading", "Reading mode", "Toggle reading mode")
      , new("help.open", "Help", "Offline User Guide")
      , new("application.settings", "Settings", "Application settings")
      , new("editor.insertImage", "Insert image", "Insert workspace image reference")
      , new("document.enroll", "Make Axiom document", "Make Axiom Document")
      , new("document.pin", "Pin document", "Pin current document")
      , new("view.scrollSync", "Synchronized scrolling", "Toggle synchronized editor and preview scrolling")
      , new("view.previewAtCaret", "Preview at caret", "Show preview at editor caret")
    });

    public static string[] Read(string commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        if (commands.Length > 1000) throw new ArgumentException("Toolbar configuration is too long.");
        var ids = commands.Length == 0 ? [] : commands.Split(',');
        if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Length || ids.Any(id => !Commands.Any(command => command.Id == id)))
            throw new ArgumentException("Toolbar commands must be supported unique command IDs.");
        return ids;
    }
}
