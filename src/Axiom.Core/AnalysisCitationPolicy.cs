using System.Text;
using System.Text.RegularExpressions;

namespace Axiom.Core;

public static partial class AnalysisCitationPolicy
{
    public static IReadOnlyList<SemanticSection> Resolve(AnalysisInsight insight)
    {
        var sections = new List<SemanticSection>();
        var validSources = insight.Sources.Where(source => source.Matches(source.Text)).ToArray();
        var lineStarts = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        foreach (var citation in Citation().Matches(insight.Text).Cast<Match>().Take(256))
        {
            var source = validSources.FirstOrDefault(candidate => candidate.ReferenceKey == citation.Groups["key"].Value);
            if (source is null
             || !int.TryParse(citation.Groups["first"].Value, out var first)
             || !int.TryParse(citation.Groups["last"].Success ? citation.Groups["last"].Value : citation.Groups["first"].Value, out var last)
             || first < 1 || last < first) continue;
            if (!lineStarts.TryGetValue(source.ReferenceKey, out var starts))
            {
                starts = [0];
                for (var offset = 0; offset < source.Text.Length; offset++)
                    if (source.Text[offset] == '\n') starts.Add(offset + 1);
                lineStarts[source.ReferenceKey] = starts;
            }
            if (last > starts.Count) continue;
            var start = starts[first - 1];
            var end = last < starts.Count ? starts[last] : source.Text.Length;
            sections.Add(new(source.ReferenceKey, source.SourceHash, start, end - start, first, $"Cited lines {first}–{last}",
                             AnalysisSnapshot.Capture("citation-" + sections.Count, source.Text[start..end])));
        }
        return sections.DistinctBy(section => (section.DocumentKey, section.Offset, section.Length)).ToArray();
    }

    public static string Report(AnalysisInsight insight)
    {
        var output = new StringBuilder();
        output.AppendLine("DERIVED ANALYSIS — claims require human review; source is unchanged.");
        output.AppendLine($"Provider: {insight.Provider}; Model: {insight.Model ?? "not attested"}; Provenance: {insight.Provenance}; Created: {insight.CreatedAt:u}");
        output.AppendLine($"Resolved citation ranges: {Resolve(insight).Count}. Range validation verifies source locations, not whether a claim is true. Unresolved citation text is unverified.");
        foreach (var source in insight.Sources)
            output.AppendLine($"SOURCE {source.ReferenceKey} | SHA-256 {source.SourceHash}");
        output.AppendLine().Append(insight.Text);
        return output.ToString();
    }

    [GeneratedRegex(@"\[\[(?<key>[^\]\r\n|]{1,128})\|L(?<first>\d{1,7})(?:-L(?<last>\d{1,7}))?\]\]", RegexOptions.CultureInvariant)]
    private static partial Regex Citation();
}
