using Axiom.Core;
using Xunit;

namespace Axiom.Infrastructure.Tests;

public sealed class WorkspaceFolderClipboardTests
{
    [Fact]
    public async Task CapturedFolderCanPasteIntoAnotherWorkspaceWithoutReadingSourceAgain()
    {
        using var source = new MutationTestWorkspace();
        using var target = new MutationTestWorkspace();
        Directory.CreateDirectory(Path.Combine(source.Root, "notes"));
        await File.WriteAllTextAsync(Path.Combine(source.Root, "notes", "ordinary.md"), "# Saved notes");
        var snapshot = await new WorkspaceFolderClipboard(source.State).CaptureAsync(source.Workspace, "notes");
        await File.WriteAllTextAsync(Path.Combine(source.Root, "notes", "ordinary.md"), "Changed later");
        var clipboard = new WorkspaceFolderClipboard(target.State);
        await clipboard.ApplyAsync(clipboard.Review(snapshot, target.Workspace, "pasted"));
        Assert.Equal("# Saved notes", await File.ReadAllTextAsync(Path.Combine(target.Root, "pasted", "ordinary.md")));
        Assert.Equal("Changed later", await File.ReadAllTextAsync(Path.Combine(source.Root, "notes", "ordinary.md")));
    }

    [Fact]
    public async Task PasteUsesCapturedSavedContentAfterSourceDeletionAndEachPasteGetsFreshIdentities()
    {
        using var fixture = new MutationTestWorkspace();
        Directory.CreateDirectory(Path.Combine(fixture.Root, "source", "empty"));
        Directory.CreateDirectory(Path.Combine(fixture.Root, "copies"));
        var id = Guid.NewGuid();
        var original = DocumentIdCodec.Enroll("# Saved\r\n[Other](other.md)\r\n[Shared](../shared.md)\r\n![Image](image.bin)", id);
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "source", "note.md"), original);
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "source", "other.md"), "# Ordinary");
        await File.WriteAllBytesAsync(Path.Combine(fixture.Root, "source", "image.bin"), [1, 2, 3]);
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "shared.md"), "Shared");
        var clipboard = new WorkspaceFolderClipboard(fixture.State);
        var snapshot = await clipboard.CaptureAsync(fixture.Workspace, "source");
        Directory.Delete(Path.Combine(fixture.Root, "source"), true);
        Guid? first = null;
        foreach (var name in new[] { "one", "two" })
        {
            var review = clipboard.Review(snapshot, fixture.Workspace, "copies/" + name);
            await clipboard.ApplyAsync(review);
            var copied = await File.ReadAllTextAsync(Path.Combine(fixture.Root, "copies", name, "note.md"));
            var copiedId = DocumentIdCodec.Find(copied);
            Assert.NotNull(copiedId);
            Assert.NotEqual(id, copiedId);
            Assert.NotEqual(first, copiedId);
            first = copiedId;
            Assert.Contains("[Other](other.md)", copied);
            Assert.Contains("[Shared](../../shared.md)", copied);
            Assert.Contains("![Image](image.bin)", copied);
            Assert.Contains("\r\n", copied);
            Assert.Null(DocumentIdCodec.Find(await File.ReadAllTextAsync(Path.Combine(fixture.Root, "copies", name, "other.md"))));
            Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(Path.Combine(fixture.Root, "copies", name, "image.bin")));
            Assert.True(Directory.Exists(Path.Combine(fixture.Root, "copies", name, "empty")));
        }
        Assert.Empty(await fixture.State.GetPendingMutationsAsync());
    }

    [Fact]
    public async Task CollisionCancellationAndTraversalKeepExistingDataAndDoNotPublish()
    {
        using var fixture = new MutationTestWorkspace();
        Directory.CreateDirectory(Path.Combine(fixture.Root, "source"));
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "source", "a.md"), "saved");
        var clipboard = new WorkspaceFolderClipboard(fixture.State);
        var snapshot = await clipboard.CaptureAsync(fixture.Workspace, "source");
        Assert.Throws<IOException>(() => clipboard.Review(snapshot, fixture.Workspace, "source"));
        Assert.ThrowsAny<Exception>(() => clipboard.Review(snapshot, fixture.Workspace, "../escape"));
        var review = clipboard.Review(snapshot, fixture.Workspace, "new-copy");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => clipboard.ApplyAsync(review, cancelled.Token));
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "new-copy")));
        Assert.Empty(Directory.GetDirectories(fixture.Root, "*.axiom-copy-*"));
        Directory.CreateDirectory(Path.Combine(fixture.Root, "new-copy"));
        await Assert.ThrowsAsync<IOException>(() => clipboard.ApplyAsync(review));
        Assert.Equal("saved", await File.ReadAllTextAsync(Path.Combine(fixture.Root, "source", "a.md")));
    }
}
