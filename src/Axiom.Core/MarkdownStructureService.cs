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
    private static readonly MarkdownPipeline Pipeline = CreatePipeline();

    private static MarkdownPipeline CreatePipeline()
    {
        var builder = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseTaskLists()
        .UseEmphasisExtras(Markdig.Extensions.EmphasisExtras.EmphasisExtraOptions.Strikethrough)
        .UseAlertBlocks((renderer, kind) => CalloutTitle.Write(renderer, kind.ToString(), kind.ToString()))
        .UsePreciseSourceLocation()
        .UseAutoIdentifiers(AutoIdentifierOptions.GitHub)
        .DisableHtml();
        builder.InlineParsers.Insert(0, new WikiLinkInlineParser());
        return builder.Build();
    }

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
        var nodes = new List<DocumentStructureNode>();
        var sectionEnds = Enumerable.Repeat(markdown.Length, headings.Length).ToArray();
        var openSections = new Stack<int>();
        for (var headingIndex = 0; headingIndex < headings.Length; headingIndex++)
        {
            while (openSections.TryPeek(out var previousIndex) && headings[previousIndex].Level >= headings[headingIndex].Level)
            {
                openSections.Pop();
                sectionEnds[previousIndex] = headings[headingIndex].Source.Offset;
            }
            openSections.Push(headingIndex);
        }
        for (var headingIndex = 0; headingIndex < headings.Length; headingIndex++)
        {
            var heading = headings[headingIndex];
            nodes.Add(new(heading.NodeId, DocumentStructureNodeKind.Heading, heading.Text, heading.Source, null));
            var end = sectionEnds[headingIndex];
            nodes.Add(new(Guid.NewGuid(), DocumentStructureNodeKind.Section, heading.Text,
                heading.Source with { Length = end - heading.Source.Offset }, heading.NodeId));
        }
        nodes.AddRange(links.Select(link => new DocumentStructureNode(link.NodeId, DocumentStructureNodeKind.Link, link.Text, link.Source, link.HeadingNodeId)));
        foreach (var block in syntax.Descendants())
        {
            var range = CreateRange(block.Span, lineStarts, markdown.Length);
            var parentHeading = FindSourceHeading(headings, range.Offset);
            if (block is FencedCodeBlock code)
                nodes.Add(new(Guid.NewGuid(), DocumentStructureNodeKind.Code, code.Lines.ToString(), range, parentHeading, Language: code.Info));
            else if (block is CodeBlock indented)
                nodes.Add(new(Guid.NewGuid(), DocumentStructureNodeKind.Code, indented.Lines.ToString(), range, parentHeading));
            else if (block is Markdig.Extensions.Tables.Table)
                nodes.Add(new(Guid.NewGuid(), DocumentStructureNodeKind.Table, markdown.Substring(range.Offset, range.Length), range, parentHeading));
            else if (block is ParagraphBlock paragraph && paragraph.Inline?.FirstChild is Markdig.Extensions.TaskLists.TaskList task)
                nodes.Add(new(Guid.NewGuid(), DocumentStructureNodeKind.Task, ReadText(paragraph.Inline), range, parentHeading, task.Checked));
        }
        var ordered = nodes.OrderBy(node => node.Source.Offset).ThenBy(node => node.Kind).ToArray();
        if (previous is not null)
        {
            var old = previous.Nodes.Where(node => node.Kind is not DocumentStructureNodeKind.Heading and not DocumentStructureNodeKind.Link).ToArray();
            var current = ordered.Where(node => node.Kind is not DocumentStructureNodeKind.Heading and not DocumentStructureNodeKind.Link).ToArray();
            var identities = DocumentNodeReconciler.Match(old, current, node => $"{node.Kind}:{node.Text}:{node.Language}", node => node.NodeId,
                (before, after) => before.Kind == after.Kind && before.ParentHeadingId == after.ParentHeadingId);
            var identityIndex = 0;
            for (var index = 0; index < ordered.Length; index++)
                if (ordered[index].Kind is not DocumentStructureNodeKind.Heading and not DocumentStructureNodeKind.Link)
                    ordered[index] = ordered[index] with { NodeId = identities[identityIndex++] };
        }
        return new DocumentStructure(markdown, syntax, headings, links, definitions, ordered);
    }

    internal static string RenderHtml(DocumentStructure document, string? linkToken = null, bool showLineNumbers = false, bool showNavigation = true)
    {
        using var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer);
        Pipeline.Setup(renderer);
        renderer.ObjectRenderers.Insert(0, new CalloutQuoteRenderer());
        renderer.ObjectRenderers.Insert(0, new CSharpCodeRenderer());
        if (linkToken is null) { renderer.Render(document.Syntax); return writer.ToString(); }
        renderer.ObjectRenderers.Remove(renderer.ObjectRenderers.FindExact<LinkInlineRenderer>()!);
        renderer.ObjectRenderers.Insert(0, new PreviewLinkRenderer(document, linkToken));
        foreach (var block in document.Syntax)
        {
            if (block is LinkReferenceDefinitionGroup || block.Span.Start < 0) continue;
            var start = block.Span.Start.ToString(System.Globalization.CultureInfo.InvariantCulture);
            renderer.Write("<div class=\"source-block\" data-source-start=\"").Write(start)
                .Write("\" data-source-line=\"").Write((block.Line + 1).ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Write("\" data-source-end=\"").Write(block.Span.End.ToString(System.Globalization.CultureInfo.InvariantCulture)).Write("\">");
            if (showNavigation)
            {
                renderer.Write("<a class=\"source-navigation\" title=\"Go to this block in the Markdown editor\" aria-label=\"Go to this block in the Markdown editor\" href=\"axiom-source://show/")
                    .Write(System.Net.WebUtility.HtmlEncode(linkToken)).Write("/").Write(start).Write("\">Go to editor");
                if (showLineNumbers) renderer.Write(" · Line ").Write((block.Line + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
                renderer.Write("</a>");
            }
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
