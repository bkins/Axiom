using System.Text;
using Axiom.Core;

namespace Axiom.Infrastructure;

public sealed record FolderClipboardFile(string Relative, byte[] Content);
public sealed record FolderClipboardSnapshot(string Source, IReadOnlyList<string> Directories, IReadOnlyList<FolderClipboardFile> Files);

/// <summary>Saved folder snapshots; pasting never rereads source content.</summary>
public sealed class WorkspaceFolderClipboard(SqliteStateStore state)
{
    public async Task<FolderClipboardSnapshot> CaptureAsync(WorkspaceRecord workspace, string relative, CancellationToken token = default)
    {
        if ((await state.GetPendingMutationsAsync(token)).Count > 0 || new ReviewedWorkspaceMove(state).Pending)
            throw new IOException("Finish the pending file operation before copying.");
        var source = WorkspaceMutationPolicy.Resolve(workspace.RootPath, relative, false);
        WorkspaceFileOperations.ValidateAncestors(workspace.RootPath, source);
        if (!Directory.Exists(source) || WorkspacePathBoundary.Contains(source, state.Paths.Root, true) || WorkspacePathBoundary.Contains(state.Paths.Root, source, true))
            throw new IOException("Choose a folder outside Axiom application state.");
        var operations = new WorkspaceFileOperations(state);
        var directories = new List<string>();
        var files = new List<string>();
        var pending = new Stack<string>();
        pending.Push(source);
        long estimatedBytes = 0;
        while (pending.TryPop(out var folder))
        {
            token.ThrowIfCancellationRequested();
            WorkspaceFileOperations.ValidateAncestors(workspace.RootPath, folder);
            ReviewedWorkspaceMove.ValidateSource(folder);
            foreach (var path in Directory.EnumerateFileSystemEntries(folder))
            {
                token.ThrowIfCancellationRequested();
                WorkspaceFileOperations.ValidateAncestors(workspace.RootPath, path);
                ReviewedWorkspaceMove.ValidateSource(path);
                if (directories.Count + files.Count >= 10000) throw new IOException("Copy supports at most 10,000 entries.");
                if (Directory.Exists(path)) { directories.Add(path); pending.Push(path); }
                else
                {
                    estimatedBytes += new FileInfo(path).Length;
                    if (estimatedBytes > 64 * 1024 * 1024) throw new IOException("Copy supports at most 64 MiB.");
                    files.Add(path);
                }
            }
        }
        var captured = new List<FolderClipboardFile>();
        long total = 0;
        foreach (var path in files)
        {
            token.ThrowIfCancellationRequested();
            WorkspaceFileOperations.ValidateAncestors(workspace.RootPath, path);
            ReviewedWorkspaceMove.ValidateSource(path);
            var bytes = await File.ReadAllBytesAsync(path, token);
            total += bytes.Length;
            if (total > 64 * 1024 * 1024) throw new IOException("The folder exceeds the 64 MiB copy limit.");
            if (Path.GetExtension(path).ToLowerInvariant() is ".md" or ".markdown") _ = MarkdownIdentityBytes.Decode(bytes);
            captured.Add(new(Path.GetRelativePath(source, path), bytes));
        }
        var entries = directories.Select(path => "D:" + Path.GetRelativePath(source, path))
            .Concat(captured.Select(file => "F:" + file.Relative + ":" + file.Content.Length + ":" + WorkspaceFileOperations.Hash(file.Content)))
            .Order(StringComparer.Ordinal);
        var capturedHash = WorkspaceFileOperations.Hash(Encoding.UTF8.GetBytes(string.Join('\n', entries)));
        if (capturedHash != await operations.ReviewFolderAsync(workspace, relative, token)) throw new IOException("The folder changed during Copy. Try again.");
        return new(source, directories.Select(path => Path.GetRelativePath(source, path)).ToArray(), captured);
    }

    public FolderCopyReview Review(FolderClipboardSnapshot snapshot, WorkspaceRecord workspace, string destinationRelative)
    {
        var destination = WorkspaceMutationPolicy.Resolve(workspace.RootPath, destinationRelative, false);
        WorkspaceFileOperations.ValidateAncestors(workspace.RootPath, destination);
        if (File.Exists(destination) || Directory.Exists(destination) || WorkspacePathBoundary.Contains(state.Paths.Root, destination, true))
            throw new IOException("Choose a new destination outside application state. Existing files and folders are not replaced.");
        var capturedPaths = snapshot.Directories.Concat(snapshot.Files.Select(file => file.Relative))
            .Select(relative => WorkspaceMutationPolicy.Resolve(snapshot.Source, relative, false)).ToHashSet(WorkspacePathBoundary.Comparer);
        capturedPaths.Add(snapshot.Source);
        string Map(string path) => capturedPaths.Contains(path) ? Path.Combine(destination, Path.GetRelativePath(snapshot.Source, path)) : path;
        bool Exists(string path) => capturedPaths.Contains(path) || File.Exists(path) || Directory.Exists(path);
        var files = new List<FolderCopyFile>();
        var warnings = new List<string>();
        foreach (var file in snapshot.Files)
        {
            var path = WorkspaceMutationPolicy.Resolve(snapshot.Source, file.Relative, false);
            var bytes = file.Content.ToArray();
            var after = bytes;
            if (Path.GetExtension(path).ToLowerInvariant() is ".md" or ".markdown")
            {
                var text = MarkdownIdentityBytes.Decode(bytes);
                var repair = RelativeReferenceRepair.Plan(text, path, Map(path), Map, Exists);
                warnings.AddRange(repair.Warnings.Select(warning => file.Relative + ": " + warning));
                after = Encoding.UTF8.GetBytes(repair.Text);
                if (bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) after = new byte[] { 0xef, 0xbb, 0xbf }.Concat(after).ToArray();
                if (DocumentIdCodec.Find(text) is not null) after = MarkdownIdentityBytes.Assign(after, Guid.NewGuid(), true);
            }
            files.Add(new(file.Relative, bytes, after));
        }
        warnings.Add("Links from other files still point to the originals. Metadata, pins and unsaved edits are not copied. Review wiki links and raw HTML manually.");
        return new(workspace, snapshot.Source, destination, "clipboard-snapshot", snapshot.Directories.ToArray(), files, warnings);
    }

    public async Task ApplyAsync(FolderCopyReview review, CancellationToken token = default)
    {
        if ((await state.GetPendingMutationsAsync(token)).Count > 0 || new ReviewedWorkspaceMove(state).Pending)
            throw new IOException("Finish the pending file operation before pasting.");
        await new ReviewedFolderCopy(state).PublishSnapshotAsync(review, token);
    }
}
