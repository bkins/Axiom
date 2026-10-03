using System.Text.Json;
using Axiom.Core;
using Microsoft.Data.Sqlite;

namespace Axiom.Infrastructure;

public sealed partial class SqliteStateStore
{
    private static async Task RebindFolderStateAsync(SqliteConnection connection, SqliteTransaction transaction,
        DocumentMutationIntent intent, CancellationToken cancellationToken)
    {
        string Map(string path) => path.Equals(intent.SourcePath, WorkspacePathBoundary.Comparison) ? intent.DestinationPath!
            : intent.Kind == DocumentMutationKind.MoveFolder && WorkspacePathBoundary.Contains(intent.SourcePath!, path)
                ? Path.Combine(intent.DestinationPath!, Path.GetRelativePath(intent.SourcePath!, path)) : path;
        string MapRelative(string path) => Path.GetRelativePath(intent.RootPath, Map(Path.Combine(intent.RootPath, path)));
        // Bound comparisons in managed code follow the filesystem path policy and
        // avoid SQL LIKE treating underscores or percent signs as wildcards.
        foreach (var table in new[] { "DocumentMetadata", "RecoveryDrafts" })
        {
            await using var read = connection.CreateCommand();
            read.Transaction = transaction;
            read.CommandText = $"SELECT DISTINCT FilePath FROM {table} WHERE FilePath IS NOT NULL;";
            var paths = new List<string>();
            await using (var reader = await read.ExecuteReaderAsync(cancellationToken))
                while (await reader.ReadAsync(cancellationToken)) paths.Add(reader.GetString(0));
            foreach (var path in paths)
            {
                var mapped = Map(path);
                if (mapped == path) continue;
                await using var update = connection.CreateCommand();
                update.Transaction = transaction;
                update.CommandText = $"UPDATE {table} SET FilePath=$new WHERE FilePath=$old;";
                update.Parameters.AddWithValue("$old", path);
                update.Parameters.AddWithValue("$new", mapped);
                await update.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        await using (var read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = "SELECT RelativePath FROM WorkspacePins WHERE WorkspaceId=$workspace;";
            read.Parameters.AddWithValue("$workspace", intent.WorkspaceId.ToString("D"));
            var pins = new List<string>();
            await using (var reader = await read.ExecuteReaderAsync(cancellationToken))
                while (await reader.ReadAsync(cancellationToken)) pins.Add(reader.GetString(0));
            foreach (var pin in pins)
            {
                var mapped = MapRelative(pin);
                if (mapped == pin) continue;
                await using var update = connection.CreateCommand();
                update.Transaction = transaction;
                update.CommandText = "UPDATE WorkspacePins SET RelativePath=$new WHERE WorkspaceId=$workspace AND RelativePath=$old;";
                update.Parameters.AddWithValue("$workspace", intent.WorkspaceId.ToString("D"));
                update.Parameters.AddWithValue("$old", pin);
                update.Parameters.AddWithValue("$new", mapped);
                await update.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        await using var settings = connection.CreateCommand();
        settings.Transaction = transaction;
        settings.CommandText = "SELECT SettingValue FROM AppSettings WHERE SettingKey=$key;";
        settings.Parameters.AddWithValue("$key", "RecentDocuments");
        if (await settings.ExecuteScalarAsync(cancellationToken) is string recentJson)
        {
            var recent = JsonSerializer.Deserialize<RecentDocument[]>(recentJson) ?? [];
            await WriteSettingAsync(connection, transaction, "RecentDocuments",
                JsonSerializer.Serialize(recent.Select(document => document with { Path = Map(document.Path) }).ToArray()), cancellationToken);
        }
        var sessionKey = SessionSettingKey(intent.WorkspaceId);
        settings.Parameters["$key"].Value = sessionKey;
        if (await settings.ExecuteScalarAsync(cancellationToken) is string sessionJson)
        {
            var session = JsonSerializer.Deserialize<WorkspaceDocumentSession>(sessionJson);
            if (session is not null)
                await WriteSettingAsync(connection, transaction, sessionKey, JsonSerializer.Serialize(session with
                {
                    ActiveRelativePath = session.ActiveRelativePath is null ? null : MapRelative(session.ActiveRelativePath)
                  , Documents = session.Documents.Select(document => document with { RelativePath = MapRelative(document.RelativePath) }).ToArray()
                }), cancellationToken);
        }
        settings.CommandText = "DELETE FROM AppSettings WHERE SettingKey=$key;";
        settings.Parameters["$key"].Value = $"LinkIndex:{intent.WorkspaceId:D}";
        await settings.ExecuteNonQueryAsync(cancellationToken);
    }
}
