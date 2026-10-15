using Axiom.Core;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Axiom.Infrastructure.Tests;

public sealed class StateRestoreTests
{
    [Fact]
    public async Task RestoreRetainsRollbackAndExactProtectedDraftsWithoutTouchingMarkdown()
    {
        using var fixture = new MutationTestWorkspace();
        var sourcePaths = new AppDataPaths(Path.Combine(fixture.Root, "backup-source"));
        var source = new SqliteStateStore(sourcePaths); await source.InitializeAsync();
        await using var walOwner = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = sourcePaths.DatabasePath, Pooling = false }.ToString());
        await walOwner.OpenAsync();
        using (var command = walOwner.CreateCommand())
        { command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA wal_autocheckpoint=0;"; command.ExecuteNonQuery(); }
        await source.SaveDraftAsync(new("backup-draft", null, "saved in backup", DateTimeOffset.UtcNow, null, true));
        Assert.True(File.Exists(sourcePaths.DatabasePath + "-wal"));
        await fixture.State.SaveDraftAsync(new("current-draft", null, "keep in rollback", DateTimeOffset.UtcNow, null, true));
        var markdown = Path.Combine(fixture.Root, "unchanged.md"); await File.WriteAllTextAsync(markdown, "unchanged");
        var service = new StateRestoreService(fixture.State.Paths);
        var review = await service.ReviewAsync(sourcePaths.DatabasePath);
        Assert.Equal(1, review.Inspection.Drafts);
        var rollback = await Task.Run(() =>
        {
            using var owner = new SingleInstanceCoordinator(fixture.State.Paths.Root);
            Assert.True(owner.IsPrimary);
            return service.RestoreOfflineAsync(review.Snapshot, review.Hash).GetAwaiter().GetResult();
        });
        Assert.Equal("saved in backup", Assert.Single(await fixture.State.GetRecoverableDraftsAsync()).Text);
        var rollbackReview = await service.ReviewAsync(rollback);
        Assert.Equal(1, rollbackReview.Inspection.Drafts);
        Assert.Equal("unchanged", await File.ReadAllTextAsync(markdown));
    }

    [Fact]
    public async Task CorruptNewerAndChangedBackupsAreRefusedWithoutReplacingCurrentState()
    {
        using var fixture = new MutationTestWorkspace();
        var service = new StateRestoreService(fixture.State.Paths);
        var corrupt = Path.Combine(fixture.Root, "corrupt.db"); await File.WriteAllTextAsync(corrupt, "not sqlite");
        await Assert.ThrowsAnyAsync<Exception>(() => service.ReviewAsync(corrupt));
        var backup = Path.Combine(fixture.Root, "backup.db");
        await new StateMaintenanceService(fixture.State.Paths).ExportBackupAsync(backup);
        var review = await service.ReviewAsync(backup);
        await File.AppendAllTextAsync(review.Snapshot, "changed");
        await Assert.ThrowsAsync<IOException>(() => service.RestoreOfflineAsync(review.Snapshot, review.Hash));
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = backup, Pooling = false }.ToString()))
        { connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "UPDATE SchemaInfo SET Version=999;"; command.ExecuteNonQuery(); }
        await Assert.ThrowsAsync<IOException>(() => service.ReviewAsync(backup));
        Assert.Equal(SqliteStateStore.CurrentSchemaVersion, (await new StateMaintenanceService(fixture.State.Paths).InspectAsync()).SchemaVersion);
    }
}
