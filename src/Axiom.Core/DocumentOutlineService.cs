using System.Text;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Axiom.Core;

public sealed record DocumentOutlineEntry(int Level, string Text, int Offset, int LineNumber);

public sealed class DocumentOutlineService
{
    private readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder().Build();

    public IReadOnlyList<DocumentOutlineEntry> Parse(string markdown)
    {
        if (string.IsNullOrEmpty(markdown)) return [];
        var document = Markdown.Parse(markdown, _pipeline);
        return document.Descendants<HeadingBlock>()
            .Select(heading => new DocumentOutlineEntry(
                heading.Level,
                ReadText(heading.Inline),
                heading.Span.Start,
                heading.Line + 1))
            .ToArray();
    }

    private static string ReadText(ContainerInline? container)
    {
        if (container is null) return string.Empty;
        var text = new StringBuilder();
        AppendChildren(container, text);
        return text.ToString().Trim();
    }

    private static void AppendChildren(ContainerInline container, StringBuilder text)
    {
        for (var current = container.FirstChild; current is not null; current = current.NextSibling)
        {
            switch (current)
            {
                case LiteralInline literal:
                    text.Append(literal.Content);
                    break;
                case CodeInline code:
                    text.Append(code.Content);
                    break;
                case LineBreakInline:
                    text.Append(' ');
                    break;
                case ContainerInline nested:
                    AppendChildren(nested, text);
                    break;
            }
        }
    }
}
