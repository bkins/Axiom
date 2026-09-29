using Axiom.Core;
using Axiom.Infrastructure;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Axiom.Infrastructure.Tests;

public sealed class WorkspaceSessionStoreTests
{
    [Fact]
    public async Task SessionsRoundTripPerWorkspaceAndForgetRemovesOnlyItsRememberedState()
    {
        using var fixture = new MutationTestWorkspace();
        var other = await fixture.State.OpenWorkspaceAsync(Path.Combine(fixture.Root, "other"), "Other", DateTimeOffset.UtcNow);
        var first = Snapshot("one.md");
        var second = Snapshot("two.md");
        await fixture.State.SaveWorkspaceSessionAsync(fixture.Workspace.WorkspaceId, first);
        await fixture.State.SaveWorkspaceSessionAsync(other.WorkspaceId, second);
        var reopened = new SqliteStateStore(fixture.State.Paths);
        await reopened.InitializeAsync();
        Assert.Equal(first.Documents, (await reopened.GetWorkspaceSessionAsync(fixture.Workspace.WorkspaceId))!.Documents);
        await reopened.ForgetWorkspaceAsync(fixture.Workspace.WorkspaceId);
        Assert.Null(await reopened.GetWorkspaceSessionAsync(fixture.Workspace.WorkspaceId));
        Assert.Equal(second.Documents, (await reopened.GetWorkspaceSessionAsync(other.WorkspaceId))!.Documents);
    }

    [Fact]
    public async Task SchemaFiveStateAndVerifiedBackupRetainSessionsDraftsPinsIdentityAndAppearance()
    {
        using var fixture = new MutationTestWorkspace();
        var snapshot = Snapshot("ordinary.md");
        await fixture.State.SaveWorkspaceSessionAsync(fixture.Workspace.WorkspaceId, snapshot);
        await fixture.State.SaveDraftAsync(new RecoveryDraft("protected", null, "untitled text", DateTimeOffset.UtcNow, null, true));
        await fixture.State.AddPinAsync(fixture.Workspace.WorkspaceId, "ordinary.md", DateTimeOffset.UtcNow);
        await fixture.State.RecordIdentityAsync(Guid.NewGuid(), Path.Combine(fixture.Root, "enrolled.md"));
        await fixture.State.SetAppearanceAsync(AppearancePreference.Dark);
        var service = new StateMaintenanceService(fixture.State.Paths);
        Assert.Equal(5, (await service.InspectAsync()).SchemaVersion);
        var backup = await service.ExportBackupAsync(Path.Combine(fixture.Root, "backup.db"));
        var isolated = new AppDataPaths(Path.Combine(fixture.Root, "restored"));
        isolated.EnsureCreated();
        File.Copy(backup, isolated.DatabasePath);
        var restored = new SqliteStateStore(isolated);
        await restored.InitializeAsync();
        Assert.Equal(snapshot.Documents, (await restored.GetWorkspaceSessionAsync(fixture.Workspace.WorkspaceId))!.Documents);
        Assert.Equal("untitled text", Assert.Single(await restored.GetRecoverableDraftsAsync()).Text);
        Assert.Single(await restored.GetPinsAsync(fixture.Workspace.WorkspaceId));
        Assert.Single(await restored.GetIdentitiesAsync());
        Assert.Equal(AppearancePreference.Dark, await restored.GetAppearanceAsync());
        Assert.Equal("ok", (await new StateMaintenanceService(isolated).InspectAsync()).Integrity);
    }

    [Fact]
    public async Task UnsupportedSessionFormatReportsAnErrorAndDoesNotChangeTheStoredRecord()
    {
        using var fixture = new MutationTestWorkspace();
        await using var connection = new SqliteConnection($"Data Source={fixture.State.Paths.DatabasePath};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO AppSettings VALUES($key, $value);";
        command.Parameters.AddWithValue("$key", $"WorkspaceSession:{fixture.Workspace.WorkspaceId:D}");
        command.Parameters.AddWithValue("$value", "{\"FormatVersion\":2,\"Documents\":[]}");
        await command.ExecuteNonQueryAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.State.GetWorkspaceSessionAsync(fixture.Workspace.WorkspaceId));
        command.CommandText = "SELECT SettingValue FROM AppSettings WHERE SettingKey=$key;";
        Assert.Equal("{\"FormatVersion\":2,\"Documents\":[]}", await command.ExecuteScalarAsync());
    }

    private static WorkspaceDocumentSession Snapshot(string path) =>
        new(1, path, [new WorkspaceDocumentLocation(path, 8, 3, 5, 2, 600, 0.45)]);
}
