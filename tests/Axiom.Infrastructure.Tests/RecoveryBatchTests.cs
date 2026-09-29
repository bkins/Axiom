using Axiom.Core;
using Axiom.Infrastructure;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Axiom.Infrastructure.Tests;

public sealed class RecoveryBatchTests
{
    [Fact]
    public async Task BatchPersistsAllFiveBuffersAndDeletesCleanDraftsTogether()
    {
        using var fixture = new MutationTestWorkspace();
        await fixture.State.SaveDraftAsync(Draft("clean", "previous"));
        var snapshots = Enumerable.Range(1, 5).Select(index => Draft($"buffer-{index}", $"revision-{index}")).ToArray();
        await fixture.State.ApplyDraftsAsync(snapshots, ["clean"]);
        var reopened = new SqliteStateStore(fixture.State.Paths);
        await reopened.InitializeAsync();
        var recovered = await reopened.GetRecoverableDraftsAsync();
        Assert.Equal(5, recovered.Count);
        foreach (var snapshot in snapshots) Assert.Contains(snapshot, recovered);
    }

    [Fact]
    public async Task FailedBatchRetainsEveryPreviousRevisionAndCleanDraft()
    {
        using var fixture = new MutationTestWorkspace();
        await fixture.State.ApplyDraftsAsync([Draft("first", "previous"), Draft("clean", "protected")], []);
        await using var connection = new SqliteConnection($"Data Source={fixture.State.Paths.DatabasePath};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TRIGGER RejectSecond BEFORE INSERT ON RecoveryDrafts
            WHEN NEW.DraftKey='second' BEGIN SELECT RAISE(ABORT, 'injected failure'); END;
            """;
        await command.ExecuteNonQueryAsync();
        await Assert.ThrowsAsync<SqliteException>(() => fixture.State.ApplyDraftsAsync(
            [Draft("first", "new"), Draft("second", "new")], ["clean"]));
        var recovered = await fixture.State.GetRecoverableDraftsAsync();
        Assert.Equal(2, recovered.Count);
        Assert.Equal("previous", recovered.Single(item => item.DraftKey == "first").Text);
        Assert.Equal("protected", recovered.Single(item => item.DraftKey == "clean").Text);
    }

    private static RecoveryDraft Draft(string key, string text) => new(key, null, text, DateTimeOffset.UtcNow, null, true);
}
