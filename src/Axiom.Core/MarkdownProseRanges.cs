using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Axiom.Core;

public static class MarkdownProseRanges
{
    public static IReadOnlyList<(int Offset, int Length)> Read(string markdown)
    {
        var syntax = new MarkdownStructureService().Parse(markdown).Syntax;
        var ranges = new List<(int Offset, int Length)>();
        foreach (var leaf in syntax.Descendants<LeafBlock>().Where(block => block is not CodeBlock))
        {
            if (leaf.Inline is null) continue;
            foreach (var literal in leaf.Inline.Descendants<LiteralInline>())
            {
                var start = literal.Span.Start;
                var length = literal.Span.End - start + 1;
                if (start >= 0 && length > 0 && start + length <= markdown.Length)
                {
                    ranges.Add((start, length));
                }
            }
        }
        return ranges;
    }
}
