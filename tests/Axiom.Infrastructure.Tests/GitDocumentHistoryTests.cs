using Axiom.Infrastructure;

namespace Axiom.Infrastructure.Tests;

public sealed class GitDocumentHistoryTests
{
    [Fact]
    public async Task CheckpointRecordsOnlyReviewedDocumentWithoutChangingBranchOrRealIndex()
    {
        using var fixture = new GitHistoryTestWorkspace();
        var path = Path.Combine(fixture.Root, "notes.md");
        await File.WriteAllTextAsync(path, "committed baseline");
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "other.md"), "other baseline");
        fixture.Run("add", ".");
        fixture.Run("commit", "-qm", "Baseline");
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "other.md"), "other staged edit");
        fixture.Run("add", "other.md");
        await File.WriteAllTextAsync(path, "saved reviewed\r\ncontent");
        var head = fixture.Run("rev-parse", "HEAD");
        var branch = fixture.Run("symbolic-ref", "HEAD");
        var index = await File.ReadAllBytesAsync(Path.Combine(fixture.Root, ".git", "index"));
        var service = new GitDocumentHistoryService();
        var location = (await service.LocateAsync(path))!;
        var reviewed = await service.ReviewCheckpointAsync(location);
        var commit = await service.CheckpointAsync(location, reviewed);
        Assert.Equal("other baseline", fixture.Run("show", commit + ":other.md"));
        Assert.Equal("saved reviewed\r\ncontent", fixture.Run("show", commit + ":notes.md"));
        Assert.Equal(head, fixture.Run("rev-parse", "HEAD"));
        Assert.Equal(branch, fixture.Run("symbolic-ref", "HEAD"));
        Assert.Equal(index, await File.ReadAllBytesAsync(Path.Combine(fixture.Root, ".git", "index")));
        Assert.Equal("other staged edit", await File.ReadAllTextAsync(Path.Combine(fixture.Root, "other.md")));
        var revisions = await service.ListAsync(location);
        Assert.Contains(revisions, revision => revision.CommitHash == commit);
        await File.WriteAllTextAsync(path, "changed after review");
        await Assert.ThrowsAsync<IOException>(() => service.CheckpointAsync(location, reviewed));
        Assert.Equal(revisions.Select(revision => revision.CommitHash), (await service.ListAsync(location)).Select(revision => revision.CommitHash));
        var second = await service.CheckpointAsync(location, await service.ReviewCheckpointAsync(location));
        Assert.Equal(commit, fixture.Run("rev-parse", second + "^"));
        Assert.Equal(head, fixture.Run("rev-parse", "HEAD"));
    }

    [Fact]
    public async Task ExplicitSetupAndFirstCheckpointKeepExistingFilesAndLeaveHeadUnborn()
    {
        var root = Path.Combine(Path.GetTempPath(), "AxiomGitSetup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "notes.md");
            await File.WriteAllTextAsync(path, "ordinary source");
            var service = new GitDocumentHistoryService();
            await service.InitializeAsync(root);
            var location = (await service.LocateAsync(path))!;
            Assert.Empty(await service.ListAsync(location));
            await Assert.ThrowsAsync<IOException>(() => service.InitializeAsync(root));
            var commit = await service.CheckpointAsync(location, await service.ReviewCheckpointAsync(location));
            var revision = Assert.Single(await service.ListAsync(location));
            Assert.Equal(commit, revision.CommitHash);
            Assert.Equal("ordinary source", await service.ReadAsync(location, revision));
            Assert.False(File.Exists(Path.Combine(root, ".git", "index")));
            Assert.Equal("ordinary source", await File.ReadAllTextAsync(path));
        }
        finally
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task RenameHistoryReadsExactBlobsAndPreservesDirtyWorktreeIndexAndHead()
    {
        using var fixture = new GitHistoryTestWorkspace();
        var original = Path.Combine(fixture.Root, "notes ü.md");
        await File.WriteAllTextAsync(original, "first\r\nrevision\r\n");
        fixture.Run("add", "--", "notes ü.md");
        fixture.Run("commit", "-qm", "Initial notes");
        var originalCommit = fixture.Run("rev-parse", "HEAD");
        fixture.Run("mv", "notes ü.md", "renamed ü.md");
        fixture.Run("commit", "-qm", "Rename notes");
        var current = Path.Combine(fixture.Root, "renamed ü.md");
        await File.WriteAllTextAsync(current, "saved current");
        fixture.Run("add", "--", "renamed ü.md");
        fixture.Run("commit", "-qm", "Update notes");
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "unrelated.txt"), "staged unrelated");
        fixture.Run("add", "--", "unrelated.txt");
        await File.WriteAllTextAsync(current, "dirty outside app");
        var head = fixture.Run("rev-parse", "HEAD");
        var index = await File.ReadAllBytesAsync(Path.Combine(fixture.Root, ".git", "index"));
        var service = new GitDocumentHistoryService();
        var location = await service.LocateAsync(current);
        Assert.NotNull(location);
        var revisions = await service.ListAsync(location);
        Assert.Equal(3, revisions.Count);
        Assert.Equal("saved current", await service.ReadAsync(location, revisions[0]));
        Assert.Equal("notes ü.md", revisions[^1].Path);
        Assert.Equal(originalCommit, revisions[^1].CommitHash);
        Assert.Equal("first\r\nrevision\r\n", await service.ReadAsync(location, revisions[^1]));
        Assert.Equal(head, fixture.Run("rev-parse", "HEAD"));
        Assert.Equal(index, await File.ReadAllBytesAsync(Path.Combine(fixture.Root, ".git", "index")));
        Assert.Equal("dirty outside app", await File.ReadAllTextAsync(current));
        Assert.Equal("staged unrelated", await File.ReadAllTextAsync(Path.Combine(fixture.Root, "unrelated.txt")));
    }

    [Fact]
    public async Task NoRepositoryUnbornRepositoryCancellationAndInvalidRevisionAreExplicit()
    {
        using var fixture = new GitHistoryTestWorkspace();
        var service = new GitDocumentHistoryService();
        var path = Path.Combine(fixture.Root, "new.md");
        await File.WriteAllTextAsync(path, "not committed");
        var location = (await service.LocateAsync(path))!;
        Assert.Empty(await service.ListAsync(location));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ReadAsync(location, new("HEAD:elsewhere", DateTimeOffset.Now, "Invalid", "new.md")));
        await Assert.ThrowsAsync<IOException>(() => service.ListAsync(location with { RelativePath = "../outside.md" }));
        await Assert.ThrowsAsync<IOException>(() => service.ReviewCheckpointAsync(location with { RelativePath = "..\\outside.md" }));
        await Assert.ThrowsAsync<IOException>(() => service.ReviewCheckpointAsync(location with { RelativePath = ".GIT/config" }));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ListAsync(location, cancelled.Token));
        var outside = Path.Combine(Path.GetTempPath(), "AxiomNoGit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        try { Assert.Null(await service.LocateAsync(Path.Combine(outside, "ordinary.md"))); }
        finally { Directory.Delete(outside); }
    }
}
