using System.Text;
using Axiom.Core;

namespace Axiom.Infrastructure;

public sealed partial class GitWorkspaceService
{
    public async Task StageAsync(GitRepositorySnapshot snapshot, string relativePath, string? reviewedWorkingHash, CancellationToken cancellationToken = default)
    {
        await ValidateSnapshotAsync(snapshot, cancellationToken);
        ValidateSelection(snapshot, relativePath);
        var path = ManagedPath(snapshot, relativePath);
        var bytes = File.Exists(path) ? await ReadWorkingAsync(path, cancellationToken) : null;
        if ((bytes is null ? null : WorkspaceFileOperations.Hash(bytes)) != reviewedWorkingHash) throw new IOException("The saved document changed after review. Refresh before staging.");
        await RejectFiltersAsync(snapshot.RepositoryRoot, relativePath, cancellationToken);
        var existing = await IndexEntryAsync(snapshot.RepositoryRoot, relativePath, cancellationToken);
        var mode = existing is null ? "100644" : ParseEntry(existing).Mode;
        var hash = bytes is null ? await ZeroHashAsync(snapshot.RepositoryRoot, cancellationToken)
            : (await RequireAsync(snapshot.RepositoryRoot, new[] { "hash-object", "-w", "--path=" + relativePath, "--stdin" }, cancellationToken, bytes)).Trim();
        if (bytes is null) mode = "0";
        cancellationToken.ThrowIfCancellationRequested();
        await RequireAsync(snapshot.RepositoryRoot, new[] { "update-index", "--add", "-z", "--index-info" }, CancellationToken.None,
            Encoding.UTF8.GetBytes($"{mode} {hash}\t{relativePath}\0"));
    }

    public async Task UnstageAsync(GitRepositorySnapshot snapshot, string relativePath, CancellationToken cancellationToken = default)
    {
        await ValidateSnapshotAsync(snapshot, cancellationToken);
        ValidateSelection(snapshot, relativePath);
        ManagedPath(snapshot, relativePath);
        var tree = snapshot.Head is null ? "" : await RequireAsync(snapshot.RepositoryRoot, new[] { "ls-tree", "-z", snapshot.Head, "--", relativePath }, cancellationToken);
        var entry = tree.Length == 0 ? $"0 {await ZeroHashAsync(snapshot.RepositoryRoot, cancellationToken)}\t{relativePath}\0"
            : tree.Replace(" blob ", " ", StringComparison.Ordinal);
        if (tree.Length > 0 && !tree.StartsWith("100644 blob ", StringComparison.Ordinal) && !tree.StartsWith("100755 blob ", StringComparison.Ordinal))
            throw new IOException("Only regular document entries can be unstaged.");
        cancellationToken.ThrowIfCancellationRequested();
        await RequireAsync(snapshot.RepositoryRoot, new[] { "update-index", "-z", "--index-info" }, CancellationToken.None, Encoding.UTF8.GetBytes(entry));
    }

    public async Task<GitSelectedCommitReview> ReviewCommitAsync(GitRepositorySnapshot snapshot, IReadOnlyList<string> relativePaths, CancellationToken cancellationToken = default)
    {
        await ValidateSnapshotAsync(snapshot, cancellationToken);
        if (relativePaths.Count == 0 || relativePaths.Distinct(StringComparer.Ordinal).Count() != relativePaths.Count)
            throw new ArgumentException("Select distinct staged documents.");
        var hashes = new Dictionary<string, string?>();
        var entries = new Dictionary<string, string?>();
        foreach (var relative in relativePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateSelection(snapshot, relative);
            var status = snapshot.Files.Single(file => file.RelativePath == relative);
            if (status.IndexStatus is " " or "?") throw new IOException("Stage each selected document before reviewing its commit.");
            var path = ManagedPath(snapshot, relative);
            hashes[relative] = File.Exists(path) ? WorkspaceFileOperations.Hash(await ReadWorkingAsync(path, cancellationToken)) : null;
            entries[relative] = await IndexEntryAsync(snapshot.RepositoryRoot, relative, cancellationToken);
        }
        return new(snapshot, relativePaths.ToArray(), hashes, entries);
    }

