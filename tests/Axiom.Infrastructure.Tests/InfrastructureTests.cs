using Axiom.Core;
using Axiom.Infrastructure;
using Microsoft.Data.Sqlite;

namespace Axiom.Infrastructure.Tests;

public sealed class InfrastructureTests
{
    [Fact]
    public async Task FileStoreListsBothMarkdownExtensionsAndPreservesOpenContent()
    {
        using var temp = new TempDirectory();
        var md = Path.Combine(temp.Path, "a.md");
        var markdown = Path.Combine(temp.Path, "nested", "b.markdown");
        Directory.CreateDirectory(Path.GetDirectoryName(markdown)!);
        await File.WriteAllTextAsync(md, "one");
        await File.WriteAllTextAsync(markdown, "two");
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "skip.txt"), "skip");
        var store = new FileDocumentStore();

        var paths = await store.ListMarkdownFilesAsync(temp.Path);
        var opened = await store.ReadAsync(md);

        Assert.Equal(2, paths.Files.Count);
        Assert.Equal("one", opened.Text);
        Assert.Equal("one", await File.ReadAllTextAsync(md));
    }

    [Fact]
    public async Task FileStoreWritesUtf8WithoutBomUsingReplacement()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "a.md");
        await File.WriteAllTextAsync(path, "old");
        var store = new FileDocumentStore();

        await store.WriteAsync(path, "new ✓");

        Assert.Equal("new ✓", await File.ReadAllTextAsync(path));
        Assert.False(File.ReadAllBytes(path).Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }));
        Assert.Empty(Directory.EnumerateFiles(temp.Path, "*.tmp"));
    }

    [Fact]
    public async Task SqliteRecoveryRoundTripsUnsavedTextAndUsesDurablePragmas()
    {
        using var temp = new TempDirectory();
        var paths = new AppDataPaths(temp.Path);
        var store = new SqliteStateStore(paths);
        await store.InitializeAsync();
        var draft = new RecoveryDraft("draft", null, "protected text", DateTimeOffset.UtcNow, null, true);

        await store.SaveDraftAsync(draft);
        var recovered = await store.GetRecoverableDraftsAsync();

        Assert.Equal("protected text", Assert.Single(recovered).Text);
        await using var connection = new SqliteConnection($"Data Source={paths.DatabasePath};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA synchronous;";
        Assert.Equal(2L, (long)(await command.ExecuteScalarAsync())!);
        command.CommandText = "PRAGMA journal_mode;";
        Assert.Equal("wal", ((string)(await command.ExecuteScalarAsync())!).ToLowerInvariant());
    }

    [Fact]
    public async Task InitializationBacksUpExistingStateBeforeMigrationWork()
    {
        using var temp = new TempDirectory();
        var paths = new AppDataPaths(temp.Path);
        var store = new SqliteStateStore(paths);
        await store.InitializeAsync();
        await store.SaveDraftAsync(new RecoveryDraft("draft", null, "text", DateTimeOffset.UtcNow, null, true));

        await store.InitializeAsync();

        Assert.NotEmpty(Directory.EnumerateFiles(paths.BackupDirectory, "axiom-*.db"));
        Assert.Single(await store.GetRecoverableDraftsAsync());
    }

    [Fact]
    public async Task IdentityMetadataTracksMultipleCopiedPaths()
    {
        using var temp = new TempDirectory();
        var store = new SqliteStateStore(new AppDataPaths(temp.Path));
        await store.InitializeAsync();
        var id = Guid.NewGuid();

        await store.RecordIdentityAsync(id, Path.Combine(temp.Path, "a.md"));
        await store.RecordIdentityAsync(id, Path.Combine(temp.Path, "copy.md"));

        Assert.Equal(2, (await store.GetIdentitiesAsync()).Count);
    }

    [Fact]
    public async Task WorkspacePersistenceReusesNormalizedPathAndRetainsOneActiveWorkspace()
    {
        using var temp = new TempDirectory();
        var paths = new AppDataPaths(Path.Combine(temp.Path, "state"));
        var store = new SqliteStateStore(paths);
        await store.InitializeAsync();
        var service = new WorkspaceService(store);
        var workspaceRoot = Path.Combine(temp.Path, "Docs");
        Directory.CreateDirectory(workspaceRoot);
        var firstOpened = new DateTimeOffset(2026, 9, 26, 10, 0, 0, TimeSpan.Zero);
        var secondOpened = firstOpened.AddHours(1);

        var first = await service.OpenAsync(workspaceRoot + Path.DirectorySeparatorChar, firstOpened);
        var second = await service.OpenAsync(workspaceRoot, secondOpened);
        var reopenedStore = new SqliteStateStore(paths);
        await reopenedStore.InitializeAsync();
        var active = await reopenedStore.GetActiveWorkspaceAsync();

        Assert.Equal(first.WorkspaceId, second.WorkspaceId);
        Assert.Equal(first.CreatedUtc, second.CreatedUtc);
        Assert.Equal(secondOpened, second.LastOpenedUtc);
        Assert.Equal(second, active);
    }

    [Fact]
    public async Task WorkspaceAdoptionDoesNotModifyMarkdown()
    {
        using var temp = new TempDirectory();
        var markdownPath = Path.Combine(temp.Path, "ordinary.md");
        var originalBytes = "# Ordinary\n\nNo Axiom metadata."u8.ToArray();
        await File.WriteAllBytesAsync(markdownPath, originalBytes);
        var state = new SqliteStateStore(new AppDataPaths(Path.Combine(temp.Path, "state")));
        await state.InitializeAsync();

        await new FileDocumentStore().ListMarkdownFilesAsync(temp.Path);
        await new WorkspaceService(state).OpenAsync(temp.Path);

        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(markdownPath));
    }

    [Fact]
    public async Task VersionOneDatabaseMigratesToWorkspaceSchemaWithoutLosingDrafts()
    {
        using var temp = new TempDirectory();
        var paths = new AppDataPaths(temp.Path);
        paths.EnsureCreated();
        await using (var connection = new SqliteConnection($"Data Source={paths.DatabasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE SchemaInfo (Version INTEGER NOT NULL);
                INSERT INTO SchemaInfo(Version) VALUES(1);
                CREATE TABLE RecoveryDrafts (
                    DraftKey TEXT PRIMARY KEY,
                    FilePath TEXT NULL,
                    TextContent TEXT NOT NULL,
                    UpdatedUtc TEXT NOT NULL,
                    SavedFileWriteUtc TEXT NULL,
                    IsNewDocument INTEGER NOT NULL
                );
                INSERT INTO RecoveryDrafts VALUES('draft', NULL, 'preserved', '2026-09-26T10:00:00.0000000+00:00', NULL, 1);
                """;
            await command.ExecuteNonQueryAsync();
        }

        var store = new SqliteStateStore(paths);
        await store.InitializeAsync();

        Assert.Equal("preserved", Assert.Single(await store.GetRecoverableDraftsAsync()).Text);
        await using var verify = new SqliteConnection($"Data Source={paths.DatabasePath};Pooling=False");
        await verify.OpenAsync();
        await using var versionCommand = verify.CreateCommand();
        versionCommand.CommandText = "SELECT Version FROM SchemaInfo;";
        Assert.Equal(SqliteStateStore.CurrentSchemaVersion, Convert.ToInt32(await versionCommand.ExecuteScalarAsync()));
        versionCommand.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('Workspaces', 'ActiveWorkspace');";
        Assert.Equal(2, Convert.ToInt32(await versionCommand.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task WorkspacePinsPersistPerWorkspaceAndCanBeRemoved()
    {
        using var temp = new TempDirectory();
        var paths = new AppDataPaths(Path.Combine(temp.Path, "state"));
        var store = new SqliteStateStore(paths);
        await store.InitializeAsync();
        var service = new WorkspaceService(store);
        var firstRoot = Path.Combine(temp.Path, "First");
        var secondRoot = Path.Combine(temp.Path, "Second");
        Directory.CreateDirectory(firstRoot);
        Directory.CreateDirectory(secondRoot);
        var first = await service.OpenAsync(firstRoot);
        var second = await service.OpenAsync(secondRoot);

        await service.PinAsync(first, Path.Combine(firstRoot, "note.md"));
        await service.PinAsync(second, Path.Combine(secondRoot, "other.markdown"));
        var reopened = new WorkspaceService(new SqliteStateStore(paths));
        var firstPins = await reopened.GetPinsAsync(first);
        var secondPins = await reopened.GetPinsAsync(second);
        await reopened.RemovePinAsync(first, Assert.Single(firstPins));

        Assert.Equal("note.md", Assert.Single(firstPins).RelativePath);
        Assert.Equal("other.markdown", Assert.Single(secondPins).RelativePath);
        Assert.Empty(await reopened.GetPinsAsync(first));
    }

    [Fact]
    public async Task AppearancePreferencePersistsAndUnknownValuesFallBackToSystem()
    {
        using var temp = new TempDirectory();
        var paths = new AppDataPaths(temp.Path);
        var store = new SqliteStateStore(paths);
        await store.InitializeAsync();

        Assert.Equal(AppearancePreference.System, await store.GetAppearanceAsync());
        await store.SetAppearanceAsync(AppearancePreference.Dark);
        Assert.Equal(AppearancePreference.Dark, await new SqliteStateStore(paths).GetAppearanceAsync());

        await using var connection = new SqliteConnection($"Data Source={paths.DatabasePath};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE AppSettings SET SettingValue='Sepia' WHERE SettingKey='Appearance';";
        await command.ExecuteNonQueryAsync();

        Assert.Equal(AppearancePreference.System, await new SqliteStateStore(paths).GetAppearanceAsync());
    }

    [Fact]
    public async Task VersionThreeMigrationPreservesDraftWorkspaceAndPinState()
    {
        using var temp = new TempDirectory();
        var paths = new AppDataPaths(Path.Combine(temp.Path, "state"));
        var store = new SqliteStateStore(paths);
        await store.InitializeAsync();
        var root = Path.Combine(temp.Path, "Workspace");
        Directory.CreateDirectory(root);
        var workspace = await new WorkspaceService(store).OpenAsync(root);
        await store.AddPinAsync(workspace.WorkspaceId, "note.md", DateTimeOffset.UtcNow);
        await store.SaveDraftAsync(new RecoveryDraft("draft", null, "preserved", DateTimeOffset.UtcNow, null, true));

        await using (var connection = new SqliteConnection($"Data Source={paths.DatabasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "DROP TABLE AppSettings; UPDATE SchemaInfo SET Version=3;";
            await command.ExecuteNonQueryAsync();
        }

        var migrated = new SqliteStateStore(paths);
        await migrated.InitializeAsync();

        Assert.Equal(AppearancePreference.System, await migrated.GetAppearanceAsync());
        Assert.Equal("preserved", Assert.Single(await migrated.GetRecoverableDraftsAsync()).Text);
        Assert.Equal(workspace.WorkspaceId, (await migrated.GetActiveWorkspaceAsync())!.WorkspaceId);
        Assert.Equal("note.md", Assert.Single(await migrated.GetPinsAsync(workspace.WorkspaceId)).RelativePath);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "AxiomTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }
        public string Path { get; }
        public void Dispose() => Directory.Delete(Path, true);
    }
}
