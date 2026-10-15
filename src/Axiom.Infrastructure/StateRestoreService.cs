using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace Axiom.Infrastructure;

public sealed record StateRestoreReview(string Snapshot, string Hash, StateInspection Inspection);

public sealed class StateRestoreService(AppDataPaths paths)
{
    public async Task<StateRestoreReview> ReviewAsync(string backup, CancellationToken token = default)
    {
        backup = Path.GetFullPath(backup);
        if (backup.Equals(Path.GetFullPath(paths.DatabasePath), StringComparison.OrdinalIgnoreCase)) throw new IOException("Choose an exported backup, not the live database.");
        ReviewedWorkspaceMove.ValidateSource(backup);
        if (!File.Exists(backup) || new FileInfo(backup).Length > 1024L * 1024 * 1024) throw new IOException("Choose a readable state backup up to 1 GiB.");
        // Inspect a consistent online snapshot, including any committed WAL, without altering the source.
        Directory.CreateDirectory(paths.BackupDirectory);
        var snapshot = Path.Combine(paths.BackupDirectory, "restore-reviewed-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            token.ThrowIfCancellationRequested();
            StateMaintenanceService.BackupDatabase(backup, snapshot);
            var inspection = await InspectSnapshotAsync(snapshot, token);
            if (inspection.Integrity != "ok" || inspection.SchemaVersion != SqliteStateStore.CurrentSchemaVersion)
                throw new IOException($"Backup schema {inspection.SchemaVersion} is not supported for restore. This build requires schema {SqliteStateStore.CurrentSchemaVersion}.");
            if (inspection.PendingOperations != 0) throw new IOException("The backup contains pending file operations. Restore is refused to avoid replaying old filesystem state.");
            return new(snapshot, await HashAsync(snapshot, token), inspection);
        }
        catch { if (File.Exists(snapshot)) File.Delete(snapshot); throw; }
    }

    public static async Task<StateInspection> InspectSnapshotAsync(string snapshot, CancellationToken token = default)
    {
        // StateMaintenanceService reads only DatabasePath; keep snapshot inspection read-only.
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = snapshot, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check;";
        var integrity = (await command.ExecuteScalarAsync(token))?.ToString() ?? "unavailable";
        var counts = new List<long>();
        foreach (var query in new[] { "SELECT Version FROM SchemaInfo LIMIT 1", "SELECT COUNT(*) FROM Workspaces", "SELECT COUNT(*) FROM RecoveryDrafts", "SELECT COUNT(*) FROM DocumentMetadata", "SELECT COUNT(*) FROM DocumentMutations" })
        { command.CommandText = query; counts.Add(Convert.ToInt64(await command.ExecuteScalarAsync(token))); }
        return new(integrity, (int)counts[0], counts[1], counts[2], counts[3], counts[4]);
    }

    /// <summary>Caller holds SingleInstanceCoordinator ownership on the calling thread throughout this task.</summary>
    public async Task<string> RestoreOfflineAsync(string snapshot, string expectedHash, CancellationToken token = default)
    {
        var directory = Path.GetDirectoryName(paths.DatabasePath)!;
        if (File.Exists(Path.Combine(directory, "reviewed-move.json"))) throw new IOException("Resolve the interrupted workspace move before restoring state.");
        if (!Path.GetDirectoryName(Path.GetFullPath(snapshot))!.Equals(Path.GetFullPath(paths.BackupDirectory), StringComparison.OrdinalIgnoreCase)) throw new IOException("Use a reviewed snapshot in this state root's Backups folder.");
        if (await HashAsync(snapshot, token) != expectedHash) throw new IOException("The reviewed backup changed; restore refused.");
        var inspection = await InspectSnapshotAsync(snapshot, token);
        if (inspection.Integrity != "ok" || inspection.SchemaVersion != SqliteStateStore.CurrentSchemaVersion || inspection.PendingOperations != 0) throw new IOException("The reviewed backup is incompatible or unsafe.");
        var current = await new StateMaintenanceService(paths).InspectAsync(token);
        if (current.PendingOperations != 0) throw new IOException("Resolve current pending file operations before restoring state.");
        var rollback = Path.Combine(paths.BackupDirectory, "pre-restore-" + Guid.NewGuid().ToString("N") + ".db");
        StateMaintenanceService.BackupDatabase(paths.DatabasePath, rollback);
        // Flush WAL before replacing; a busy external connection makes this fail, leaving current state intact.
        using (var live = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = paths.DatabasePath, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString()))
        {
            live.Open();
            using var checkpoint = live.CreateCommand(); checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            using var result = checkpoint.ExecuteReader();
            if (!result.Read() || result.GetInt32(0) != 0) throw new IOException("The current state database is still in use. Restore refused.");
        }
        // Verify exclusive access after all SQLite handles have closed.
        using (new FileStream(paths.DatabasePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        foreach (var suffix in new[] { "-wal", "-shm" })
            if (File.Exists(paths.DatabasePath + suffix)) File.Delete(paths.DatabasePath + suffix);
        var temporary = Path.Combine(directory, "restore-" + Guid.NewGuid().ToString("N") + ".db");
        var displaced = rollback + ".replaced";
        try
        {
            File.Copy(snapshot, temporary, false);
            token.ThrowIfCancellationRequested();
            File.Replace(temporary, paths.DatabasePath, displaced);
            try
            {
                var installed = await new StateMaintenanceService(paths).InspectAsync(CancellationToken.None);
                if (installed.Integrity != "ok" || installed.SchemaVersion != inspection.SchemaVersion) throw new IOException("Restored state verification failed.");
            }
            catch
            {
                File.Copy(rollback, temporary, false);
                File.Replace(temporary, paths.DatabasePath, null);
                throw;
            }
            return rollback;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static async Task<string> HashAsync(string path, CancellationToken token)
    { await using var stream = File.OpenRead(path); return Convert.ToHexString(await SHA256.HashDataAsync(stream, token)); }
}
