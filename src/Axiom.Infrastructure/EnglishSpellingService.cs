using System.Text.RegularExpressions;
using Axiom.Core;
using WeCantSpell.Hunspell;

namespace Axiom.Infrastructure;

public sealed partial class EnglishSpellingService
{
    private static readonly Lazy<WordList> Dictionary = new(() =>
    {
        var assembly = typeof(EnglishSpellingService).Assembly;
        using var words = assembly.GetManifestResourceStream("Axiom.Infrastructure.Assets.Spellcheck.en.dic")!;
        using var affixes = assembly.GetManifestResourceStream("Axiom.Infrastructure.Assets.Spellcheck.en.aff")!;
        return WordList.CreateFromStreams(words, affixes);
    });

    public bool Check(string word) => Dictionary.Value.Check(word);
    public IReadOnlyList<string> Suggest(string word) => Dictionary.Value.Suggest(word).Take(6).ToArray();

    public IReadOnlyList<SpellingIssue> FindIssues(string markdown, IEnumerable<string> ignoredWords, CancellationToken cancellationToken = default)
    {
        if (markdown.Length > 1_000_000) return [];
        var ignored = ignoredWords.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var issues = new List<SpellingIssue>();
        foreach (var range in MarkdownProseRanges.Read(markdown))
        {
            var prose = markdown.Substring(range.Offset, range.Length);
            var tokens = IgnoredTokenPattern().Matches(prose).Cast<Match>().ToArray();
            foreach (Match match in WordPattern().Matches(prose))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (tokens.Any(token => match.Index >= token.Index && match.Index < token.Index + token.Length)) continue;
                var word = match.Value;
                if (word.Length is < 2 or > 80 || ignored.Contains(word) || Check(word)) continue;
                issues.Add(new(range.Offset + match.Index, word.Length, word));
                if (issues.Count == 1000) return issues;
            }
        }
        return issues;
    }

    [GeneratedRegex(@"\b\p{L}+(?:['’]\p{L}+)*\b", RegexOptions.CultureInvariant)]
    private static partial Regex WordPattern();
    [GeneratedRegex(@"\b(?:[A-Za-z][A-Za-z0-9+.-]*://|www\.)\S+|\b[\w.+-]+@[\w.-]+\.\w+\b", RegexOptions.CultureInvariant)]
    private static partial Regex IgnoredTokenPattern();
}
