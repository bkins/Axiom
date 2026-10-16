using System.Text;
using Axiom.Core;

namespace Axiom.Infrastructure;

public sealed record FolderCopyFile(string Relative, byte[] Before, byte[] After);
public sealed record FolderCopyReview(WorkspaceRecord Workspace, string Source, string Destination, string SourceHash,
    IReadOnlyList<string> Directories, IReadOnlyList<FolderCopyFile> Files, IReadOnlyList<string> Warnings);

public sealed class ReviewedFolderCopy(SqliteStateStore state)
{
    public async Task<FolderCopyReview> ReviewAsync(WorkspaceRecord workspace, string sourceRelative, string destinationRelative, CancellationToken token = default)
    {
        if ((await state.GetPendingMutationsAsync(token)).Count > 0 || new ReviewedWorkspaceMove(state).Pending) throw new IOException("Reconcile the pending operation before copying.");
        var source = WorkspaceMutationPolicy.Resolve(workspace.RootPath, sourceRelative, false);
        var destination = WorkspaceMutationPolicy.Resolve(workspace.RootPath, destinationRelative, false);
        WorkspaceFileOperations.ValidateAncestors(workspace.RootPath, source);
        WorkspaceFileOperations.ValidateAncestors(workspace.RootPath, destination);
        if (!Directory.Exists(source) || File.Exists(destination) || Directory.Exists(destination) || WorkspacePathBoundary.Contains(source, destination, true)) throw new IOException("Choose an existing source folder and a new destination outside its subtree.");
        if (WorkspacePathBoundary.Contains(state.Paths.Root, source, true) || WorkspacePathBoundary.Contains(source, state.Paths.Root, true) || WorkspacePathBoundary.Contains(state.Paths.Root, destination, true)) throw new IOException("App state cannot be copied by workspace tools.");
        var hash = await new WorkspaceFileOperations(state).ReviewFolderAsync(workspace, sourceRelative, token);
        var directories = Directory.GetDirectories(source, "*", SearchOption.AllDirectories).Select(path => Path.GetRelativePath(source, path)).Order(StringComparer.Ordinal).ToArray();
        var paths = Directory.GetFiles(source, "*", SearchOption.AllDirectories).Order(WorkspacePathBoundary.Comparer).ToArray();
        if (paths.Sum(path => new FileInfo(path).Length) > 64 * 1024 * 1024) throw new IOException("Folder copy supports 64 MiB total; source retained.");
        string Map(string path) => WorkspacePathBoundary.Contains(source, path, true) ? Path.Combine(destination, Path.GetRelativePath(source, path)) : path;
        var files = new List<FolderCopyFile>(); var warnings = new List<string>();
        foreach (var path in paths)
        {
            token.ThrowIfCancellationRequested(); ReviewedWorkspaceMove.ValidateSource(path);
            var bytes = await File.ReadAllBytesAsync(path, token); var after = bytes;
            if (Path.GetExtension(path).ToLowerInvariant() is ".md" or ".markdown")
            {
                var text = MarkdownIdentityBytes.Decode(bytes);
                var repair = RelativeReferenceRepair.Plan(text, path, Map(path), Map, target => File.Exists(target) || Directory.Exists(target));
                warnings.AddRange(repair.Warnings.Select(warning => Path.GetRelativePath(source, path) + ": " + warning));
                after = Encoding.UTF8.GetBytes(repair.Text);
                if (bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) after = new byte[] { 0xef, 0xbb, 0xbf }.Concat(after).ToArray();
                if (DocumentIdCodec.Find(text) is not null) after = MarkdownIdentityBytes.Assign(after, Guid.NewGuid(), true);
            }
            files.Add(new(Path.GetRelativePath(source, path), bytes, after));
        }
        warnings.Add("Incoming links outside the copy stay with the originals. Wiki links and raw HTML are retained for manual review; metadata, pins and protected drafts are not cloned.");
        return new(workspace, source, destination, hash, directories, files, warnings);
    }

    public async Task ApplyAsync(FolderCopyReview review, CancellationToken token = default)
    {
        var current = await ReviewAsync(review.Workspace, Path.GetRelativePath(review.Workspace.RootPath, review.Source), Path.GetRelativePath(review.Workspace.RootPath, review.Destination), token);
        if (current.SourceHash != review.SourceHash || current.Files.Count != review.Files.Count || current.Files.Zip(review.Files).Any(pair => pair.First.Relative != pair.Second.Relative || !pair.First.Before.SequenceEqual(pair.Second.Before))) throw new IOException("Source changed since copy review. Nothing was copied.");
        var operation = Guid.NewGuid();
        var staging = review.Destination + ".axiom-copy-" + operation.ToString("N");
        var published = false; DocumentMutationIntent? intent = null;
        Directory.CreateDirectory(staging);
        try
        {
            foreach (var directory in review.Directories) Directory.CreateDirectory(Path.Combine(staging, directory));
            foreach (var file in review.Files)
            {
                token.ThrowIfCancellationRequested();
                var path = WorkspaceMutationPolicy.Resolve(staging, file.Relative, false);
                await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.WriteThrough);
                await output.WriteAsync(file.After, token); output.Flush(true);
            }
            var hash = await WorkspaceFileOperations.HashFolderAsync(staging, token);
            foreach (var file in review.Files)
                if (!(await File.ReadAllBytesAsync(Path.Combine(staging, file.Relative), token)).SequenceEqual(file.After)) throw new IOException("Staged copy failed verification.");
            intent = new(operation, review.Workspace.WorkspaceId, review.Workspace.RootPath, DocumentMutationKind.CopyFolder, review.Source, review.Destination, hash, null);
            await state.PrepareMutationAsync(intent, token);
            WorkspaceFileOperations.ValidateAncestors(review.Workspace.RootPath, review.Destination);
            token.ThrowIfCancellationRequested();
            Directory.Move(staging, review.Destination); published = true;
            await state.ReconcileMutationAsync(intent, true, CancellationToken.None);
        }
        catch
        {
            if (!published && intent is not null) await state.ReconcileMutationAsync(intent, false, CancellationToken.None);
            throw;
        }
        finally { if (!published && Directory.Exists(staging)) Directory.Delete(staging, true); }
    }
}
