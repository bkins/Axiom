using System.Text;
using Axiom.Core;

namespace Axiom.Infrastructure;

public sealed partial class GitWorkspaceService
{
    public Task StageSelectedAsync(GitRepositorySnapshot snapshot
                                 , IReadOnlyDictionary<string, string?> reviewedHashes
                                 , CancellationToken cancellationToken = default
                                 , Action? validateBeforePublish = null)
    {
        return ChangeSelectedStagingAsync(snapshot, reviewedHashes, true, cancellationToken, validateBeforePublish);
    }

    public Task UnstageSelectedAsync(GitRepositorySnapshot snapshot
                                   , IReadOnlyList<string> relativePaths
                                   , CancellationToken cancellationToken = default
                                   , Action? validateBeforePublish = null)
    {
        if (relativePaths.Distinct(StringComparer.Ordinal).Count() != relativePaths.Count)
            throw new ArgumentException("Select distinct staged documents.");
        return ChangeSelectedStagingAsync(snapshot, relativePaths.ToDictionary(path => path, _ => (string?)null), false, cancellationToken, validateBeforePublish);
    }

    private async Task ChangeSelectedStagingAsync(GitRepositorySnapshot snapshot
                                               , IReadOnlyDictionary<string, string?> reviewedHashes
                                               , bool stage
                                               , CancellationToken cancellationToken
                                               , Action? validateBeforePublish)
    {
        if (reviewedHashes.Count == 0) throw new ArgumentException("Select changed documents first.");
        await ValidateSnapshotAsync(snapshot, cancellationToken);
        foreach (var relative in reviewedHashes.Keys) { ValidateSelection(snapshot, relative); ManagedPath(snapshot, relative); }
        for (var ancestor = snapshot.IndexPath; ancestor is not null; ancestor = Path.GetDirectoryName(ancestor))
            if ((File.Exists(ancestor) || Directory.Exists(ancestor)) && File.GetAttributes(ancestor).HasFlag(FileAttributes.ReparsePoint))
                throw new IOException("Linked Git administration paths are unsupported.");

        var temporaryIndex = Path.Combine(Path.GetTempPath(), "AxiomStagingIndex-" + Guid.NewGuid().ToString("N"));
        var lockPath = snapshot.IndexPath + ".lock";
        // Own Git's conventional index lock until the complete reviewed batch is atomically published.
        await using var indexLock = new FileStream(lockPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Delete);
        var published = false;
        try
        {
            if (await IndexHashAsync(snapshot.IndexPath, cancellationToken) != snapshot.IndexHash)
                throw new IOException("The Git index changed after review.");
            if (File.Exists(snapshot.IndexPath)) File.Copy(snapshot.IndexPath, temporaryIndex);
            else await RequireAsync(snapshot.RepositoryRoot, new[] { "read-tree", "--empty" }, cancellationToken, indexPath: temporaryIndex);
            var zero = await ZeroHashAsync(snapshot.RepositoryRoot, cancellationToken);
            var updates = new StringBuilder();
            foreach (var relative in reviewedHashes.Keys)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (stage)
                {
                    var path = ManagedPath(snapshot, relative);
                    var bytes = File.Exists(path) ? await ReadWorkingAsync(path, cancellationToken) : null;
                    if ((bytes is null ? null : WorkspaceFileOperations.Hash(bytes)) != reviewedHashes[relative])
                        throw new IOException("A selected saved document changed after review. Refresh before staging.");
                    await RejectFiltersAsync(snapshot.RepositoryRoot, relative, cancellationToken);
                    var entry = await IndexEntryAsync(snapshot.RepositoryRoot, relative, cancellationToken);
                    var mode = entry is null ? "100644" : ParseEntry(entry).Mode;
                    var hash = bytes is null ? zero : (await RequireAsync(snapshot.RepositoryRoot,
                        new[] { "hash-object", "-w", "--path=" + relative, "--stdin" }, cancellationToken, bytes)).Trim();
                    updates.Append(bytes is null ? "0" : mode).Append(' ').Append(hash).Append('\t').Append(relative).Append('\0');
                }
                else
                {
                    var tree = snapshot.Head is null ? "" : await RequireAsync(snapshot.RepositoryRoot,
                        new[] { "ls-tree", "-z", snapshot.Head, "--", relative }, cancellationToken);
                    if (tree.Length > 0 && !tree.StartsWith("100644 blob ", StringComparison.Ordinal) && !tree.StartsWith("100755 blob ", StringComparison.Ordinal))
                        throw new IOException("Only regular document entries can be unstaged.");
                    updates.Append(tree.Length == 0 ? $"0 {zero}\t{relative}\0" : tree.Replace(" blob ", " ", StringComparison.Ordinal));
                }
            }
            await RequireAsync(snapshot.RepositoryRoot, new[] { "update-index", "--add", "-z", "--index-info" },
                cancellationToken, Encoding.UTF8.GetBytes(updates.ToString()), temporaryIndex);
            await ValidateSnapshotAsync(snapshot, cancellationToken);
            if (stage)
                foreach (var relative in reviewedHashes.Keys)
                    if (await ReviewWorkingHashAsync(snapshot, relative, cancellationToken) != reviewedHashes[relative])
                        throw new IOException("A selected saved document changed while staging.");
            validateBeforePublish?.Invoke();
            var updatedIndex = await File.ReadAllBytesAsync(temporaryIndex, cancellationToken);
            await indexLock.WriteAsync(updatedIndex, cancellationToken);
            await indexLock.FlushAsync(cancellationToken);
            indexLock.Flush(flushToDisk: true);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(lockPath, snapshot.IndexPath, overwrite: true);
            published = true;
        }
        finally
        {
            await indexLock.DisposeAsync();
            if (!published) File.Delete(lockPath);
            File.Delete(temporaryIndex);
            File.Delete(temporaryIndex + ".lock");
        }
    }
}
