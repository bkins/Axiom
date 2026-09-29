using Axiom.Core;

namespace Axiom.Infrastructure;

public sealed class ClassificationService(IDocumentFileStore files, IDocumentMetadataStore identities, IClassificationStore store)
{
    public async Task<ClassificationCatalog> DiscoverAsync(string root, CancellationToken cancellationToken = default)
    {
        root = WorkspaceService.NormalizeRootPath(root);
        var discovery = await files.ListMarkdownFilesAsync(root, cancellationToken);
        var discoveredPaths = discovery.Files.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var errors = discovery.SkippedDirectories.Select(path => $"Skipped: {path}").ToList();
        var reader = new MarkdownLinkTargetReader(files);
        var saved = new Dictionary<string, (Guid? Id, string Hash)>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in discovery.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = await reader.ReadAsync(root, path, cancellationToken);
            if (file.Status != MarkdownLinkResolutionStatus.Resolved) { errors.Add($"{path}: {file.Status}"); continue; }
            saved[path] = (DocumentIdCodec.Find(file.Text!), WorkspaceLinkIndexService.Hash(file.Text!));
        }
        // Verify known accessible occurrences outside this collection too. Missing observations remain stored.
        foreach (var occurrence in await identities.GetIdentitiesAsync(cancellationToken))
        {
            if (saved.ContainsKey(occurrence.Path)) continue;
            var file = await reader.ReadAsync(Path.GetDirectoryName(occurrence.Path)!, occurrence.Path, cancellationToken);
            if (file.Status == MarkdownLinkResolutionStatus.Resolved && DocumentIdCodec.Find(file.Text!) is { } id)
                saved[occurrence.Path] = (id, WorkspaceLinkIndexService.Hash(file.Text!));
            else if (file.Status is not MarkdownLinkResolutionStatus.Resolved and not MarkdownLinkResolutionStatus.MissingDocument)
                errors.Add($"Known identity unavailable: {occurrence.Path}");
        }
        var counts = saved.Values.Where(value => value.Id is not null).GroupBy(value => value.Id!.Value).ToDictionary(group => group.Key, group => group.Count());
        var values = (await store.GetClassificationsAsync(cancellationToken)).ToDictionary(value => value.DocumentId);
        var documents = saved.Where(pair => discoveredPaths.Contains(pair.Key)).Select(pair =>
        {
            var id = pair.Value.Id;
            var status = id is null ? "Ordinary or invalid identity" : counts[id.Value] > 1 ? "Ambiguous identity" : errors.Count > 0 ? "Discovery incomplete; editing disabled" : "Unique saved identity";
            return new ClassifiedDocument(Path.GetRelativePath(root, pair.Key), id, pair.Value.Hash, status,
                id is { } key ? values.GetValueOrDefault(key) ?? DocumentClassification.Empty(key) : null);
        }).OrderBy(document => document.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray();
        return new(root, documents, errors);
    }

    public async Task ApplyAsync(ClassificationCatalog evidence, IReadOnlyList<ClassificationChange> changes, CancellationToken cancellationToken = default)
    {
        var current = await DiscoverAsync(evidence.Root, cancellationToken);
        foreach (var change in changes)
        {
            var before = evidence.Find(change.Value.DocumentId);
            var now = current.Find(change.Value.DocumentId);
            if (before is null || now is null || before.RelativePath != now.RelativePath || before.ContentHash != now.ContentHash)
                throw new InvalidOperationException("Document identity or file evidence changed. Refresh the proposal.");
        }
        await store.ApplyClassificationsAsync(changes, cancellationToken);
    }
}
