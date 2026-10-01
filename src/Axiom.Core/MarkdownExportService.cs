using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Renderers.Html;

namespace Axiom.Core;

public sealed class MarkdownExportService
{
    public ExportDocument Parse(ExportSnapshot snapshot)
    {
        var structure = new MarkdownStructureService().Parse(snapshot.Markdown);
        var warnings = new List<string>();
        return new ExportDocument(structure.Syntax.Select(block => ConvertBlock(block, warnings)).ToArray(), warnings);
    }

    public static bool IsSafeLink(string destination)
    {
        if (string.IsNullOrWhiteSpace(destination) || destination.Any(char.IsControl)) return false;
        if (destination.StartsWith("//", StringComparison.Ordinal) || destination.Contains('\\')) return false;
        return !Uri.TryCreate(destination, UriKind.Absolute, out var uri) || uri.Scheme is "https" or "http" or "mailto";
    }

    private static ExportBlock ConvertBlock(Block block, List<string> warnings)
    {
        var children = block is ContainerBlock container
            ? container.Select(child => ConvertBlock(child, warnings)).ToArray() : [];
        var runs = block is LeafBlock leaf ? ReadRuns(leaf.Inline).ToArray() : [];
        return block switch
        {
            HeadingBlock heading => new("heading", runs, children, heading.Level, Fragment: heading.GetAttributes().Id),
            ParagraphBlock => new("paragraph", runs, children),
            CodeBlock code => new("code", [new ExportRun(code.Lines.ToString(), Code: true)], []),
            QuoteBlock => new("quote", [], children),
            ListBlock list => new("list", [], children, Ordered: list.IsOrdered, Start: int.TryParse(list.OrderedStart, out var start) ? start : 1),
            ListItemBlock => new("item", [], children),
            Table => new("table", [], children),
            TableRow row => new("row", [], children, Level: row.IsHeader ? 1 : 0),
            TableCell => new("cell", [], children),
            ThematicBreakBlock => new("paragraph", [new ExportRun("────────")], []),
            LinkReferenceDefinitionGroup => new("definitions", [], []),
            _ => Unsupported(block, runs, children, warnings)
        };
    }

    private static ExportBlock Unsupported(Block block, ExportRun[] runs, ExportBlock[] children, List<string> warnings)
    {
        warnings.Add($"Unsupported Markdown block: {block.GetType().Name}.");
        return new("paragraph", runs, children);
    }

    private static IEnumerable<ExportRun> ReadRuns(ContainerInline? container, bool bold = false, bool italic = false, string? link = null)
    {
        if (container is null) yield break;
        foreach (var inline in container)
        {
            switch (inline)
            {
                case LiteralInline literal: yield return new(literal.Content.ToString(), bold, italic, Link: link); break;
                case HtmlEntityInline entity: yield return new(entity.Transcoded.ToString(), bold, italic, Link: link); break;
                case CodeInline code: yield return new(code.Content, bold, italic, true, link); break;
                case LineBreakInline: yield return new("\n", bold, italic, Link: link); break;
                case TaskList task: yield return new(task.Checked ? "☑" : "☐", bold, italic); break;
                case LinkInline image when image.IsImage:
                    yield return new(string.Concat(ReadRuns(image).Select(run => run.Text)), Image: image.Url); break;
                case LinkInline hyperlink:
                    foreach (var run in ReadRuns(hyperlink, bold, italic, hyperlink.Url)) yield return run;
                    break;
                case EmphasisInline emphasis:
                    foreach (var run in ReadRuns(emphasis, bold || emphasis.DelimiterCount >= 2, italic || emphasis.DelimiterCount == 1, link)) yield return run;
                    break;
                case ContainerInline nested:
                    foreach (var run in ReadRuns(nested, bold, italic, link)) yield return run;
                    break;
                case AutolinkInline auto: yield return new(auto.Url, bold, italic, Link: auto.IsEmail ? "mailto:" + auto.Url : auto.Url); break;
            }
        }
    }
}
