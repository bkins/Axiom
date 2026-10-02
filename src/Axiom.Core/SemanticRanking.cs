namespace Axiom.Core;

public static class SemanticRanking
{
    public static IReadOnlyList<SemanticSection> Sections(IReadOnlyList<AnalysisSnapshot> documents)
    {
        if (documents.Count is < 1 or > 16 || documents.Sum(document => (long)document.Text.Length) > 200_000
         || documents.Any(document => !document.Matches(document.Text)))
            throw new ArgumentException("Review a bounded set of 1–16 valid source snapshots before indexing.");
        var result = new List<SemanticSection>();
        var parser = new MarkdownStructureService();
        foreach (var document in documents)
        {
            var structure = parser.Parse(document.Text);
            var starts = new[] { 0 }.Concat(structure.Headings.Select(heading => heading.Source.Offset)).Distinct().Order().ToArray();
            for (var index = 0; index < starts.Length; index++)
            {
                var start = starts[index];
                var end = index + 1 < starts.Length ? starts[index + 1] : document.Text.Length;
                for (var offset = start; offset < end;)
                {
                    var length = Math.Min(8000, end - offset);
                    if (offset + length < document.Text.Length && char.IsHighSurrogate(document.Text[offset + length - 1])) length--;
                    if (length == 0) length = 1;
                    var text = document.Text.Substring(offset, length);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        var heading = structure.Headings.LastOrDefault(candidate => candidate.Source.Offset <= offset)?.Text ?? "Introduction";
                        var line = document.Text.AsSpan(0, offset).Count('\n') + 1;
                        result.Add(new(document.ReferenceKey, document.SourceHash, offset, length, line, heading
                                     , AnalysisSnapshot.Capture("section-" + result.Count, text)));
                    }
                    offset += length;
                }
            }
        }
        if (result.Count > 256) throw new ArgumentException("Select a smaller document set (at most 256 source sections).");
        return result;
    }

    public static double Cosine(float[] left, float[] right)
    {
        if (left.Length == 0 || left.Length != right.Length || left.Any(value => !float.IsFinite(value)) || right.Any(value => !float.IsFinite(value)))
            throw new ArgumentException("Semantic vectors must have matching, finite dimensions.");
        double dot = 0, leftLength = 0, rightLength = 0;
        for (var index = 0; index < left.Length; index++)
        {
            dot += (double)left[index] * right[index];
            leftLength += (double)left[index] * left[index];
            rightLength += (double)right[index] * right[index];
        }
        return leftLength == 0 || rightLength == 0 ? 0 : Math.Clamp(dot / Math.Sqrt(leftLength * rightLength), -1, 1);
    }

    public static IReadOnlyList<SemanticMatch> Rank(IReadOnlyList<SemanticSection> sections, float[][] vectors, float[] query, string? excludedDocument = null)
    {
        if (sections.Count != vectors.Length) throw new ArgumentException("Source sections and vectors must match.");
        return sections.Select((section, index) => new SemanticMatch(section, Cosine(vectors[index], query)))
                       .Where(match => match.Section.DocumentKey != excludedDocument)
                       .OrderByDescending(match => match.Similarity).ThenBy(match => match.Section.DocumentKey, StringComparer.Ordinal)
                       .ThenBy(match => match.Section.Offset).Take(30).ToArray();
    }
}
