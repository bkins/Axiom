using System.Text;
using Axiom.Core;

namespace Axiom.Infrastructure.Tests;

public sealed class DeletedDocumentTests
{
    [Fact]
    public async Task CaptureFailureAndChangedSourcePreventDeletion()
    {
        using var fixture = new MutationTestWorkspace();
        var path = Path.Combine(fixture.Root, "a.md");
        await File.WriteAllTextAsync(path, "original");
        var store = new FaultingMutationStore(fixture.State) { FailCapture = true };
        var operations = new WorkspaceFileOperations(store, new NonMarkdownRecycleBin(fixture.Root));
        await Assert.ThrowsAsync<IOException>(() => operations.ExecuteAsync(fixture.Workspace, DocumentMutationKind.Recycle, "a.md", null));
        Assert.Equal("original", await File.ReadAllTextAsync(path));
        Assert.Empty(await fixture.State.GetDeletedDocumentsAsync(fixture.Workspace.WorkspaceId));
        store.FailCapture = false;
        store.AfterPrepare = _ => File.WriteAllText(path, "newer external bytes");
        await Assert.ThrowsAsync<IOException>(() => operations.ExecuteAsync(fixture.Workspace, DocumentMutationKind.Recycle, "a.md", null));
        Assert.Equal("newer external bytes", await File.ReadAllTextAsync(path));
        Assert.Equal("Cancelled", Assert.Single(await fixture.State.GetDeletedDocumentsAsync(fixture.Workspace.WorkspaceId)).Status);
    }

    [Fact]
    public async Task CompletedDeletionAndRestoreReconcileAfterStateFailuresWithoutRepeatingFiles()
    {
        using var fixture = new MutationTestWorkspace();
        var path = Path.Combine(fixture.Root, "a.md");
        await File.WriteAllTextAsync(path, "exact bytes");
        var store = new FaultingMutationStore(fixture.State) { FailReconciliation = true };
        var operations = new WorkspaceFileOperations(store, new NonMarkdownRecycleBin(fixture.Root));
        await Assert.ThrowsAsync<IOException>(() => operations.ExecuteAsync(fixture.Workspace, DocumentMutationKind.Recycle, "a.md", null));
        Assert.False(File.Exists(path));
        var item = Assert.Single(await fixture.State.GetDeletedDocumentsAsync(fixture.Workspace.WorkspaceId));
        Assert.Equal("Prepared", item.Status);
        await new WorkspaceFileOperations(fixture.State).ReconcilePendingAsync();
        await Assert.ThrowsAsync<IOException>(() => operations.RestoreAsync(fixture.Workspace, item.Intent.OperationId));
        Assert.Equal("exact bytes", await File.ReadAllTextAsync(path));
        Assert.Equal("Restoring", Assert.Single(await fixture.State.GetDeletedDocumentsAsync(fixture.Workspace.WorkspaceId)).Status);
        Assert.Single(await new WorkspaceFileOperations(fixture.State).ReconcilePendingAsync());
        Assert.Equal("Restored", Assert.Single(await fixture.State.GetDeletedDocumentsAsync(fixture.Workspace.WorkspaceId)).Status);
        Assert.Empty(await fixture.State.GetPendingMutationsAsync());
    }

    [Fact]
    public async Task DestinationCollisionAfterPrepareRetainsOtherFileAndReturnsRecoveryToAvailable()
    {
        using var fixture = new MutationTestWorkspace();
        var path = Path.Combine(fixture.Root, "a.md");
        await File.WriteAllTextAsync(path, "original");
        await new WorkspaceFileOperations(fixture.State, new NonMarkdownRecycleBin(fixture.Root)).ExecuteAsync(fixture.Workspace, DocumentMutationKind.Recycle, "a.md", null);
        var item = Assert.Single(await fixture.State.GetDeletedDocumentsAsync(fixture.Workspace.WorkspaceId));
        var store = new FaultingMutationStore(fixture.State) { AfterPrepare = intent => File.WriteAllText(intent.DestinationPath!, "other writer") };
        await Assert.ThrowsAsync<IOException>(() => new WorkspaceFileOperations(store).RestoreAsync(fixture.Workspace, item.Intent.OperationId));
        Assert.Equal("other writer", await File.ReadAllTextAsync(path));
        Assert.Equal("Available", Assert.Single(await fixture.State.GetDeletedDocumentsAsync(fixture.Workspace.WorkspaceId)).Status);
        Assert.Empty(await fixture.State.GetPendingMutationsAsync());
    }

