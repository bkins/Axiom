namespace Axiom.Core;

public sealed record EditorPreferences(string FontFamily = "Cascadia Mono, Consolas"
                                    , int FontSize = 15
                                    , bool WordWrap = true
                                    , int TabWidth = 4
                                    , MenuStyle MenuStyle = MenuStyle.Quiet)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(FontFamily) || FontFamily.Length > 200 || FontFamily.Any(char.IsControl))
            throw new ArgumentException("Choose a font name of 1–200 characters.");
        if (FontSize is < 8 or > 40) throw new ArgumentException("Font size must be 8–40.");
        if (!Enum.IsDefined(MenuStyle)) throw new ArgumentException("Choose a supported menu style.");
        if (TabWidth is < 1 or > 8) throw new ArgumentException("Tab width must be 1–8.");
    }
}
