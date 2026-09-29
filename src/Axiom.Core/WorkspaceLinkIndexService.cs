using System.Security.Cryptography;
using System.Text;

namespace Axiom.Core;

public sealed class WorkspaceLinkIndexService(IDocumentFileStore files, IMarkdownLinkTargetReader targets)
{
    private readonly MarkdownStructureService _parser = new();
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public async Task<WorkspaceBacklinkIndex> BuildAsync(string root, IProgress<WorkspaceIndexProgress>? progress = null,
        CancellationToken cancellationToken = default, WorkspaceBacklinkIndex? previous = null)
    {
        root = WorkspaceService.NormalizeRootPath(root);
        var discovery = await files.ListMarkdownFilesAsync(root, cancellationToken);
        var errors = discovery.SkippedDirectories.Select(path => $"Skipped directory: {path}").ToList();
        var documents = new Dictionary<string, DocumentStructure>(PathComparer);
        var targetStates = new Dictionary<string, MarkdownLinkResolutionStatus>(PathComparer);
        var processed = 0;
        foreach (var path in discovery.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = await targets.ReadAsync(root, path, cancellationToken);
            targetStates[path] = target.Status;
            if (target.Status == MarkdownLinkResolutionStatus.Resolved) documents[path] = _parser.Parse(target.Text ?? string.Empty);
            else errors.Add($"{Path.GetRelativePath(root, path)}: {target.Status}");
            if (++processed % 25 == 0 || processed == discovery.Files.Count) progress?.Report(new(processed, discovery.Files.Count));
        }
        var indexed = new List<WorkspaceIndexedDocument>();
        foreach (var document in documents.OrderBy(pair => pair.Key, PathComparer))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var references = new List<WorkspaceLinkReference>();
            foreach (var link in document.Value.Links)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = MarkdownLinkResolver.ResolveLocation(root, document.Key, link.Destination);
                if (result.TargetPath is { } targetPath && result.Status == MarkdownLinkResolutionStatus.Resolved)
                {
                    if (!targetStates.TryGetValue(targetPath, out var status))
                    {
                        status = (await targets.ReadAsync(root, targetPath, cancellationToken)).Status;
                        targetStates[targetPath] = status;
                    }
                    result = result with { Status = status };
                    if (status == MarkdownLinkResolutionStatus.Resolved && result.Fragment is { Length: > 0 } fragment
                        && documents.TryGetValue(targetPath, out var target) && target.FindHeading(fragment) is null)
                        result = result with { Status = MarkdownLinkResolutionStatus.MissingHeading };
                }
                references.Add(CreateReference(root, document.Key, document.Value, link, result));
            }
            indexed.Add(new(Path.GetRelativePath(root, document.Key), Hash(document.Value.SourceText), references));
        }
        foreach (var oldDocument in previous?.Documents ?? [])
        {
            cancellationToken.ThrowIfCancellationRequested();
            var oldPath = WorkspaceMutationPolicy.Resolve(root, oldDocument.RelativePath);
            var failedRead = targetStates.TryGetValue(oldPath, out var status) && status is not MarkdownLinkResolutionStatus.Resolved and not MarkdownLinkResolutionStatus.MissingDocument;
            var skippedDirectory = discovery.SkippedDirectories.Any(directory => oldPath.StartsWith(
                Path.TrimEndingDirectorySeparator(directory) + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
            if (failedRead || skippedDirectory) indexed.Add(oldDocument);
        }
        indexed.Sort((left, right) => PathComparer.Compare(left.RelativePath, right.RelativePath));
        return new(1, DateTimeOffset.UtcNow, indexed, errors);
    }

    public static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    public static WorkspaceLinkReference CreateReference(string root, string sourcePath, DocumentStructure structure,
        MarkdownDocumentLink link, MarkdownLinkResolution resolution, bool unsaved = false)
    {
        var heading = structure.Headings.FirstOrDefault(candidate => candidate.NodeId == link.HeadingNodeId)?.Text;
        return new(Path.GetRelativePath(root, sourcePath), resolution.TargetPath is { } target ? Path.GetRelativePath(root, target) : null,
            link.Destination, string.IsNullOrWhiteSpace(heading) ? link.Text : $"{heading} · {link.Text}", link.Source, resolution.Status, unsaved);
    }

    public static IReadOnlyList<WorkspaceLinkReference> Incoming(string targetRelativePath, WorkspaceBacklinkIndex? saved,
        IReadOnlyDictionary<string, IReadOnlyList<WorkspaceLinkReference>> overlays)
    {
        var references = (saved?.Documents ?? []).Where(document => !overlays.ContainsKey(document.RelativePath))
            .SelectMany(document => document.References).Concat(overlays.Values.SelectMany(links => links));
        return references.Where(reference => reference.TargetRelativePath is { } target && PathComparer.Equals(target, targetRelativePath))
            .OrderBy(reference => reference.SourceRelativePath, PathComparer).ThenBy(reference => reference.Source.Offset).ToArray();
    }
}