    public async Task<string> CommitAsync(GitSelectedCommitReview review, string message, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(message) || message.Length > 4096 || message.Contains('\0')) throw new ArgumentException("Provide a commit message up to 4096 characters.");
        if (review.RelativePaths.Count == 0 || review.RelativePaths.Distinct(StringComparer.Ordinal).Count() != review.RelativePaths.Count)
            throw new ArgumentException("Select distinct staged documents.");
        var snapshot = review.Repository;
        await ValidateSnapshotAsync(snapshot, cancellationToken);
        for (var ancestor = snapshot.IndexPath; ancestor is not null; ancestor = Path.GetDirectoryName(ancestor))
            if ((File.Exists(ancestor) || Directory.Exists(ancestor)) && File.GetAttributes(ancestor).HasFlag(FileAttributes.ReparsePoint))
                throw new IOException("Linked Git administration paths are unsupported.");
        // Git's real-index lock protects other staged entries while an isolated commit tree is constructed.
        // The real index bytes are never rewritten: selected entries already match the new commit tree.
        await using var indexLock = new FileStream(snapshot.IndexPath + ".lock", FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
        if (await IndexHashAsync(snapshot.IndexPath, cancellationToken) != snapshot.IndexHash) throw new IOException("The Git index changed after review.");
        var temporaryIndex = Path.Combine(Path.GetTempPath(), "AxiomSelectedGitIndex-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var relative in review.RelativePaths)
            {
                ValidateSelection(snapshot, relative);
                var path = ManagedPath(snapshot, relative);
                var currentHash = File.Exists(path) ? WorkspaceFileOperations.Hash(await ReadWorkingAsync(path, cancellationToken)) : null;
                if (currentHash != review.WorkingHashes[relative]) throw new IOException("A selected saved document changed after commit review.");
                if (await IndexEntryAsync(snapshot.RepositoryRoot, relative, cancellationToken) != review.IndexEntries[relative]) throw new IOException("Selected staging changed after commit review.");
            }
            await RequireAsync(snapshot.RepositoryRoot, snapshot.Head is null ? new[] { "read-tree", "--empty" } : new[] { "read-tree", snapshot.Head }, cancellationToken, indexPath: temporaryIndex);
            var zero = await ZeroHashAsync(snapshot.RepositoryRoot, cancellationToken);
            foreach (var relative in review.RelativePaths)
            {
                var entry = review.IndexEntries[relative] ?? $"0 {zero}\t{relative}";
                await RequireAsync(snapshot.RepositoryRoot, new[] { "update-index", "--add", "-z", "--index-info" }, cancellationToken, Encoding.UTF8.GetBytes(entry.TrimEnd('\0') + "\0"), temporaryIndex);
            }
            var tree = (await RequireAsync(snapshot.RepositoryRoot, new[] { "write-tree" }, cancellationToken, indexPath: temporaryIndex)).Trim();
            var arguments = new List<string> { "-c", "commit.gpgsign=false", "commit-tree", tree };
            if (snapshot.Head is not null) { arguments.Add("-p"); arguments.Add(snapshot.Head); }
            arguments.Add("-m");
            arguments.Add(message);
            var commit = (await RequireAsync(snapshot.RepositoryRoot, arguments, cancellationToken, indexPath: temporaryIndex)).Trim();
            await ValidateSnapshotAsync(snapshot, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            // Publish only the reviewed branch, with compare-and-swap against its reviewed tip.
            await RequireAsync(snapshot.RepositoryRoot, new[] { "update-ref", "--create-reflog", "-m", "Axiom selected saved-document commit", snapshot.BranchReference, commit, snapshot.Head ?? zero }, CancellationToken.None);
            return commit;
        }
        finally
        {
            File.Delete(temporaryIndex);
            File.Delete(temporaryIndex + ".lock");
        }
    }

    private async Task ValidateSnapshotAsync(GitRepositorySnapshot snapshot, CancellationToken cancellationToken)
    {
        if (!snapshot.CanMutate) throw new IOException(snapshot.Reason ?? "This repository is read-only.");
        var current = await InspectAsync(snapshot.WorkspaceRoot, cancellationToken);
        if (current is null || !current.CanMutate || current.RepositoryRoot != snapshot.RepositoryRoot || current.BranchReference != snapshot.BranchReference || current.Head != snapshot.Head || current.IndexPath != snapshot.IndexPath || current.IndexHash != snapshot.IndexHash)
            throw new IOException("Repository state changed after review. Refresh before making a Git change.");
    }

    private static void ValidateSelection(GitRepositorySnapshot snapshot, string relative)
    {
        var file = snapshot.Files.SingleOrDefault(file => file.RelativePath == relative);
        if (file is null || !file.CanManage) throw new IOException(file?.Reason ?? "Select a changed Markdown document inside the workspace.");
    }

    private async Task RejectFiltersAsync(string root, string relativePath, CancellationToken cancellationToken)
    {
        var attributes = (await RequireAsync(root, new[] { "check-attr", "-z", "filter", "--", relativePath }, cancellationToken)).Split('\0');
        if (attributes.Length < 3 || attributes[2] is not "unspecified" and not "unset")
            throw new IOException("Git clean/LFS filters require external Git. Axiom does not run configured external filters.");
    }

    private async Task<string> ZeroHashAsync(string root, CancellationToken cancellationToken) =>
        new('0', (await RequireAsync(root, new[] { "rev-parse", "--show-object-format" }, cancellationToken)).Trim() == "sha256" ? 64 : 40);
}
