using Axiom.Core;
using Axiom.Infrastructure;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Axiom.Infrastructure.Tests;

public sealed class ClassificationTests
{
    [Fact]
    public async Task IdentityLifecycleRetainsMetadataAndBlocksCopiedIdsWithoutMarkdownWrites()
    {
        using var fixture = new MutationTestWorkspace();
        var id = Guid.NewGuid();
        var path = Path.Combine(fixture.Root, "one.md");
        var text = DocumentIdCodec.Enroll("# One", id);
        await File.WriteAllTextAsync(path, text);
        var service = new ClassificationService(new FileDocumentStore(), fixture.State, fixture.State);
        var evidence = await service.DiscoverAsync(fixture.Root);
        await service.ApplyAsync(evidence, [new(new(id, ["Tag"], "Note", ["Alias"]), 0)]);
        Assert.Equal(text, await File.ReadAllTextAsync(path));
        var moved = Path.Combine(fixture.Root, "moved.md");
        File.Move(path, moved);
        Assert.Equal("Alias", Assert.Single((await service.DiscoverAsync(fixture.Root)).Find(id)!.Classification!.Aliases));
        await File.WriteAllTextAsync(path, text);
        var duplicate = await service.DiscoverAsync(fixture.Root);
        Assert.All(duplicate.Documents, document => Assert.False(document.IsEligible));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyAsync(evidence, [new(new(id, ["Wrong"], null, []), 1)]));
        File.Delete(path);
        File.Delete(moved);
        Assert.Null((await service.DiscoverAsync(fixture.Root)).Find(id));
        await fixture.State.ForgetWorkspaceAsync(fixture.Workspace.WorkspaceId);
        Assert.Equal("Tag", Assert.Single(Assert.Single(await fixture.State.GetClassificationsAsync()).Tags));
    }

    [Fact]
    public async Task AtomicBatchRollsBackOnRevisionConflictAndCancellation()
    {
        using var fixture = new MutationTestWorkspace();
        var one = Guid.NewGuid();
        var two = Guid.NewGuid();
        await fixture.State.ApplyClassificationsAsync([new(new(two, ["Original"], null, []), 0)]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.State.ApplyClassificationsAsync([
            new(new(one, ["Must roll back"], null, []), 0), new(new(two, ["Wrong"], null, []), 0)]));
        Assert.Equal(two, Assert.Single(await fixture.State.GetClassificationsAsync()).DocumentId);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.State.ApplyClassificationsAsync([new(new(one, [], null, []), 0)], cancellation.Token));
        Assert.Single(await fixture.State.GetClassificationsAsync());
    }

    [Fact]
    public async Task PortableImportDefaultsToSkipAndRejectsChangedFileEvidence()
    {
        using var fixture = new MutationTestWorkspace();
        var id = Guid.NewGuid();
        var path = Path.Combine(fixture.Root, "one.md");
        await File.WriteAllTextAsync(path, DocumentIdCodec.Enroll("One", id));
        var service = new ClassificationService(new FileDocumentStore(), fixture.State, fixture.State);
        var evidence = await service.DiscoverAsync(fixture.Root);
        var transfer = new MetadataTransfer("axiom-document-metadata", 1, [new(id, ["Imported"], "Idea", ["Alternative"], "one.md")]);
        var exportPath = Path.Combine(fixture.Root, "metadata.json");
        await MetadataTransferService.WriteAsync(exportPath, transfer);
        transfer = await MetadataTransferService.ReadAsync(exportPath);
        var proposal = Assert.Single(MetadataTransferService.Preview(transfer, evidence));
        Assert.False(proposal.Replace);
        await File.AppendAllTextAsync(path, " changed");
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyAsync(evidence, [proposal.Change!]));
        Assert.Empty(await fixture.State.GetClassificationsAsync());
        evidence = await service.DiscoverAsync(fixture.Root);
        await service.ApplyAsync(evidence, [Assert.Single(MetadataTransferService.Preview(transfer, evidence)).Change!]);
        Assert.Null(Assert.Single(MetadataTransferService.Preview(transfer, await service.DiscoverAsync(fixture.Root))).Change);
        Assert.Throws<InvalidDataException>(() => MetadataTransferService.Validate(transfer with { SchemaVersion = 99 }));
        Assert.Throws<InvalidDataException>(() => MetadataTransferService.Validate(transfer with { Records = [transfer.Records[0], transfer.Records[0]] }));
        await Assert.ThrowsAsync<ArgumentException>(() => MetadataTransferService.WriteAsync(path, transfer));
    }

    [Fact]
    public async Task SchemaFiveMigrationMakesVerifiedBackupAndRetainsEveryExistingStore()
    {
        using var fixture = new MutationTestWorkspace();
        var id = Guid.NewGuid();
        await fixture.State.SaveDraftAsync(new("protected", null, "text", DateTimeOffset.UtcNow, null, true));
        await fixture.State.AddPinAsync(fixture.Workspace.WorkspaceId, "one.md", DateTimeOffset.UtcNow);
        await fixture.State.RecordIdentityAsync(id, Path.Combine(fixture.Root, "one.md"));
        await fixture.State.SetAppearanceAsync(AppearancePreference.Dark);
        var session = new WorkspaceDocumentSession(1, "one.md", [new("one.md", 2, 0, 0, 0, 4, 0.5)]);
        await fixture.State.SaveWorkspaceSessionAsync(fixture.Workspace.WorkspaceId, session);
        await fixture.State.SaveLinkIndexAsync(fixture.Workspace.WorkspaceId, new(1, DateTimeOffset.UtcNow, [], []));
        await using (var connection = new SqliteConnection($"Data Source={fixture.State.Paths.DatabasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "DROP TABLE DocumentClassification; UPDATE SchemaInfo SET Version=5;";
            await command.ExecuteNonQueryAsync();
        }
        await fixture.State.InitializeAsync();
        Assert.Equal(6, (await new StateMaintenanceService(fixture.State.Paths).InspectAsync()).SchemaVersion);
        Assert.Single(await fixture.State.GetRecoverableDraftsAsync());
        Assert.Single(await fixture.State.GetPinsAsync(fixture.Workspace.WorkspaceId));
        Assert.Single(await fixture.State.GetIdentitiesAsync());
        Assert.Equal(AppearancePreference.Dark, await fixture.State.GetAppearanceAsync());
        Assert.Equal(session.Documents, (await fixture.State.GetWorkspaceSessionAsync(fixture.Workspace.WorkspaceId))!.Documents);
        Assert.NotNull(await fixture.State.GetLinkIndexAsync(fixture.Workspace.WorkspaceId));
        var backup = Directory.GetFiles(fixture.State.Paths.BackupDirectory, "*.db").Single();
        var restorePaths = new AppDataPaths(Path.Combine(fixture.Root, "restore"));
        restorePaths.EnsureCreated();
        File.Copy(backup, restorePaths.DatabasePath);
        Assert.Equal(5, (await new StateMaintenanceService(restorePaths).InspectAsync()).SchemaVersion);
        Assert.Equal("ok", (await new StateMaintenanceService(restorePaths).InspectAsync()).Integrity);
    }
}
