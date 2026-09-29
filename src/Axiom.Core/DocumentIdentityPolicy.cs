using System.Text.RegularExpressions;

namespace Axiom.Core;

public static partial class DocumentIdentityPolicy
{
    // Identity-bound mutations require the entire first line to be a marker.
    // The tolerant editor/preview codec remains compatible with existing Markdown.
    [GeneratedRegex(@"\A\uFEFF?<!--[ \t]*axiom:documentId=(?<id>[0-9a-fA-F-]{36})[ \t]*-->[ \t]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex CanonicalLine();

    public static Guid? FindCanonical(string text)
    {
        var newline = text.IndexOf('\n');
        var firstLine = (newline < 0 ? text : text[..newline]).TrimEnd('\r');
        var match = CanonicalLine().Match(firstLine);
        return match.Success && Guid.TryParse(match.Groups["id"].Value, out var id) && id != Guid.Empty ? id : null;
    }
}
