using System.Security.Cryptography;
using System.Text;
using Axiom.Core;

namespace Axiom.Infrastructure;

public sealed partial class WorkspaceFileOperations
{
    public async Task<string> ReviewFolderAsync(WorkspaceRecord workspace, string relative, CancellationToken cancellationToken = default)
    {
        var source = WorkspaceMutationPolicy.Resolve(workspace.RootPath, relative, false);
        ValidateAncestors(workspace.RootPath, source);
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException("The source folder is missing.");
        return await HashFolderAsync(source, cancellationToken);
    }

    public async Task<string> MoveFolderAsync(WorkspaceRecord workspace, string sourceRelative, string destinationRelative,
        string reviewedHash, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var source = WorkspaceMutationPolicy.Resolve(workspace.RootPath, sourceRelative, false);
            var destination = WorkspaceMutationPolicy.Resolve(workspace.RootPath, destinationRelative, false);
            ValidateAncestors(workspace.RootPath, source);
            ValidateAncestors(workspace.RootPath, destination);
            if (WorkspacePathBoundary.Contains(source, destination, includeRoot: true))
                throw new ArgumentException("A folder cannot move into itself or one of its descendants. Case-only rename is not supported.");
            if (File.Exists(destination) || Directory.Exists(destination)) throw new IOException("The destination exists; nothing was overwritten.");
            if (await HashFolderAsync(source, cancellationToken) != reviewedHash) throw new IOException("The folder changed since review; refresh before moving.");
            var intent = new DocumentMutationIntent(Guid.NewGuid(), workspace.WorkspaceId, workspace.RootPath,
                DocumentMutationKind.MoveFolder, source, destination, reviewedHash, null);
            await state.PrepareMutationAsync(intent, cancellationToken);
            var moved = false;
            try
            {
                ValidateAncestors(workspace.RootPath, source);
                ValidateAncestors(workspace.RootPath, destination);
                if (await HashFolderAsync(source, cancellationToken) != reviewedHash) throw new IOException("The folder changed before moving; nothing was moved.");
                cancellationToken.ThrowIfCancellationRequested();
                Directory.Move(source, destination);
                moved = true;
                await state.ReconcileMutationAsync(intent, true, CancellationToken.None);
                _log.Information("document.mutation.MoveFolder", destination);
                return destination;
            }
            catch (Exception exception)
            {
                if (!moved) await state.ReconcileMutationAsync(intent, false, CancellationToken.None);
                if (moved) throw new IOException("The folder moved, but Axiom state needs reconciliation. Restart before another operation.", exception);
                throw;
            }
        }
        finally { _gate.Release(); }
    }

    private async Task<string> ReconcileFolderMoveAsync(DocumentMutationIntent intent, CancellationToken cancellationToken)
    {
        ValidateAncestors(intent.RootPath, intent.SourcePath!, false);
        ValidateAncestors(intent.RootPath, intent.DestinationPath!, false);
        var sourceExists = Directory.Exists(intent.SourcePath);
        var destinationExists = Directory.Exists(intent.DestinationPath);
        var applied = !sourceExists && destinationExists && await HashFolderAsync(intent.DestinationPath!, cancellationToken) == intent.ExpectedHash;
        var unapplied = sourceExists && !destinationExists && await HashFolderAsync(intent.SourcePath!, cancellationToken) == intent.ExpectedHash;
        if (!applied && !unapplied) return $"Needs inspection: folder move {intent.SourcePath} → {intent.DestinationPath}. No filesystem operation was repeated.";
        await state.ReconcileMutationAsync(intent, applied, cancellationToken);
        return $"Reconciled folder move: {(applied ? "filesystem change present" : "source retained")}: {intent.DestinationPath}";
    }

    internal static async Task<string> HashFolderAsync(string root, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("The source folder is missing.");
        var entries = new List<string>();
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var folder))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.GetAttributes(folder).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Linked folders cannot be moved.");
            foreach (var path in Directory.EnumerateFileSystemEntries(folder))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entries.Count >= 10000) throw new IOException("Folder moves support up to 10,000 entries. Choose a smaller subtree.");
                var attributes = File.GetAttributes(path);
                if (attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Linked files or folders cannot be moved.");
                var relative = Path.GetRelativePath(root, path);
                if (attributes.HasFlag(FileAttributes.Directory)) { entries.Add("D:" + relative); pending.Push(path); }
                else
                {
                    await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
                    var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
                    entries.Add("F:" + relative + ":" + stream.Length + ":" + hash);
                }
            }
        }
        entries.Sort(StringComparer.Ordinal);
        return Hash(Encoding.UTF8.GetBytes(string.Join('\n', entries)));
    }
}
