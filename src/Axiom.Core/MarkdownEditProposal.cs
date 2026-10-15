using System.Text.RegularExpressions;

namespace Axiom.Core;

public static class MarkdownEditProposal
{
    public static string Parse(string source, string response)
    {
        if (response.Length > 200_000) throw new InvalidOperationException("The edit proposal is too large. Source is unchanged.");
        var match = Regex.Match(response, @"\A\s*(?<fence>`{3,}|~{3,})(?:markdown|md)[ \t]*\r?\n(?<text>[\s\S]*?)\r?\n\k<fence>\s*\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
        if (!match.Success || string.IsNullOrWhiteSpace(match.Groups["text"].Value)) throw new InvalidOperationException("CP did not return a complete Markdown proposal. Source is unchanged; refine the edit request and retry.");
        var proposal = match.Groups["text"].Value;
        if (DocumentIdentityPolicy.FindCanonical(source) != DocumentIdentityPolicy.FindCanonical(proposal)) throw new InvalidOperationException("The proposal changes the document identity. It cannot be applied.");
        return proposal;
    }
}
