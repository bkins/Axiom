using System.Text;
using Axiom.Core;
using Axiom.Infrastructure;

namespace Axiom.Infrastructure.Tests;

public sealed class TemplateImportTests
{
    [Fact]
    public async Task ReviewedTemplateCreationUsesDurableIntentAndProtectsExistingFiles()
    {
        using var fixture = new MutationTestWorkspace();
        var source = $"<!-- axiom:documentId={Guid.NewGuid():D} -->\r\n# {{{{title}}}}\n";
        var expanded = DocumentTemplateService.Expand(source, "Café", new DateOnly(2026, 10, 1));
        var operations = new WorkspaceFileOperations(fixture.State);
        await operations.ExecuteAsync(fixture.Workspace, DocumentMutationKind.Create, null, "new.md", creationText: expanded);
        Assert.Equal(expanded, await File.ReadAllTextAsync(Path.Combine(fixture.Root, "new.md")));
        Assert.Empty(await fixture.State.GetIdentitiesAsync());
        await Assert.ThrowsAsync<IOException>(() => operations.ExecuteAsync(fixture.Workspace, DocumentMutationKind.Create, null, "new.md", creationText: "replacement"));
        await Assert.ThrowsAsync<ArgumentException>(() => operations.ExecuteAsync(fixture.Workspace, DocumentMutationKind.Create, null, "../escape.md", creationText: expanded));
        await Assert.ThrowsAsync<ArgumentException>(() => operations.ExecuteAsync(fixture.Workspace, DocumentMutationKind.Create, null, "identity.md", creationText: source));
        Assert.Empty(await fixture.State.GetPendingMutationsAsync());
        Assert.Empty(Directory.GetFiles(fixture.Root, ".axiom-*.tmp"));
    }

    [Fact]
    public async Task MarkdownAndImageCopiesPreserveOriginalsAndAvoidDuplicateIdentity()
    {
        using var fixture = new MutationTestWorkspace();
        var source = Path.Combine(fixture.Root, "source.md");
        var originalIdentity = Guid.NewGuid();
        var original = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes($"<!-- axiom:documentId={originalIdentity:D} -->\r\n# Café\r\n")).ToArray();
        await File.WriteAllBytesAsync(source, original);
        var service = new WorkspaceImportService(new WorkspaceFileOperations(fixture.State));
        var reviewed = await service.ReviewAsync(source, false);
        await service.CopyReviewedAsync(fixture.Workspace, "copy.md", reviewed, false);
        var copied = await File.ReadAllBytesAsync(Path.Combine(fixture.Root, "copy.md"));
        Assert.NotEqual(originalIdentity, DocumentIdCodec.Find(MarkdownIdentityBytes.Decode(copied)));
        Assert.Equal(original, MarkdownIdentityBytes.Assign(copied, originalIdentity, true));
        Assert.Equal(original, await File.ReadAllBytesAsync(source));
        Assert.Single(await fixture.State.GetIdentitiesAsync());
        var ordinary = Encoding.UTF8.GetBytes("# Ordinary\n");
        await service.CopyReviewedAsync(fixture.Workspace, "ordinary.md", ordinary, false);
        Assert.Equal(ordinary, await File.ReadAllBytesAsync(Path.Combine(fixture.Root, "ordinary.md")));
        var image = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
        await service.CopyReviewedAsync(fixture.Workspace, "image.png", image, true);
        Assert.Equal(image, await File.ReadAllBytesAsync(Path.Combine(fixture.Root, "image.png")));
        await Assert.ThrowsAsync<IOException>(() => service.CopyReviewedAsync(fixture.Workspace, "image.png", [1], true));
        Assert.Equal(image, await File.ReadAllBytesAsync(Path.Combine(fixture.Root, "image.png")));
    }

    [Fact]
    public async Task ImportCancellationAndStateFailureLeaveRecoverableFilesystemEvidence()
    {
        using var fixture = new MutationTestWorkspace();
        using var cancellation = new CancellationTokenSource();
        var cancelStore = new FaultingMutationStore(fixture.State) { AfterPrepare = _ => cancellation.Cancel() };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new WorkspaceFileOperations(cancelStore).ExecuteAsync(
            fixture.Workspace, DocumentMutationKind.ImportAsset, null, "cancel.png", cancellationToken: cancellation.Token, importContent: [1, 2]));
        Assert.False(File.Exists(Path.Combine(fixture.Root, "cancel.png")));
        Assert.Empty(await fixture.State.GetPendingMutationsAsync());
        var failStore = new FaultingMutationStore(fixture.State) { FailReconciliation = true };
        await Assert.ThrowsAsync<IOException>(() => new WorkspaceFileOperations(failStore).ExecuteAsync(
            fixture.Workspace, DocumentMutationKind.Create, null, "new.md", creationText: "# Reviewed"));
        Assert.Equal("# Reviewed", await File.ReadAllTextAsync(Path.Combine(fixture.Root, "new.md")));
        Assert.Single(await fixture.State.GetPendingMutationsAsync());
        Assert.Single(await new WorkspaceFileOperations(fixture.State).ReconcilePendingAsync());
        Assert.Empty(await fixture.State.GetPendingMutationsAsync());
        Assert.Empty(Directory.GetFiles(fixture.Root, ".axiom-*.tmp"));
    }

    [Fact]
    public async Task ImportSnapshotHasLimitsAndSurvivesSourceChanges()
    {
        using var fixture = new MutationTestWorkspace();
        var service = new WorkspaceImportService(new WorkspaceFileOperations(fixture.State));
        var source = Path.Combine(fixture.Root, "input.md");
        await File.WriteAllTextAsync(source, "reviewed");
        var bytes = await service.ReviewAsync(source, false);
        await File.WriteAllTextAsync(source, "changed after review");
        await service.CopyReviewedAsync(fixture.Workspace, "copy.md", bytes, false);
        Assert.Equal("reviewed", await File.ReadAllTextAsync(Path.Combine(fixture.Root, "copy.md")));
        Assert.Equal("changed after review", await File.ReadAllTextAsync(source));
        await using (var stream = File.Create(source)) stream.SetLength(4 * 1024 * 1024 + 1);
        await Assert.ThrowsAsync<IOException>(() => service.ReviewAsync(source, false));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ReviewAsync(source, true));
    }
}
