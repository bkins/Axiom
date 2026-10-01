using System.Text;
using Axiom.Core;

namespace Axiom.Infrastructure;

internal static class TextExportWriter
{
    public static byte[] Write(ExportDocument document, List<string> warnings)
    {
        var output = new StringBuilder();
        foreach (var block in document.Blocks) WriteBlock(output, block, string.Empty, warnings);
        return Encoding.UTF8.GetBytes(output.ToString().TrimEnd() + "\n");
    }

    private static void WriteBlock(StringBuilder output, ExportBlock block, string indent, List<string> warnings)
    {
        if (block.Kind == "definitions") return;
        if (block.Kind == "list")
        {
            var number = block.Start;
            foreach (var item in block.Children)
            {
                var marker = block.Ordered ? number++ + ". " : "- ";
                var content = new StringBuilder();
                foreach (var child in item.Children) WriteBlock(content, child, string.Empty, warnings);
                var lines = content.ToString().TrimEnd().Replace("\r\n", "\n").Split('\n');
                output.Append(indent).Append(marker).AppendLine(lines[0]);
                foreach (var line in lines.Skip(1)) output.Append(indent).Append(' ', marker.Length).AppendLine(line);
            }
            output.AppendLine();
            return;
        }
        if (block.Kind == "table")
        {
            warnings.Add("Plain-text tables use separators; column alignment is approximate.");
            foreach (var row in block.Children)
            {
                var cells = row.Children.Select(cell => RenderRuns(ExportDocumentTraversal.Flatten([cell]).SelectMany(part => part.Runs).ToArray(), warnings).Replace("\r\n", " ").Replace('\n', ' '));
                output.Append(indent).AppendLine(string.Join(" | ", cells));
            }
            output.AppendLine();
            return;
        }
        if (block.Kind == "quote")
        {
            foreach (var child in block.Children) WriteBlock(output, child, indent + "> ", warnings);
            return;
        }
        var text = RenderRuns(block.Runs, warnings);
        if (text.Length > 0)
        {
            foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
                output.Append(indent).Append(block.Kind == "code" ? "    " : string.Empty).AppendLine(line);
            output.AppendLine();
        }
        foreach (var child in block.Children) WriteBlock(output, child, indent, warnings);
    }

    private static string RenderRuns(IReadOnlyList<ExportRun> runs, List<string> warnings)
    {
        var text = new StringBuilder();
        for (var index = 0; index < runs.Count; index++)
        {
            var run = runs[index];
            var label = run.Text;
            var link = run.Link;
            while (link is not null && index + 1 < runs.Count && runs[index + 1].Link == link)
                label += runs[++index].Text;
            text.Append(label);
            if (link is null) continue;
            if (!MarkdownExportService.IsSafeLink(link)) warnings.Add("Unsafe link omitted: " + link);
            else if (label != link) text.Append(" (").Append(link).Append(')');
        }
        return text.ToString();
    }
}
