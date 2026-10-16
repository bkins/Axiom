using Axiom.Core;
using Xunit;

namespace Axiom.Infrastructure.Tests;

public sealed class WorkspaceToolsTests
{
    [Fact]
    public void RenamePreflightIsStableKeepsExtensionsAndRejectsCollisionsAndPaths()
    {
        using var fixture = new MutationTestWorkspace();
        File.WriteAllText(Path.Combine(fixture.Root, "b.markdown"), "B");
        File.WriteAllText(Path.Combine(fixture.Root, "a.md"), "A");
        var selected = new[] { ("b.markdown", false), ("a.md", false) };
        var plan = WorkspaceRenamePlan.Create(fixture.Root, selected, "{n}-{name}", File.Exists);
        Assert.Equal("1-a.md", Path.GetFileName(plan[0].Destination));
        Assert.Equal("2-b.markdown", Path.GetFileName(plan[1].Destination));
        Assert.Throws<IOException>(() => WorkspaceRenamePlan.Create(fixture.Root, selected, "{name}", File.Exists));
        Assert.Throws<ArgumentException>(() => WorkspaceRenamePlan.Create(fixture.Root, selected, "../{name}", File.Exists));
        Assert.Throws<ArgumentException>(() => WorkspaceRenamePlan.Create(fixture.Root, selected, "{unknown}", File.Exists));
    }

