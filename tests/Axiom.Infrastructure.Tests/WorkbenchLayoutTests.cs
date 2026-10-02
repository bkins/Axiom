using Axiom.Core;
using Axiom.Infrastructure;
using Microsoft.Data.Sqlite;

namespace Axiom.Infrastructure.Tests;

public sealed class WorkbenchLayoutTests
{
    [Fact]
    public async Task WorkbenchRestartRestoresDimensionsAndInvalidInputRetainsPreviousLayout()
    {
        using var fixture = new MutationTestWorkspace();
        var selected = new WorkbenchLayout(310, 380, 3, true);
        await fixture.State.SetWorkbenchLayoutAsync(selected);
        var reopened = new SqliteStateStore(fixture.State.Paths);
        Assert.Equal(selected, await reopened.GetWorkbenchLayoutAsync());
        await Assert.ThrowsAsync<ArgumentException>(() => reopened.SetWorkbenchLayoutAsync(selected with { WorkspaceWidth = double.NaN }));
        await Assert.ThrowsAsync<ArgumentException>(() => reopened.SetWorkbenchLayoutAsync(selected with { ToolIndex = 6 }));
        Assert.Equal(selected, await reopened.GetWorkbenchLayoutAsync());
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("{\"WorkspaceWidth\":1,\"ToolWidth\":320,\"ToolIndex\":1}")]
    [InlineData("null")]
    public async Task CorruptWorkbenchPreferenceUsesSafeDefaultWithoutChangingDocumentState(string json)
    {
        using var fixture = new MutationTestWorkspace();
        await using var connection = new SqliteConnection($"Data Source={fixture.State.Paths.DatabasePath};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO AppSettings(SettingKey,SettingValue) VALUES('WorkbenchLayout',$json) ON CONFLICT(SettingKey) DO UPDATE SET SettingValue=$json;";
        command.Parameters.AddWithValue("$json", json);
        await command.ExecuteNonQueryAsync();
        Assert.Equal(new WorkbenchLayout(), await fixture.State.GetWorkbenchLayoutAsync());
    }
}
