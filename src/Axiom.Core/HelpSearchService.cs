namespace Axiom.Core;

public sealed class HelpSearchService
{
    public IReadOnlyList<HelpSearchResult> Search(string markdown, string query)
    {
        query = query.Trim();
        if (query.Length == 0) return [];
        var blocks = new MarkdownExportService().Parse(new(markdown, null)).Blocks;
        var topic = "User Guide";
        var fragment = string.Empty;
        var results = new List<HelpSearchResult>();
        foreach (var block in ExportDocumentTraversal.Flatten(blocks))
        {
            var text = string.Concat(block.Runs.Select(run => run.Text));
            if (block.Kind == "heading")
            {
                topic = text;
                fragment = block.Fragment ?? string.Empty;
            }
            var match = text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
            if (match < 0) continue;
            var start = Math.Max(0, match - 35);
            var excerpt = text.Substring(start, Math.Min(160, text.Length - start)).Replace('\n', ' ');
            results.Add(new(topic, fragment, excerpt));
        }
        return results;
    }

}
