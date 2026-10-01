namespace Axiom.Core;

public sealed class WorkspaceWikiLinkService(IDocumentFileStore files, IMarkdownLinkTargetReader targets, IClassificationStore? metadata = null)
{
    public async Task<WikiLinkResolver> BuildAsync(string root, Func<string, DocumentStructure?>? openDocument = null, CancellationToken cancellationToken = default)
    {
        root = WorkspaceService.NormalizeRootPath(root);
        var discovery = await files.ListMarkdownFilesAsync(root, cancellationToken);
        var documents = new Dictionary<string, DocumentStructure>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var parser = new MarkdownStructureService();
        var complete = discovery.SkippedDirectories.Count == 0;
        foreach (var path in discovery.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = await targets.ReadAsync(root, path, cancellationToken);
            if (target.Status != MarkdownLinkResolutionStatus.Resolved) { complete = false; continue; }
            documents[path] = openDocument?.Invoke(path) ?? parser.Parse(target.Text ?? "");
        }
        var classifications = metadata is null ? null : await metadata.GetClassificationsAsync(cancellationToken);
        return new WikiLinkResolver(root, documents, classifications, complete);
    }
}