    [Fact]
    public async Task CopyKeepsOriginalIdentityAssetsAndEmptyFoldersAndRepairsOutgoingPaths()
    {
        using var fixture = new MutationTestWorkspace();
        Directory.CreateDirectory(Path.Combine(fixture.Root, "source", "empty"));
        Directory.CreateDirectory(Path.Combine(fixture.Root, "target"));
        var originalId = Guid.NewGuid();
        var source = Path.Combine(fixture.Root, "source", "a.md");
        var text = DocumentIdCodec.Enroll("# A\r\n![asset](asset.bin)\r\n[shared](../shared.md)", originalId);
        await File.WriteAllTextAsync(source, text);
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "shared.md"), "# Shared");
        await File.WriteAllBytesAsync(Path.Combine(fixture.Root, "source", "asset.bin"), [1, 2, 3]);
        await fixture.State.RecordIdentityAsync(originalId, source);
        var service = new ReviewedFolderCopy(fixture.State);
        var review = await service.ReviewAsync(fixture.Workspace, "source", "target/copy");
        await service.ApplyAsync(review);
        var copied = await File.ReadAllTextAsync(Path.Combine(fixture.Root, "target", "copy", "a.md"));
        Assert.NotEqual(originalId, DocumentIdCodec.Find(copied));
        Assert.NotNull(DocumentIdCodec.Find(copied));
        Assert.Contains("[shared](../../shared.md)", copied);
        Assert.Contains("![asset](asset.bin)", copied);
        Assert.Equal(text, await File.ReadAllTextAsync(source));
        Assert.True(Directory.Exists(Path.Combine(fixture.Root, "target", "copy", "empty")));
        Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(Path.Combine(fixture.Root, "target", "copy", "asset.bin")));
        Assert.Equal(2, (await fixture.State.GetIdentitiesAsync()).Count);
        Assert.Empty(await fixture.State.GetPendingMutationsAsync());
    }

    [Fact]
    public async Task ChangedCopySourceAndCancelledCopyDoNotPublishDestination()
    {
        using var fixture = new MutationTestWorkspace();
        Directory.CreateDirectory(Path.Combine(fixture.Root, "source"));
        var source = Path.Combine(fixture.Root, "source", "a.md");
        await File.WriteAllTextAsync(source, "# A");
        var service = new ReviewedFolderCopy(fixture.State);
        var review = await service.ReviewAsync(fixture.Workspace, "source", "copy");
        await File.AppendAllTextAsync(source, "changed");
        await Assert.ThrowsAsync<IOException>(() => service.ApplyAsync(review));
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "copy")));
        review = await service.ReviewAsync(fixture.Workspace, "source", "copy");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ApplyAsync(review, new CancellationToken(true)));
        Assert.Empty(await fixture.State.GetPendingMutationsAsync());
        await Assert.ThrowsAsync<IOException>(() => service.ReviewAsync(fixture.Workspace, "source", "source/nested"));
    }

    [Fact]
    public async Task BulkExportPublishesNestedSnapshotsWithoutSavingSourcesAndRefusesCollisions()
    {
        using var fixture = new MutationTestWorkspace();
        Directory.CreateDirectory(Path.Combine(fixture.Root, "nested"));
        var source = Path.Combine(fixture.Root, "nested", "a.md"); await File.WriteAllTextAsync(source, "saved");
        var service = new WorkspaceBatchExport(new DocumentExportService(), fixture.State.Paths.Root);
        var output = Path.Combine(fixture.Root, "output");
        var review = service.Review(fixture.Root, [new("unsaved **snapshot**", source)], output, "html");
        await service.ApplyAsync(review);
        Assert.Contains("unsaved <strong>snapshot</strong>", await File.ReadAllTextAsync(Path.Combine(output, "nested", "a.html")));
        Assert.Equal("saved", await File.ReadAllTextAsync(source));
        Assert.Throws<IOException>(() => service.Review(fixture.Root, [new("x", source)], output, "txt"));
        Assert.Throws<IOException>(() => service.Review(fixture.Root, [new("x", source), new("y", Path.ChangeExtension(source, "markdown"))], Path.Combine(fixture.Root, "collision"), "html"));
        var cancel = service.Review(fixture.Root, [new("x", source)], Path.Combine(fixture.Root, "cancelled"), "txt");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ApplyAsync(cancel, token: new CancellationToken(true)));
        Assert.False(Directory.Exists(cancel.Destination));
        Assert.Empty(Directory.GetDirectories(fixture.Root, ".axiom-export-*"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedLaterExportCleansStagingAndRetainsExistingFiles(bool cancel)
    {
        using var fixture = new MutationTestWorkspace();
        var source = Path.Combine(fixture.Root, "a.md"); await File.WriteAllTextAsync(source, "saved");
        using var cancellation = new CancellationTokenSource();
        var renderer = new SecondOutputFailure(cancel ? cancellation : null);
        var service = new WorkspaceBatchExport(new DocumentExportService(renderer), fixture.State.Paths.Root);
        var review = service.Review(fixture.Root, [new("x", source), new("y", Path.Combine(fixture.Root, "b.md"))], Path.Combine(fixture.Root, "failed-output"), "pdf");
        if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ApplyAsync(review, token: cancellation.Token));
        else await Assert.ThrowsAsync<IOException>(() => service.ApplyAsync(review));
        Assert.Equal(2, renderer.Calls);
        Assert.False(Directory.Exists(review.Destination));
        Assert.Empty(Directory.GetDirectories(fixture.Root, ".axiom-export-*"));
        Assert.Equal("saved", await File.ReadAllTextAsync(source));
    }

    private sealed class SecondOutputFailure(CancellationTokenSource? cancel) : IPdfExportRenderer
    {
        public int Calls { get; private set; }
        public Task<PdfRenderResult> RenderAsync(string html, PdfPageSize pageSize, CancellationToken cancellationToken)
        {
            if (++Calls == 2)
            {
                if (cancel is null) throw new IOException("Simulated renderer failure after first staged document.");
                cancel.Cancel(); cancellationToken.ThrowIfCancellationRequested();
            }
            return Task.FromResult(new PdfRenderResult(System.Text.Encoding.UTF8.GetBytes("%PDF-1.7 fixture"), []));
        }
    }

    [Fact]
    public async Task InterruptedCopyRegistersOnlyMatchingDestinationAndRefusesChangedTree()
    {
        using var fixture = new MutationTestWorkspace();
        Directory.CreateDirectory(Path.Combine(fixture.Root, "source"));
        var id = Guid.NewGuid();
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "source", "a.md"), DocumentIdCodec.Enroll("A", id));
        var service = new ReviewedFolderCopy(fixture.State);
        await service.ApplyAsync(await service.ReviewAsync(fixture.Workspace, "source", "copy"));
        var destination = Path.Combine(fixture.Root, "copy");
        var file = Path.Combine(destination, "a.md"); var original = await File.ReadAllTextAsync(file);
        var operations = new WorkspaceFileOperations(fixture.State);
        var hash = await operations.ReviewFolderAsync(fixture.Workspace, "copy");
        await fixture.State.PrepareMutationAsync(new(Guid.NewGuid(), fixture.Workspace.WorkspaceId, fixture.Root, DocumentMutationKind.CopyFolder, Path.Combine(fixture.Root, "source"), destination, hash, null));
        await File.AppendAllTextAsync(file, "external change");
        Assert.Contains("Needs inspection", Assert.Single(await operations.ReconcilePendingAsync()));
        Assert.Single(await fixture.State.GetPendingMutationsAsync());
        await File.WriteAllTextAsync(file, original);
        Assert.Contains("verified copy present", Assert.Single(await operations.ReconcilePendingAsync()));
        Assert.Empty(await fixture.State.GetPendingMutationsAsync());
        Assert.Single(await fixture.State.GetIdentitiesAsync());
        Assert.Equal(id, DocumentIdCodec.Find(await File.ReadAllTextAsync(Path.Combine(fixture.Root, "source", "a.md"))));
    }
}
