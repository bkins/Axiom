using Axiom.Core;

namespace Axiom.Infrastructure;

public sealed class MarkdownLinkTargetReader(IDocumentFileStore files) : IMarkdownLinkTargetReader
{
    public async Task<MarkdownLinkTarget> ReadAsync(string workspaceRoot, string targetPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var root = WorkspaceService.NormalizeRootPath(workspaceRoot);
            var path = Path.GetFullPath(targetPath);
            var relative = WorkspaceService.GetRelativeDocumentPath(root, path);
            if (relative == ".") return new(MarkdownLinkResolutionStatus.ExcludedPath, Message: "A document path is required.");
            // Reject links at every level, including the workspace root and its ancestors.
            // Inspect attributes directly so inaccessible paths are not mistaken for missing files.
            for (var current = path; current is not null; current = Path.GetDirectoryName(current))
            {
                try
                {
                    if (File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
                        return new(MarkdownLinkResolutionStatus.ExcludedPath, Message: "Linked files or folders are excluded from document navigation.");
                }
                catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
                {
                    // Keep inspecting ancestors: a missing child beneath a linked folder is excluded.
                }
            }
            var file = await files.ReadAsync(path, cancellationToken);
            return new(MarkdownLinkResolutionStatus.Resolved, file.Text);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return new(MarkdownLinkResolutionStatus.MissingDocument, Message: "The linked Markdown file does not exist.");
        }
        catch (UnauthorizedAccessException)
        {
            return new(MarkdownLinkResolutionStatus.ExcludedPath, Message: "The linked document or an ancestor is inaccessible.");
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or NotSupportedException)
        {
            return new(MarkdownLinkResolutionStatus.Unavailable, Message: "The linked document could not be read safely.");
        }
    }
}
