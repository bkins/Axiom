using Axiom.Core;
using Microsoft.Data.Sqlite;

namespace Axiom.Infrastructure.Tests;

public sealed class StatePathPolicyTests
{
    [Fact]
    public async Task Invalid_Legacy_Foreign_Key_Rolls_Back_Table_Rebuild_And_Schema_Change()
    {
        var root = Path.Combine(Path.GetTempPath(), "Axiom-path-rollback-" + Guid.NewGuid().ToString("N"));
        var paths = new AppDataPaths(root);
        await new SqliteStateStore(paths, caseSensitivePaths: false).InitializeAsync();
        await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = paths.DatabasePath, Pooling = false }.ToString()))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA foreign_keys=OFF; UPDATE SchemaInfo SET Version=8; DELETE FROM AppSettings WHERE SettingKey='FilesystemPathPolicy'; INSERT INTO ActiveWorkspace VALUES(1,'missing-workspace');";
            await command.ExecuteNonQueryAsync();
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => new SqliteStateStore(paths, caseSensitivePaths: true).InitializeAsync());

        await using var verify = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = paths.DatabasePath, Pooling = false }.ToString());
        await verify.OpenAsync();
        await using var read = verify.CreateCommand();
        read.CommandText = "SELECT Version FROM SchemaInfo;";
        Assert.Equal(8L, await read.ExecuteScalarAsync());
        read.CommandText = "SELECT sql FROM sqlite_master WHERE name='Workspaces';";
        Assert.Contains("NOCASE", (string)(await read.ExecuteScalarAsync())!);
        read.CommandText = "SELECT COUNT(*) FROM AppSettings WHERE SettingKey='FilesystemPathPolicy';";
        Assert.Equal(0L, await read.ExecuteScalarAsync());
        read.CommandText = "SELECT WorkspaceId FROM ActiveWorkspace;";
        Assert.Equal("missing-workspace", await read.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Binary_State_Preserves_Case_Distinct_Workspaces_Identities_Pins_And_Recents()
    {
        var root = Path.Combine(Path.GetTempPath(), "Axiom-binary-state-" + Guid.NewGuid().ToString("N"));
        var state = new SqliteStateStore(new AppDataPaths(Path.Combine(root, "state")), caseSensitivePaths: true);
        await state.InitializeAsync();
        var firstRoot = Path.Combine(root, "Cedar");
        var secondRoot = Path.Combine(root, "cedar");
        var first = await state.OpenWorkspaceAsync(firstRoot, "First", DateTimeOffset.UtcNow);
        var second = await state.OpenWorkspaceAsync(secondRoot, "Second", DateTimeOffset.UtcNow);
        var identity = Guid.NewGuid();
        var firstPath = Path.Combine(firstRoot, "Note.md");
        var secondPath = Path.Combine(firstRoot, "note.md");

        await state.RecordIdentityAsync(identity, firstPath);
        await state.RecordIdentityAsync(identity, secondPath);
        await state.AddPinAsync(first.WorkspaceId, "Note.md", DateTimeOffset.UtcNow);
        await state.AddPinAsync(first.WorkspaceId, "note.md", DateTimeOffset.UtcNow);
        await state.RecordRecentDocumentAsync(firstPath, firstRoot);
        await state.RecordRecentDocumentAsync(secondPath, firstRoot);

        Assert.NotEqual(first.WorkspaceId, second.WorkspaceId);
        Assert.Equal(2, (await state.GetIdentitiesAsync()).Count);
        Assert.Equal(2, (await state.GetPinsAsync(first.WorkspaceId)).Count);
        Assert.Equal(2, (await state.GetRecentDocumentsAsync()).Count);
        await state.RemovePinAsync(first.WorkspaceId, "Note.md");
        Assert.Equal("note.md", Assert.Single(await state.GetPinsAsync(first.WorkspaceId)).RelativePath);
        await state.RemovePathAsync(firstPath);
        Assert.Equal(secondPath, Assert.Single(await state.GetIdentitiesAsync()).Path);
        await state.ForgetWorkspaceAsync(first.WorkspaceId);
        Assert.Empty(await state.GetPinsAsync(first.WorkspaceId));
        await state.InitializeAsync();
        Assert.Equal(second.WorkspaceId, (await state.GetActiveWorkspaceAsync())!.WorkspaceId);
    }

    [Fact]
    public async Task Migrates_Legacy_State_Without_Losing_Active_Workspace_Drafts_Or_Classifications_And_Rejects_Policy_Reinterpretation()
    {
        var root = Path.Combine(Path.GetTempPath(), "Axiom-path-migrate-" + Guid.NewGuid().ToString("N"));
        var paths = new AppDataPaths(Path.Combine(root, "state"));
        var original = new SqliteStateStore(paths, caseSensitivePaths: false);
        await original.InitializeAsync();
        var workspace = await original.OpenWorkspaceAsync(root, "Original", DateTimeOffset.UtcNow);
        await original.AddPinAsync(workspace.WorkspaceId, "Original.md", DateTimeOffset.UtcNow);
        await original.SaveDraftAsync(new("protected", null, "unsaved source", DateTimeOffset.UtcNow, null, true));
        await original.SetAppearanceAsync(AppearancePreference.Dark);
        await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = paths.DatabasePath, Pooling = false }.ToString()))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE SchemaInfo SET Version=8; DELETE FROM AppSettings WHERE SettingKey='FilesystemPathPolicy';";
            await command.ExecuteNonQueryAsync();
        }

        var migrated = new SqliteStateStore(paths, caseSensitivePaths: true);
        await migrated.InitializeAsync();

        Assert.Equal(workspace.WorkspaceId, (await migrated.GetActiveWorkspaceAsync())!.WorkspaceId);
        Assert.Equal("Original.md", Assert.Single(await migrated.GetPinsAsync(workspace.WorkspaceId)).RelativePath);
        Assert.Equal("unsaved source", Assert.Single(await migrated.GetRecoverableDraftsAsync()).Text);
        Assert.Equal(AppearancePreference.Dark, await migrated.GetAppearanceAsync());
        Assert.NotEmpty(Directory.GetFiles(paths.BackupDirectory, "axiom-*.db"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new SqliteStateStore(paths, caseSensitivePaths: false).InitializeAsync());
        Assert.Equal(workspace.WorkspaceId, (await migrated.GetActiveWorkspaceAsync())!.WorkspaceId);
    }
}
