using Axiom.Core;
using Axiom.Infrastructure;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Axiom.Infrastructure.Tests;

public sealed class RelationshipTests
{
    [Fact]
    public async Task RelationshipsAreDirectedRejectSelfDuplicatesAndRetainMissingEndpoints()
    {
        using var fixture = new MutationTestWorkspace();
        var source = Guid.NewGuid();
        var target = Guid.NewGuid();
        var one = Path.Combine(fixture.Root, "one.md");
        var two = Path.Combine(fixture.Root, "two.md");
        await File.WriteAllTextAsync(one, DocumentIdCodec.Enroll("One", source));
        await File.WriteAllTextAsync(two, DocumentIdCodec.Enroll("Two", target));
        var service = new ClassificationService(new FileDocumentStore(), fixture.State, fixture.State);
        var evidence = await service.DiscoverAsync(fixture.Root);
        var relationship = new DocumentRelationship(Guid.NewGuid(), source, target, " Related ", "a note");
        await service.ApplyConnectedAsync(evidence, [], [new(relationship, 0)]);
        var saved = Assert.Single(await fixture.State.GetRelationshipsAsync());
        Assert.Equal("Related", saved.Kind);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.State.ApplyConnectedMetadataAsync([], [new(relationship with { RelationshipId = Guid.NewGuid(), Kind = "related" }, 0)]));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.State.ApplyConnectedMetadataAsync([], [new(relationship with { RelationshipId = Guid.NewGuid(), TargetId = source }, 0)]));
        await fixture.State.ApplyConnectedMetadataAsync([], [new(relationship with { RelationshipId = Guid.NewGuid(), SourceId = target, TargetId = source }, 0)]);
        File.Delete(two);
        var missing = await service.DiscoverAsync(fixture.Root);
        Assert.Null(missing.Find(target));
        Assert.Equal(2, (await fixture.State.GetRelationshipsAsync()).Count);
        var transfer = MetadataTransferService.Export(missing, await fixture.State.GetRelationshipsAsync());
        Assert.Equal(2, transfer.SchemaVersion);
        Assert.Equal(2, transfer.Relationships!.Length);
        Assert.All(MetadataTransferService.Preview(transfer, missing, []), proposal => Assert.False(proposal.Replace));
        await File.WriteAllTextAsync(two, DocumentIdCodec.Enroll("Returned", target));
        Assert.NotNull((await service.DiscoverAsync(fixture.Root)).Find(target));
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "copied.md"), DocumentIdCodec.Enroll("Copied", target));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyConnectedAsync(evidence, [], [new(saved with { Note = "Ambiguous edit" }, saved.Revision)]));
        await fixture.State.ForgetWorkspaceAsync(fixture.Workspace.WorkspaceId);
        Assert.Equal(2, (await fixture.State.GetRelationshipsAsync()).Count);
        await fixture.State.ApplyConnectedMetadataAsync([], [new(saved, saved.Revision, true)]);
        Assert.Single(await fixture.State.GetRelationshipsAsync());
    }

    [Fact]
    public async Task VersionTwoImportIsAtomicWithClassificationAndVersionOneStillWorks()
    {
        using var fixture = new MutationTestWorkspace();
        var source = Guid.NewGuid();
        var target = Guid.NewGuid();
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "one.md"), DocumentIdCodec.Enroll("One", source));
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "two.md"), DocumentIdCodec.Enroll("Two", target));
        var service = new ClassificationService(new FileDocumentStore(), fixture.State, fixture.State);
        var evidence = await service.DiscoverAsync(fixture.Root);
        var transfer = new MetadataTransfer("axiom-document-metadata", 2, [new(source, ["Portable"], "Note", [])], [new(source, target, "Related", "Portable note")]);
        var proposals = MetadataTransferService.Preview(transfer, evidence);
        var classification = proposals.Single(proposal => proposal.Change is not null).Change!;
        var relation = proposals.Single(proposal => proposal.RelationshipChange is not null).RelationshipChange!;
        await fixture.State.ApplyConnectedMetadataAsync([], [relation]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyConnectedAsync(evidence, [classification], [relation]));
        Assert.Empty(await fixture.State.GetClassificationsAsync());
        var existing = Assert.Single(await fixture.State.GetRelationshipsAsync());
        await service.ApplyConnectedAsync(evidence, [classification], [new(existing with { Note = "Changed" }, existing.Revision)]);
        Assert.Single(await fixture.State.GetClassificationsAsync());
        var path = Path.Combine(fixture.Root, "transfer.json");
        await MetadataTransferService.WriteAsync(path, transfer);
        var read = await MetadataTransferService.ReadAsync(path);
        Assert.Equal("Portable note", Assert.Single(read.Relationships!).Note);
        Assert.Throws<InvalidDataException>(() => MetadataTransferService.Validate(transfer with { SchemaVersion = 1 }));
        var versionOne = transfer with { SchemaVersion = 1, Relationships = null };
        MetadataTransferService.Validate(versionOne);
        Assert.Single(MetadataTransferService.Preview(versionOne, evidence));
        Assert.Throws<InvalidDataException>(() => MetadataTransferService.Validate(transfer with { Relationships = [transfer.Relationships![0], transfer.Relationships[0]] }));
    }

    [Fact]
    public async Task SchemaSixMigrationRetainsClassificationAndVerifiedBackup()
    {
        using var fixture = new MutationTestWorkspace();
        var id = Guid.NewGuid();
        await fixture.State.ApplyClassificationsAsync([new(new(id, ["Preserve"], "Note", ["Alias"]), 0)]);
        await using (var connection = new SqliteConnection($"Data Source={fixture.State.Paths.DatabasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "DROP TABLE DocumentRelationships; UPDATE SchemaInfo SET Version=6;";
            await command.ExecuteNonQueryAsync();
        }
        await fixture.State.InitializeAsync();
        Assert.Equal(7, (await new StateMaintenanceService(fixture.State.Paths).InspectAsync()).SchemaVersion);
        Assert.Equal("Preserve", Assert.Single(Assert.Single(await fixture.State.GetClassificationsAsync()).Tags));
        var backup = Directory.GetFiles(fixture.State.Paths.BackupDirectory, "*.db").Single();
        await using var backedUp = new SqliteConnection($"Data Source={backup};Mode=ReadOnly;Pooling=False");
        await backedUp.OpenAsync();
        await using var check = backedUp.CreateCommand();
        check.CommandText = "SELECT Version FROM SchemaInfo;";
        Assert.Equal(6L, await check.ExecuteScalarAsync());
        check.CommandText = "PRAGMA integrity_check;";
        Assert.Equal("ok", await check.ExecuteScalarAsync());
    }
}
