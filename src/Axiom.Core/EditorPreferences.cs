namespace Axiom.Core;

public sealed record EditorPreferences(string FontFamily = "Cascadia Mono, Consolas"
                                    , int FontSize = 15
                                    , bool WordWrap = true
                                    , int TabWidth = 4
                                    , MenuStyle MenuStyle = MenuStyle.Quiet)
{
    public bool CompactTitleBar { get; init; } = true;
    public bool HighlightCurrentLine { get; init; } = true;
    public bool ShowNonPrintingCharacters { get; init; }
    public bool ShowPreviewLineNumbers { get; init; }
    public bool ShowPreviewNavigation { get; init; } = true;
    public bool SpellCheck { get; init; } = true;
    public string ToolbarCommands { get; init; } = ToolbarLayout.DefaultCommands;
    public bool CompactWorkbench { get; init; }
    public CommandPlacement CommandPlacement { get; init; }
    public DiscoveryPlacement DiscoveryPlacement { get; init; }
    public string PersonalDictionary { get; init; } = string.Empty;
    [System.Text.Json.Serialization.JsonIgnore]
    public string[] PersonalWords => PersonalDictionary.Split('\n', StringSplitOptions.RemoveEmptyEntries);
    public void Validate()
    {
        _ = ToolbarLayout.Read(ToolbarCommands);
        if (!Enum.IsDefined(CommandPlacement) || !Enum.IsDefined(DiscoveryPlacement)) throw new ArgumentException("Choose a supported command and discovery placement.");
        if (PersonalDictionary is null || PersonalDictionary.Length > 81000 || PersonalWords.Length > 1000 || PersonalWords.Any(word => word.Length is < 1 or > 80 || word.Any(character => !char.IsLetter(character) && character != '\'' && character != '’')))
            throw new ArgumentException("Personal dictionary allows up to 1,000 words of 1–80 letters or apostrophes.");
        if (string.IsNullOrWhiteSpace(FontFamily) || FontFamily.Length > 200 || FontFamily.Any(char.IsControl))
            throw new ArgumentException("Choose a font name of 1–200 characters.");
        if (FontSize is < 8 or > 40) throw new ArgumentException("Font size must be 8–40.");
        if (!Enum.IsDefined(MenuStyle)) throw new ArgumentException("Choose a supported menu style.");
        if (TabWidth is < 1 or > 8) throw new ArgumentException("Tab width must be 1–8.");
    }
}
