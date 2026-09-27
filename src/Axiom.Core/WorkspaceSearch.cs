namespace Axiom.Core;

public sealed record WorkspaceSearchResult(
    string FullPath,
    string RelativePath,
    int LineNumber,
    int ColumnNumber,
    int MatchOffset,
    string MatchedText,
    string Excerpt);

public sealed record WorkspaceSearchResponse(
    IReadOnlyList<WorkspaceSearchResult> Results,
    IReadOnlyList<string> SkippedPaths);

public sealed class WorkspaceSearchService(IDocumentFileStore files)
{
    public async Task<WorkspaceSearchResponse> SearchAsync(
        string workspaceRoot,
        string query,
        CancellationToken cancellationToken = default)
    {
        var root = WorkspaceService.NormalizeRootPath(workspaceRoot);
        if (string.IsNullOrWhiteSpace(query)) return new WorkspaceSearchResponse([], []);

        var discovery = await files.ListMarkdownFilesAsync(root, cancellationToken);
        var results = new List<WorkspaceSearchResult>();
        var skipped = new List<string>(discovery.SkippedDirectories);
        foreach (var path in discovery.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string text;
            try
            {
                text = (await files.ReadAsync(path, cancellationToken)).Text;
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
            {
                skipped.Add(path);
                continue;
            }
            FindMatches(root, path, text, query, results, cancellationToken);
        }

        return new WorkspaceSearchResponse(results, skipped.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static void FindMatches(
        string root,
        string path,
        string text,
        string query,
        ICollection<WorkspaceSearchResult> results,
        CancellationToken cancellationToken)
    {
        var lineStart = 0;
        var lineNumber = 1;
        while (lineStart <= text.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var newline = text.IndexOf('\n', lineStart);
            var lineEnd = newline < 0 ? text.Length : newline;
            if (lineEnd > lineStart && text[lineEnd - 1] == '\r') lineEnd--;
            var line = text.AsSpan(lineStart, lineEnd - lineStart);
            var searchStart = 0;
            while (searchStart <= line.Length - query.Length)
            {
                var match = line[searchStart..].IndexOf(query.AsSpan(), StringComparison.OrdinalIgnoreCase);
                if (match < 0) break;
                var column = searchStart + match;
                results.Add(new WorkspaceSearchResult(
                    path,
                    Path.GetRelativePath(root, path),
                    lineNumber,
                    column + 1,
                    lineStart + column,
                    line.Slice(column, query.Length).ToString(),
                    CreateExcerpt(line.ToString())));
                searchStart = column + Math.Max(query.Length, 1);
            }
            if (newline < 0) break;
            lineStart = newline + 1;
            lineNumber++;
        }
    }

    private static string CreateExcerpt(string line)
    {
        var excerpt = line.Trim();
        return excerpt.Length <= 180 ? excerpt : excerpt[..177] + "...";
    }
}
