using Axiom.Core;

namespace Axiom.Infrastructure;

public sealed class BulkEnrollmentService(WorkspaceFileOperations operations)
{
    public async Task<IReadOnlyList<BulkEnrollmentEntry>> PreviewAsync(WorkspaceRecord workspace
                                                                    , IProgress<string>? progress = null
                                                                    , CancellationToken cancellationToken = default)
    {
        var entries = new List<BulkEnrollmentEntry>();
        var discovery = await new FileDocumentStore().ListMarkdownFilesAsync(workspace.RootPath, cancellationToken);
        foreach (var path in discovery.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(workspace.RootPath, path);
            try
            {
                WorkspaceMutationPolicy.Resolve(workspace.RootPath, relative);
                WorkspaceFileOperations.ValidateAncestors(workspace.RootPath, path);
                var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
                var text = MarkdownIdentityBytes.Decode(bytes);
                var id = DocumentIdCodec.Find(text);
                if (id is null) MarkdownIdentityBytes.Assign(bytes, Guid.NewGuid(), false);
                entries.Add(new BulkEnrollmentEntry(relative, id is null ? "Ordinary — ready to enroll" : "Already enrolled — skip", WorkspaceFileOperations.Hash(bytes), id, workspace.WorkspaceId));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                entries.Add(new BulkEnrollmentEntry(relative, "Unavailable/unsupported: " + exception.Message, null, null, workspace.WorkspaceId));
            }
            progress?.Report($"Reviewed {entries.Count}: {relative}");
        }
        var duplicates = entries.Where(entry => entry.Identity is not null).GroupBy(entry => entry.Identity).Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet();
        var incomplete = discovery.SkippedDirectories.Count > 0 || entries.Any(entry => entry.ReviewedHash is null);
        return entries.Select(entry => duplicates.Contains(entry.Identity) && entry.Identity is not null
            ? entry with { Status = "Duplicate ID — resolve separately" }
            : incomplete && entry.IsEligible ? entry with { Status = "Blocked — workspace scan incomplete" } : entry).ToArray();
    }

    public async Task<IReadOnlyList<BulkEnrollmentResult>> ApplyAsync(WorkspaceRecord workspace
                                                                   , IReadOnlyList<BulkEnrollmentEntry> selection
                                                                   , IProgress<string>? progress = null
                                                                   , CancellationToken cancellationToken = default)
    {
        var results = new List<BulkEnrollmentResult>();
        IReadOnlyList<BulkEnrollmentEntry> current;
        try { current = await PreviewAsync(workspace, progress, cancellationToken); }
        catch (OperationCanceledException) { return selection.Select(entry => new BulkEnrollmentResult(entry.RelativePath, "Unprocessed — cancelled")).ToArray(); }
        foreach (var entry in selection)
        {
            if (entry.WorkspaceId != workspace.WorkspaceId)
            {
                results.Add(new BulkEnrollmentResult(entry.RelativePath, "Workspace changed — refresh required"));
                continue;
            }
            if (cancellationToken.IsCancellationRequested)
            {
                results.Add(new BulkEnrollmentResult(entry.RelativePath, "Unprocessed — cancelled"));
                continue;
            }
            var latest = current.FirstOrDefault(candidate => candidate.RelativePath == entry.RelativePath);
            if (latest is null || latest.ReviewedHash != entry.ReviewedHash)
            {
                results.Add(new BulkEnrollmentResult(entry.RelativePath, "Changed/missing — refresh required"));
                continue;
            }
            if (!latest.IsEligible)
            {
                results.Add(new BulkEnrollmentResult(entry.RelativePath, latest.Status));
                continue;
            }
            try
            {
                await operations.ExecuteAsync(workspace, DocumentMutationKind.Enroll, entry.RelativePath, entry.RelativePath, entry.ReviewedHash, cancellationToken);
                results.Add(new BulkEnrollmentResult(entry.RelativePath, "Enrolled"));
            }
            catch (OperationCanceledException) { results.Add(new BulkEnrollmentResult(entry.RelativePath, "Unprocessed — cancelled")); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                results.Add(new BulkEnrollmentResult(entry.RelativePath, "Failed/changed: " + exception.Message));
            }
            progress?.Report($"Processed {results.Count}/{selection.Count}: {entry.RelativePath}");
        }
        return results;
    }
}
