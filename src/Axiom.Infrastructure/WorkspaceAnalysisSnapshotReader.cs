using Axiom.Core;

namespace Axiom.Infrastructure;

/// <summary>Captures explicitly chosen saved files without opening, saving or enrolling documents.</summary>
public sealed class WorkspaceAnalysisSnapshotReader(IDocumentFileStore files)
{
    public async Task<AnalysisSnapshot> ReadAsync(string root, string path, CancellationToken cancellationToken = default)
    {
        if (!WorkspacePathBoundary.Contains(root, path) || !(Path.GetExtension(path).Equals(".md", StringComparison.OrdinalIgnoreCase)
                                                       || Path.GetExtension(path).Equals(".markdown", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Choose Markdown inside the active workspace.");
        if (new FileInfo(path).Length > 1_048_576)
            throw new InvalidOperationException("Saved analysis documents allow at most 1 MiB each.");
        var result = await new MarkdownLinkTargetReader(files).ReadAsync(root, path, cancellationToken);
        if (result.Status != MarkdownLinkResolutionStatus.Resolved || string.IsNullOrWhiteSpace(result.Text) || result.Text.Length > 200_000)
            throw new InvalidOperationException("Saved source is unavailable, empty or exceeds 200,000 characters.");
        var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
        var suffix = WorkspaceLinkIndexService.Hash(Path.GetFullPath(path))[..12];
        var label = new string(relative.Select(character => character is '|' or '[' or ']' || char.IsControl(character) ? '_' : character).ToArray());
        if (label.Length > 90) label = label[..90];
        return AnalysisSnapshot.Capture("saved:" + label + ":" + suffix, result.Text);
    }
}
