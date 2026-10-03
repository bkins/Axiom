using Markdig.Syntax;

namespace Axiom.Core;

public sealed partial class MarkdownEditService
{
    private static MarkdownEditResult ApplyHardBreak(string text, int start, int length)
    {
        var structure = new MarkdownStructureService().Parse(text);
        var inCode = structure.Syntax.Descendants<CodeBlock>().Any(block => start >= block.Span.Start && start <= block.Span.End);
        var inserted = (inCode ? "" : "\\") + DetectNewline(text);
        return Replace(text, start, length, inserted, start + inserted.Length, 0);
    }
}
