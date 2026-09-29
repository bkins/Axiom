using Axiom.Core;

namespace Axiom.App;

internal static class OfflineHelp
{
    public static string Read()
    {
        using var stream = typeof(OfflineHelp).Assembly.GetManifestResourceStream("Axiom.App.Help.UserGuide.md")
            ?? throw new InvalidOperationException("The generated offline User Guide resource is missing.");
        using var reader = new StreamReader(stream);
        var text = reader.ReadToEnd();
        return text.StartsWith("<!-- Generated", StringComparison.Ordinal) ? text[(text.IndexOf("\n\n", StringComparison.Ordinal) + 2)..] : text;
    }

    public static DocumentHeading? FindTopic(DocumentStructure guide, string? context) => string.IsNullOrWhiteSpace(context) ? guide.Headings.FirstOrDefault() :
        guide.Headings.FirstOrDefault(heading => heading.Text.Contains(context, StringComparison.OrdinalIgnoreCase)) ?? guide.Headings.FirstOrDefault();
}
