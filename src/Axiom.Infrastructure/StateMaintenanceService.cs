using Microsoft.Data.Sqlite;

namespace Axiom.Infrastructure;

public sealed class StateMaintenanceService(AppDataPaths paths)
{
    public async Task<StateInspection> InspectAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = OpenReadOnly(paths.DatabasePath);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check;";
        var messages = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken)) messages.Add(reader.GetString(0));
        var integrity = string.Join(Environment.NewLine, messages);
        var counts = new List<long>();
        foreach (var sql in new[] { "SELECT Version FROM SchemaInfo LIMIT 1", "SELECT COUNT(*) FROM Workspaces", "SELECT COUNT(*) FROM RecoveryDrafts", "SELECT COUNT(*) FROM DocumentMetadata", "SELECT COUNT(*) FROM DocumentMutations" })
        {
            command.CommandText = sql;
            counts.Add(Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)));
        }
        return new StateInspection(integrity, (int)counts[0], counts[1], counts[2], counts[3], counts[4]);
    }

    public Task<string> ExportBackupAsync(string destination, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fullPath = Path.GetFullPath(destination);
            if (string.Equals(fullPath, paths.DatabasePath, StringComparison.OrdinalIgnoreCase)) throw new IOException("Choose a separate backup path.");
            if (File.Exists(fullPath) || Directory.Exists(fullPath)) throw new IOException("The backup destination already exists; it was not overwritten.");
            var parent = Path.GetDirectoryName(fullPath)!;
            if (!Directory.Exists(parent)) throw new DirectoryNotFoundException("Choose an existing backup folder.");
            var temporary = Path.Combine(parent, ".axiom-backup-" + Guid.NewGuid().ToString("N") + ".db");
            try
            {
                BackupDatabase(paths.DatabasePath, temporary);
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(temporary, fullPath, false);
                return fullPath;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }, cancellationToken);
    }

    public static void BackupDatabase(string source, string destination)
    {
        if (File.Exists(destination)) throw new IOException("The backup destination already exists.");
        using var sourceConnection = OpenReadOnly(source);
        sourceConnection.Open();
        using var targetConnection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = destination, Pooling = false }.ToString());
        targetConnection.Open();
        sourceConnection.BackupDatabase(targetConnection);
        using var check = targetConnection.CreateCommand();
        check.CommandText = "PRAGMA integrity_check;";
        if (!string.Equals(check.ExecuteScalar()?.ToString(), "ok", StringComparison.Ordinal))
            throw new IOException("The backup did not pass SQLite integrity verification.");
    }

    private static SqliteConnection OpenReadOnly(string path) => new(new SqliteConnectionStringBuilder
    {
        DataSource = path
        , Mode = SqliteOpenMode.ReadOnly
        , Pooling = false
    }.ToString());
}
