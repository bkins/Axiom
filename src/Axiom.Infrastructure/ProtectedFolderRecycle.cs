using System.IO.Compression;
using Axiom.Core;

namespace Axiom.Infrastructure;

public sealed class ProtectedFolderRecycle(SqliteStateStore state, IRecycleBin? recycle = null)
{
    public async Task<string> ExecuteAsync(WorkspaceRecord workspace, string relative, string reviewedHash, CancellationToken token = default)
    {
        var folder = WorkspaceMutationPolicy.Resolve(workspace.RootPath, relative, false);
        if (WorkspacePathBoundary.Contains(state.Paths.Root, folder, true) || WorkspacePathBoundary.Contains(folder, state.Paths.Root, true)) throw new IOException("App state cannot be recycled.");
        var operations = new WorkspaceFileOperations(state);
        if (await operations.ReviewFolderAsync(workspace, relative, token) != reviewedHash) throw new IOException("Folder changed since review.");
        // ReviewFolder rejects reparse points and bounds the entry count before enumeration.
        var files = Directory.GetFiles(folder, "*", SearchOption.AllDirectories);
        if (files.Sum(path => new FileInfo(path).Length) > 20 * 1024 * 1024) throw new IOException("Protected folder recycling supports up to 20 MiB. Folder retained.");
        Directory.CreateDirectory(state.Paths.BackupDirectory);
        var backup = Path.Combine(state.Paths.BackupDirectory, "RecycledFolder-" + Guid.NewGuid().ToString("N") + ".zip");
        var temporary = backup + ".tmp";
        try
        {
            ZipFile.CreateFromDirectory(folder, temporary, CompressionLevel.Optimal, includeBaseDirectory: true);
            // Read every archived entry and compare exact content, not only ZIP metadata.
            using (var archive = ZipFile.OpenRead(temporary))
            {
                foreach (var path in files)
                {
                    var entry = archive.GetEntry(Path.GetFileName(folder) + "/" + Path.GetRelativePath(folder, path).Replace('\\', '/')) ?? throw new IOException("Folder backup is incomplete.");
                    using var stream = entry.Open(); using var memory = new MemoryStream();
                    await stream.CopyToAsync(memory, token);
                    var current = await File.ReadAllBytesAsync(path, token);
                    if (!memory.ToArray().SequenceEqual(current)) throw new IOException("Folder changed while backing up.");
                }
            }
            if (await operations.ReviewFolderAsync(workspace, relative, token) != reviewedHash) throw new IOException("Folder changed during backup. Nothing recycled.");
            File.Move(temporary, backup, false);
            await File.WriteAllTextAsync(backup + ".txt", $"Original folder: {folder}\nCreated: {DateTimeOffset.Now:O}\nRestore the folder using Windows Recycle Bin. This verified ZIP also contains its complete files and empty folders; extract to a new location before reviewing. Never overwrite newer work blindly. App state/Markdown backup restoration does not restore this ZIP automatically.", token);
            token.ThrowIfCancellationRequested();
            (recycle ?? new WindowsRecycleBin()).Recycle(folder);
            if (Directory.Exists(folder)) throw new IOException("Recycling was cancelled or failed. Folder retained; verified backup is at " + backup);
            foreach (var path in files) await state.RemovePathAsync(path, CancellationToken.None);
            foreach (var pin in await state.GetPinsAsync(workspace.WorkspaceId, CancellationToken.None))
                if (WorkspacePathBoundary.Contains(folder, Path.Combine(workspace.RootPath, pin.RelativePath)))
                    await state.RemovePinAsync(workspace.WorkspaceId, pin.RelativePath, CancellationToken.None);
            return backup;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