    [Fact]
    public async Task RestoreReconstructsRecordedParentsAndPreservesBytesIdentityAndPin()
    {
        using var fixture = new MutationTestWorkspace();
        var folder = Path.Combine(fixture.Root, "parent", "child");
        Directory.CreateDirectory(folder);
        var identity = Guid.NewGuid();
        var content = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes($"<!-- axiom:documentId={identity:D} -->\r\n# Café\r\n")).ToArray();
        var source = Path.Combine(folder, "deleted.md");
        await File.WriteAllBytesAsync(source, content);
        await fixture.State.AddPinAsync(fixture.Workspace.WorkspaceId, Path.GetRelativePath(fixture.Root, source), DateTimeOffset.UtcNow);
        var operations = new WorkspaceFileOperations(fixture.State, new NonMarkdownRecycleBin(fixture.Root));
        await operations.ExecuteAsync(fixture.Workspace, DocumentMutationKind.Recycle, "parent/child/deleted.md", null);
        Directory.Delete(folder);
        Directory.Delete(Path.GetDirectoryName(folder)!);
        var item = Assert.Single(await fixture.State.GetDeletedDocumentsAsync(fixture.Workspace.WorkspaceId));
        var restored = await operations.RestoreAsync(fixture.Workspace, item.Intent.OperationId);
        Assert.Equal(source, restored);
        Assert.Equal(content, await File.ReadAllBytesAsync(restored));
        Assert.Equal(identity, DocumentIdCodec.Find(await File.ReadAllTextAsync(restored)));
        Assert.Equal(Path.GetRelativePath(fixture.Root, source), Assert.Single(await fixture.State.GetPinsAsync(fixture.Workspace.WorkspaceId)).RelativePath);
        Assert.Equal("Restored", Assert.Single(await fixture.State.GetDeletedDocumentsAsync(fixture.Workspace.WorkspaceId)).Status);
        Assert.Empty(await fixture.State.GetPendingMutationsAsync());
        await Assert.ThrowsAsync<IOException>(() => operations.RestoreAsync(fixture.Workspace, item.Intent.OperationId));
    }

    [Fact]
    public async Task ExistingPathAndIdentityRequireExplicitAlternativeWithoutOverwriting()
    {
        using var fixture = new MutationTestWorkspace();
        var identity = Guid.NewGuid();
        var text = $"<!-- axiom:documentId={identity:D} -->\n# deleted\n";
        var source = Path.Combine(fixture.Root, "deleted.md");
        await File.WriteAllTextAsync(source, text);
        var operations = new WorkspaceFileOperations(fixture.State, new NonMarkdownRecycleBin(fixture.Root));
        await operations.ExecuteAsync(fixture.Workspace, DocumentMutationKind.Recycle, "deleted.md", null);
        var item = Assert.Single(await fixture.State.GetDeletedDocumentsAsync(fixture.Workspace.WorkspaceId));
        await File.WriteAllTextAsync(source, text);
        await Assert.ThrowsAsync<IOException>(() => operations.RestoreAsync(fixture.Workspace, item.Intent.OperationId));
        await Assert.ThrowsAsync<IOException>(() => operations.RestoreAsync(fixture.Workspace, item.Intent.OperationId, "alternative.md"));
        var path = await operations.RestoreAsync(fixture.Workspace, item.Intent.OperationId, "alternative.md", true);
        var restored = await File.ReadAllTextAsync(path);
        Assert.NotEqual(identity, DocumentIdCodec.Find(restored));
        Assert.Equal(text, await File.ReadAllTextAsync(source));
    }

    [Fact]
    public async Task CaptureSurvivesRestartAndNewerMetadataRemainsAuthoritative()
    {
        using var fixture = new MutationTestWorkspace();
        var identity = Guid.NewGuid();
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "deleted.md"), $"<!-- axiom:documentId={identity:D} -->\nold");
        await fixture.State.ApplyClassificationsAsync([new(new(identity, ["Old"], null, []), 0)]);
        var operations = new WorkspaceFileOperations(fixture.State, new NonMarkdownRecycleBin(fixture.Root));
        await operations.ExecuteAsync(fixture.Workspace, DocumentMutationKind.Recycle, "deleted.md", null);
        await fixture.State.ApplyClassificationsAsync([new(new(identity, ["New"], null, []), 1)]);
        var restarted = new SqliteStateStore(fixture.State.Paths);
        await restarted.InitializeAsync();
        var item = Assert.Single(await restarted.GetDeletedDocumentsAsync(fixture.Workspace.WorkspaceId));
        Assert.Contains("Old", item.MetadataSnapshot);
        await new WorkspaceFileOperations(restarted).RestoreAsync(fixture.Workspace, item.Intent.OperationId);
        Assert.Equal("New", Assert.Single(Assert.Single(await restarted.GetClassificationsAsync()).Tags));
    }
}
