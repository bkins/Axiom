using Axiom.Core;
using Axiom.Infrastructure;

namespace Axiom.Infrastructure.Tests;

public sealed class FolderMoveTests
{
    [Fact]
    public async Task MovingSubtreePreservesBytesAndRebindsPinsIdentityRecoveryRecentsAndSession()
    {
        using var fixture = new MutationTestWorkspace();
        Directory.CreateDirectory(Path.Combine(fixture.Root, "notes_", "empty"));
        Directory.CreateDirectory(Path.Combine(fixture.Root, "notes_extra"));
        Directory.CreateDirectory(Path.Combine(fixture.Root, "target"));
        var source = Path.Combine(fixture.Root, "notes_", "a.md");
        var other = Path.Combine(fixture.Root, "notes_extra", "a.md");
        var identity = Guid.NewGuid();
        var bytes = System.Text.Encoding.UTF8.GetBytes($"<!-- axiom:documentId={identity:D} -->\r\n# Source\r\n");
        await File.WriteAllBytesAsync(source, bytes);
        await File.WriteAllBytesAsync(Path.Combine(fixture.Root, "notes_", "image.png"), [1, 2, 3]);
        await File.WriteAllTextAsync(other, "other");
        await fixture.State.RecordIdentityAsync(identity, source);
        await fixture.State.AddPinAsync(fixture.Workspace.WorkspaceId, Path.Combine("notes_", "a.md"), DateTimeOffset.UtcNow);
        await fixture.State.AddPinAsync(fixture.Workspace.WorkspaceId, Path.Combine("notes_extra", "a.md"), DateTimeOffset.UtcNow);
        await fixture.State.SaveDraftAsync(new("draft", source, "unsaved protected work", DateTimeOffset.UtcNow, null, false));
        await fixture.State.RecordRecentDocumentAsync(source, fixture.Root);
        await fixture.State.SaveWorkspaceSessionAsync(fixture.Workspace.WorkspaceId, new(1, Path.Combine("notes_", "a.md"), [new(Path.Combine("notes_", "a.md"), 3, 0, 0, 0, 20, 0.2)]));
        var operations = new WorkspaceFileOperations(fixture.State);
        var hash = await operations.ReviewFolderAsync(fixture.Workspace, "notes_");
        await operations.MoveFolderAsync(fixture.Workspace, "notes_", "target/renamed", hash);
        var destination = Path.Combine(fixture.Root, "target", "renamed", "a.md");
        Assert.Equal(bytes, await File.ReadAllBytesAsync(destination));
        Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(Path.Combine(fixture.Root, "target", "renamed", "image.png")));
        Assert.True(Directory.Exists(Path.Combine(fixture.Root, "target", "renamed", "empty")));
        Assert.Equal(destination, Assert.Single(await fixture.State.GetIdentitiesAsync()).Path);
        var draft = Assert.Single(await fixture.State.GetRecoverableDraftsAsync());
        Assert.Equal(destination, draft.FilePath);
        Assert.Equal("unsaved protected work", draft.Text);
        Assert.Equal(destination, Assert.Single(await fixture.State.GetRecentDocumentsAsync()).Path);
        var pins = await fixture.State.GetPinsAsync(fixture.Workspace.WorkspaceId);
        Assert.Contains(pins, pin => pin.RelativePath == Path.Combine("target", "renamed", "a.md"));
        Assert.Contains(pins, pin => pin.RelativePath == Path.Combine("notes_extra", "a.md"));
        Assert.Equal(Path.Combine("target", "renamed", "a.md"), (await fixture.State.GetWorkspaceSessionAsync(fixture.Workspace.WorkspaceId))!.ActiveRelativePath);
        Assert.Empty(await fixture.State.GetPendingMutationsAsync());
    }

    [Fact]
    public async Task FailedStateCommitReconcilesMovedTreeOnceAndAmbiguousTreeRemainsBlocked()
    {
        using var fixture = new MutationTestWorkspace();
        Directory.CreateDirectory(Path.Combine(fixture.Root, "source"));
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "source", "a.md"), "saved");
        await fixture.State.AddPinAsync(fixture.Workspace.WorkspaceId, Path.Combine("source", "a.md"), DateTimeOffset.UtcNow);
        var operations = new WorkspaceFileOperations(fixture.State);
        var hash = await operations.ReviewFolderAsync(fixture.Workspace, "source");
        var failing = new WorkspaceFileOperations(new FaultingMutationStore(fixture.State) { FailReconciliation = true });
        await Assert.ThrowsAsync<IOException>(() => failing.MoveFolderAsync(fixture.Workspace, "source", "moved", hash));
        Assert.Single(await fixture.State.GetPendingMutationsAsync());
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "moved", "a.md"), "externally changed");
        Assert.Contains("Needs inspection", Assert.Single(await operations.ReconcilePendingAsync()));
        Assert.Single(await fixture.State.GetPendingMutationsAsync());
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "moved", "a.md"), "saved");
        Assert.Contains("Reconciled", Assert.Single(await operations.ReconcilePendingAsync()));
        Assert.Equal(Path.Combine("moved", "a.md"), Assert.Single(await fixture.State.GetPinsAsync(fixture.Workspace.WorkspaceId)).RelativePath);
        Assert.Empty(await operations.ReconcilePendingAsync());
    }

    [Fact]
    public async Task CycleCollisionStaleReviewAndCancellationRetainSource()
    {
        using var fixture = new MutationTestWorkspace();
        Directory.CreateDirectory(Path.Combine(fixture.Root, "source", "nested"));
        Directory.CreateDirectory(Path.Combine(fixture.Root, "occupied"));
        var file = Path.Combine(fixture.Root, "source", "a.md");
        await File.WriteAllTextAsync(file, "original");
        var operations = new WorkspaceFileOperations(fixture.State);
        var hash = await operations.ReviewFolderAsync(fixture.Workspace, "source");
        await Assert.ThrowsAsync<ArgumentException>(() => operations.MoveFolderAsync(fixture.Workspace, "source", "source/nested/loop", hash));
        await Assert.ThrowsAsync<ArgumentException>(() => operations.MoveFolderAsync(fixture.Workspace, "source", "SOURCE", hash));
        await Assert.ThrowsAsync<IOException>(() => operations.MoveFolderAsync(fixture.Workspace, "source", "occupied", hash));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operations.MoveFolderAsync(fixture.Workspace, "source", "moved", hash, new(true)));
        await File.WriteAllTextAsync(file, "updated");
        await Assert.ThrowsAsync<IOException>(() => operations.MoveFolderAsync(fixture.Workspace, "source", "moved", hash));
        var store = new FaultingMutationStore(fixture.State) { AfterPrepare = _ => File.WriteAllText(file, "changed during preparation") };
        hash = await operations.ReviewFolderAsync(fixture.Workspace, "source");
        await Assert.ThrowsAsync<IOException>(() => new WorkspaceFileOperations(store).MoveFolderAsync(fixture.Workspace, "source", "moved", hash));
        Assert.Equal("changed during preparation", await File.ReadAllTextAsync(file));
        Assert.Empty(await fixture.State.GetPendingMutationsAsync());
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "moved")));
    }

    [Fact]
    public async Task LinkedDescendantPreventsReviewAndRetainsExternalTarget()
    {
        using var fixture = new MutationTestWorkspace();
        var source = Path.Combine(fixture.Root, "source");
        var target = Path.Combine(fixture.Root, "external");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(target);
        await File.WriteAllTextAsync(Path.Combine(target, "a.md"), "retained");
        var link = Path.Combine(source, "junction");
        var start = new System.Diagnostics.ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { "/c", "mklink", "/J", link, target }) start.ArgumentList.Add(argument);
        using var process = System.Diagnostics.Process.Start(start)!;
        await process.WaitForExitAsync();
        Assert.Equal(0, process.ExitCode);
        try
        {
            await Assert.ThrowsAsync<IOException>(() => new WorkspaceFileOperations(fixture.State).ReviewFolderAsync(fixture.Workspace, "source"));
            Assert.Equal("retained", await File.ReadAllTextAsync(Path.Combine(target, "a.md")));
            Assert.Empty(await fixture.State.GetPendingMutationsAsync());
        }
        finally { Directory.Delete(link); }
    }

    [Fact]
    public async Task SingleFileMoveAlsoRebindsProtectedRecoveryAndRecents()
    {
        using var fixture = new MutationTestWorkspace();
        var source = Path.Combine(fixture.Root, "a.md");
        await File.WriteAllTextAsync(source, "saved");
        await fixture.State.SaveDraftAsync(new("draft", source, "protected", DateTimeOffset.UtcNow, null, false));
        await fixture.State.RecordRecentDocumentAsync(source, fixture.Root);
        await new WorkspaceFileOperations(fixture.State).ExecuteAsync(fixture.Workspace, DocumentMutationKind.Move, "a.md", "b.md");
        Assert.Equal(Path.Combine(fixture.Root, "b.md"), Assert.Single(await fixture.State.GetRecoverableDraftsAsync()).FilePath);
        Assert.Equal(Path.Combine(fixture.Root, "b.md"), Assert.Single(await fixture.State.GetRecentDocumentsAsync()).Path);
        Assert.Equal("protected", Assert.Single(await fixture.State.GetRecoverableDraftsAsync()).Text);
    }
}
