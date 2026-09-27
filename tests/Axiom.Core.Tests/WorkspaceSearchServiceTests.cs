using Axiom.Core;

namespace Axiom.Core.Tests;

public sealed class WorkspaceSearchServiceTests
{
    [Fact]
    public async Task SearchFindsLiteralCaseInsensitiveMatchesWithLocations()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "AxiomSearch", Guid.NewGuid().ToString("N")));
        var path = Path.Combine(root, "nested", "notes.md");
        var files = new SearchFiles(new Dictionary<string, string>
        {
            [path] = "First line\r\nNeedle and needle again\nLast line"
        });

        var response = await new WorkspaceSearchService(files).SearchAsync(root, "NEEDLE");

        Assert.Equal(2, response.Results.Count);
        Assert.All(response.Results, result => Assert.Equal(Path.Combine("nested", "notes.md"), result.RelativePath));
        Assert.Equal([2, 2], response.Results.Select(result => result.LineNumber));
        Assert.Equal([1, 12], response.Results.Select(result => result.ColumnNumber));
        Assert.Equal(["Needle", "needle"], response.Results.Select(result => result.MatchedText));
        Assert.Empty(response.SkippedPaths);
    }

    [Fact]
    public async Task SearchSkipsUnreadableFilesAndEmptyQueryDoesNoReads()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "AxiomSearch", Guid.NewGuid().ToString("N")));
        var readable = Path.Combine(root, "readable.md");
        var unreadable = Path.Combine(root, "unreadable.md");
        var files = new SearchFiles(new Dictionary<string, string> { [readable] = "find me", [unreadable] = "ignored" }, unreadable);
        var service = new WorkspaceSearchService(files);

        var response = await service.SearchAsync(root, "find");
        var empty = await service.SearchAsync(root, "  ");

        Assert.Single(response.Results);
        Assert.Equal(unreadable, Assert.Single(response.SkippedPaths));
        Assert.Empty(empty.Results);
        Assert.Empty(empty.SkippedPaths);
    }

    private sealed class SearchFiles(IReadOnlyDictionary<string, string> content, string? unreadable = null) : IDocumentFileStore
    {
        public Task<MarkdownDiscoveryResult> ListMarkdownFilesAsync(string folder, CancellationToken cancellationToken = default) =>
            Task.FromResult(new MarkdownDiscoveryResult(content.Keys.ToArray(), []));

        public Task<DocumentFile> ReadAsync(string path, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(path, unreadable, StringComparison.OrdinalIgnoreCase)) throw new IOException("unreadable");
            var text = content[path];
            return Task.FromResult(new DocumentFile(path, text, DateTimeOffset.UtcNow, text.Length));
        }

        public Task<DocumentFile> WriteAsync(string path, string text, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(content.ContainsKey(path));
    }
}
