using System.Text;
using Markdig;
using Markdig.Renderers;
using Markdig.Extensions.AutoIdentifiers;
using Markdig.Renderers.Html;
using Markdig.Renderers.Html.Inlines;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Axiom.Core;

public sealed class MarkdownStructureService
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseTaskLists()
        .UsePreciseSourceLocation()
        .UseAutoIdentifiers(AutoIdentifierOptions.GitHub)
        .DisableHtml()
        .Build();

    /// <summary>Supply a previous view only when reparsing the same logical document.</summary>
    public DocumentStructure Parse(string markdown, DocumentStructure? previous = null)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        var syntax = Markdown.Parse(MaskIdentityMarker(markdown), Pipeline);
        var lineStarts = BuildLineStarts(markdown);
        var headings = syntax.Descendants<HeadingBlock>()
                             .Select(heading => new DocumentHeading(Guid.NewGuid()
                                                                  , heading.Level
                                                                  , ReadText(heading.Inline)
                                                                  , heading.GetAttributes().Id ?? string.Empty
                                                                  , CreateRange(heading.Span, lineStarts, markdown.Length)))
                             .ToArray();
        if (previous is not null)
        {
            var identities = DocumentNodeReconciler.Match(previous.Headings, headings
                                                       , heading => $"{heading.Level}:{heading.Text}"
                                                       , heading => heading.NodeId
                                                       , (oldHeading, newHeading) => oldHeading.Level == newHeading.Level);
            for (var index = 0; index < headings.Length; index++) headings[index] = headings[index] with { NodeId = identities[index] };
        }

        var links = syntax.Descendants<LinkInline>()
                          .Where(link => !link.IsImage && !IsWithinImage(link))
                          .Select(link => new MarkdownDocumentLink(Guid.NewGuid()
                                                                 , ReadText(link)
                                                                 , link.Url ?? string.Empty
                                                                 , link.Reference?.Label
                                                                 , CreateRange(link.Span, lineStarts, markdown.Length)
                                                                 , FindSourceHeading(headings, link.Span.Start)))
                          .OrderBy(link => link.Source.Offset)
                          .ToArray();
        if (previous is not null)
        {
            var identities = DocumentNodeReconciler.Match(previous.Links, links
                                                       , link => $"{link.Text.Length}:{link.Text}{link.Destination.Length}:{link.Destination}:{link.ReferenceLabel}"
                                                       , link => link.NodeId);
            for (var index = 0; index < links.Length; index++) links[index] = links[index] with { NodeId = identities[index] };
        }

        var definitions = syntax.GetLinkReferenceDefinitions(false)?.Links.Values
                                .Select(definition => new MarkdownReferenceDefinition(definition.Label ?? string.Empty
                                                                                     , definition.Url ?? string.Empty
                                                                                     , CreateRange(definition.Span, lineStarts, markdown.Length)))
                                .OrderBy(definition => definition.Source.Offset)
                                .ToArray() ?? [];
        return new DocumentStructure(markdown, syntax, headings, links, definitions);
    }

    internal static string RenderHtml(DocumentStructure document, string? linkToken = null)
    {
        if (linkToken is null) return Markdown.ToHtml(document.Syntax, Pipeline);
        using var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer);
        Pipeline.Setup(renderer);
        renderer.ObjectRenderers.Remove(renderer.ObjectRenderers.FindExact<LinkInlineRenderer>()!);
        renderer.ObjectRenderers.Insert(0, new PreviewLinkRenderer(document, linkToken));
        foreach (var block in document.Syntax)
        {
            if (block is LinkReferenceDefinitionGroup || block.Span.Start < 0) continue;
            var start = block.Span.Start.ToString(System.Globalization.CultureInfo.InvariantCulture);
            renderer.Write("<div class=\"source-block\" data-source-start=\"").Write(start)
                .Write("\" data-source-line=\"").Write((block.Line + 1).ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Write("\" data-source-end=\"").Write(block.Span.End.ToString(System.Globalization.CultureInfo.InvariantCulture)).Write("\">");
            renderer.Write("<a class=\"source-navigation\" aria-label=\"Show this block in the editor\" href=\"axiom-source://show/")
                .Write(System.Net.WebUtility.HtmlEncode(linkToken)).Write("/").Write(start).Write("\">Source</a>");
            renderer.Render(block);
            renderer.Write("</div>");
        }
        return writer.ToString();
    }

    private static Guid? FindSourceHeading(DocumentHeading[] headings, int offset)
    {
        var lower = 0;
        var upper = headings.Length - 1;
        var found = -1;
        while (lower <= upper)
        {
            var middle = lower + (upper - lower) / 2;
            if (headings[middle].Source.Offset <= offset)
            {
                found = middle;
                lower = middle + 1;
            }
            else upper = middle - 1;
        }
        return found < 0 ? null : headings[found].NodeId;
    }

    private static string MaskIdentityMarker(string markdown)
    {
        var markerLength = markdown.Length - DocumentIdCodec.RemoveCanonicalMarkerForPreview(markdown).Length;
        if (markerLength == 0) return markdown;
        var characters = markdown.ToCharArray();
        for (var index = 0; index < markerLength; index++)
            if (characters[index] is not '\r' and not '\n') characters[index] = ' ';
        return new string(characters);
    }

    private static int[] BuildLineStarts(string markdown)
    {
        var starts = new List<int> { 0 };
        for (var index = 0; index < markdown.Length; index++)
        {
            if (markdown[index] == '\r')
            {
                if (index + 1 < markdown.Length && markdown[index + 1] == '\n') index++;
                starts.Add(index + 1);
            }
            else if (markdown[index] == '\n') starts.Add(index + 1);
        }
        return starts.ToArray();
    }

    private static DocumentSourceRange CreateRange(SourceSpan span, int[] lineStarts, int textLength)
    {
        var offset = Math.Clamp(span.Start, 0, textLength);
        var end = Math.Clamp(span.End + 1, offset, textLength);
        var lineIndex = Array.BinarySearch(lineStarts, offset);
        if (lineIndex < 0) lineIndex = ~lineIndex - 1;
        return new DocumentSourceRange(offset, end - offset, lineIndex + 1, offset - lineStarts[lineIndex] + 1);
    }

    private static bool IsWithinImage(Inline inline)
    {
        for (var parent = inline.Parent; parent is not null; parent = parent.Parent)
            if (parent is LinkInline { IsImage: true }) return true;
        return false;
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
