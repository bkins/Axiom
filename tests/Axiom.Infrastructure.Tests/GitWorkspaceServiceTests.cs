using Axiom.Core;

namespace Axiom.Infrastructure.Tests;

public sealed class GitWorkspaceServiceTests
{
    [Fact]
    public async Task BatchStagesAndUnstagesAtomicallyWithoutChangingOtherIndexOrWorkingEntries()
    {
        using var fixture = new GitHistoryTestWorkspace();
        foreach (var name in new[] { "a.md", "b.md", "other.md" }) await File.WriteAllTextAsync(Path.Combine(fixture.Root, name), "baseline");
        fixture.Run("add", "."); fixture.Run("commit", "-qm", "Baseline");
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "other.md"), "unrelated staged");
        fixture.Run("add", "other.md");
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "a.md"), "saved changed");
        File.Delete(Path.Combine(fixture.Root, "b.md"));
        var service = new GitWorkspaceService();
        var snapshot = (await service.InspectAsync(fixture.Root))!;
        var hashes = new Dictionary<string, string?> { ["a.md"] = await service.ReviewWorkingHashAsync(snapshot, "a.md"), ["b.md"] = null };
        var index = await File.ReadAllBytesAsync(snapshot.IndexPath);
        await Assert.ThrowsAsync<IOException>(() => service.StageSelectedAsync(snapshot, hashes, validateBeforePublish: () => throw new IOException("Dirty editor changed")));
        Assert.Equal(index, await File.ReadAllBytesAsync(snapshot.IndexPath));
        Assert.False(File.Exists(snapshot.IndexPath + ".lock"));
        await service.StageSelectedAsync(snapshot, hashes);
        Assert.Equal("saved changed", fixture.Run("show", ":a.md"));
        Assert.Equal("unrelated staged", fixture.Run("show", ":other.md"));
        Assert.DoesNotContain("b.md", fixture.Run("ls-files"));
        snapshot = (await service.InspectAsync(fixture.Root))!;
        await service.UnstageSelectedAsync(snapshot, ["a.md", "b.md"]);
        Assert.Equal("baseline", fixture.Run("show", ":a.md"));
        Assert.Equal("baseline", fixture.Run("show", ":b.md"));
        Assert.Equal("unrelated staged", fixture.Run("show", ":other.md"));
        Assert.Equal("saved changed", await File.ReadAllTextAsync(Path.Combine(fixture.Root, "a.md")));
        Assert.False(File.Exists(Path.Combine(fixture.Root, "b.md")));
    }

    [Fact]
    public async Task BatchRejectsStaleFilesCancellationAndIndexLocksWithoutPartialStaging()
    {
        using var fixture = new GitHistoryTestWorkspace();
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "a.md"), "first");
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "b.md"), "second");
        var service = new GitWorkspaceService();
        var snapshot = (await service.InspectAsync(fixture.Root))!;
        var hashes = new Dictionary<string, string?> { ["a.md"] = await service.ReviewWorkingHashAsync(snapshot, "a.md"), ["b.md"] = "stale" };
        await Assert.ThrowsAsync<IOException>(() => service.StageSelectedAsync(snapshot, hashes));
        Assert.Empty(fixture.Run("ls-files"));
        Assert.False(File.Exists(snapshot.IndexPath + ".lock"));
        hashes["b.md"] = await service.ReviewWorkingHashAsync(snapshot, "b.md");
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.StageSelectedAsync(snapshot, hashes, cancellation.Token, () => cancellation.Cancel()));
        Assert.Empty(fixture.Run("ls-files"));
        await File.WriteAllTextAsync(snapshot.IndexPath + ".lock", "external owner");
        await Assert.ThrowsAsync<IOException>(() => service.StageSelectedAsync(snapshot, hashes));
        Assert.Equal("external owner", await File.ReadAllTextAsync(snapshot.IndexPath + ".lock"));
    }

    [Fact]
    public async Task InspectionMarksFilteredMarkdownUnsupportedBeforeSelection()
    {
        using var fixture = new GitHistoryTestWorkspace();
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "a.md"), "ordinary");
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "b.markdown"), "filtered");
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, ".gitattributes"), "*.markdown filter=unsafe\n");
        var snapshot = (await new GitWorkspaceService().InspectAsync(fixture.Root))!;
        Assert.True(snapshot.Files.Single(file => file.RelativePath == "a.md").CanManage);
        var filtered = snapshot.Files.Single(file => file.RelativePath == "b.markdown");
        Assert.False(filtered.CanManage);
        Assert.Contains("filters require external Git", filtered.Reason);
    }

    [Fact]
    public async Task NestedWorkspaceShowsOutsideChangesButCannotStageThem()
    {
        using var fixture = new GitHistoryTestWorkspace();
        var workspace = Path.Combine(fixture.Root, "nested");
        Directory.CreateDirectory(workspace);
        await File.WriteAllTextAsync(Path.Combine(workspace, "inside.md"), "inside");
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "outside.md"), "outside");
        var service = new GitWorkspaceService();
        var snapshot = (await service.InspectAsync(workspace))!;
        Assert.Equal(Path.GetFullPath(fixture.Root), Path.GetFullPath(snapshot.RepositoryRoot));
        Assert.True(snapshot.Files.Single(file => file.RelativePath == "nested/inside.md").CanManage);
        Assert.False(snapshot.Files.Single(file => file.RelativePath == "outside.md").CanManage);
        await Assert.ThrowsAnyAsync<Exception>(() => service.StageAsync(snapshot, "outside.md", null));
        Assert.Empty(fixture.Run("ls-files"));
    }

    [Fact]
    public async Task DetachedHeadIsReadOnlyAndNonRepositoryReturnsNull()
    {
        using var fixture = new GitHistoryTestWorkspace();
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "a.md"), "baseline");
        fixture.Run("add", "a.md");
        fixture.Run("commit", "-qm", "Baseline");
        var service = new GitWorkspaceService();
        var detached = fixture.Run("rev-parse", "HEAD");
        fixture.Run("checkout", "--detach", detached);
        var snapshot = (await service.InspectAsync(fixture.Root))!;
        Assert.False(snapshot.CanMutate);
        Assert.Contains("Detached", snapshot.Reason);
        var ordinary = Path.Combine(Path.GetTempPath(), "AxiomNoGit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(ordinary);
        try { Assert.Null(await service.InspectAsync(ordinary)); }
        finally { Directory.Delete(ordinary); }
    }

    [Fact]
    public async Task StageRespectsGitLineEndingNormalizationWithoutChangingSavedBytes()
    {
        using var fixture = new GitHistoryTestWorkspace();
        fixture.Run("config", "core.autocrlf", "true");
        var path = Path.Combine(fixture.Root, "a.md");
        await File.WriteAllTextAsync(path, "Windows saved\r\ntext\r\n");
        var original = await File.ReadAllBytesAsync(path);
        var service = new GitWorkspaceService();
        var snapshot = (await service.InspectAsync(fixture.Root))!;
        await service.StageAsync(snapshot, "a.md", WorkspaceFileOperations.Hash(original));
        snapshot = (await service.InspectAsync(fixture.Root))!;
        Assert.Equal("Windows saved\ntext\n", (await service.ReadDiffAsync(snapshot, "a.md")).IndexText);
        await service.CommitAsync(await service.ReviewCommitAsync(snapshot, ["a.md"]), "Normalized commit");
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
        Assert.Empty((await service.InspectAsync(fixture.Root))!.Files);
    }

    [Fact]
    public async Task SelectedCommitPreservesUnrelatedStagingAndAllWorkingBytes()
    {
        using var fixture = new GitHistoryTestWorkspace();
        var path = Path.Combine(fixture.Root, "notes ü [x].md");
        var other = Path.Combine(fixture.Root, "other.md");
        await File.WriteAllTextAsync(path, "baseline\r\n");
        await File.WriteAllTextAsync(other, "other baseline");
        fixture.Run("add", ".");
        fixture.Run("commit", "-qm", "Baseline");
        await File.WriteAllTextAsync(other, "unrelated staged");
        fixture.Run("add", "other.md");
        await File.WriteAllTextAsync(path, "selected staged\r\n");
        var service = new GitWorkspaceService();
        var snapshot = (await service.InspectAsync(fixture.Root))!;
        await service.StageAsync(snapshot, "notes ü [x].md", WorkspaceFileOperations.Hash(await File.ReadAllBytesAsync(path)));
        snapshot = (await service.InspectAsync(fixture.Root))!;
        var diff = await service.ReadDiffAsync(snapshot, "notes ü [x].md");
        Assert.Equal("baseline\r\n", diff.HeadText);
        Assert.Equal("selected staged\r\n", diff.IndexText);
        await File.WriteAllTextAsync(path, "saved unstaged changes\r\n");
        var index = await File.ReadAllBytesAsync(snapshot.IndexPath);
        var review = await service.ReviewCommitAsync(snapshot, ["notes ü [x].md"]);
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, ".git", "hooks", "reference-transaction"), "#!/bin/sh\nexit 17\n");
        var commit = await service.CommitAsync(review, "Only selected notes");
        Assert.Equal(commit, fixture.Run("rev-parse", "HEAD"));
        Assert.Equal("selected staged", fixture.Run("show", commit + ":notes ü [x].md").TrimEnd());
        Assert.Equal("other baseline", fixture.Run("show", commit + ":other.md"));
        Assert.Equal("unrelated staged", fixture.Run("show", ":other.md"));
        Assert.Equal("saved unstaged changes\r\n", await File.ReadAllTextAsync(path));
        Assert.Equal("unrelated staged", await File.ReadAllTextAsync(other));
        Assert.Equal(index, await File.ReadAllBytesAsync(snapshot.IndexPath));
        Assert.False(File.Exists(snapshot.IndexPath + ".lock"));
    }

    [Fact]
    public async Task UnbornStageUnstageAndFirstSelectedCommitRetainOtherFiles()
    {
        using var fixture = new GitHistoryTestWorkspace();
        var path = Path.Combine(fixture.Root, "a.md");
        await File.WriteAllTextAsync(path, "first");
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "other.md"), "other");
        var service = new GitWorkspaceService();
        var snapshot = (await service.InspectAsync(fixture.Root))!;
        Assert.Null(snapshot.Head);
        await service.StageAsync(snapshot, "a.md", WorkspaceFileOperations.Hash(await File.ReadAllBytesAsync(path)));
        snapshot = (await service.InspectAsync(fixture.Root))!;
        await service.UnstageAsync(snapshot, "a.md");
        Assert.Equal("first", await File.ReadAllTextAsync(path));
        Assert.Empty(fixture.Run("ls-files"));
        snapshot = (await service.InspectAsync(fixture.Root))!;
        await service.StageAsync(snapshot, "a.md", WorkspaceFileOperations.Hash(await File.ReadAllBytesAsync(path)));
        snapshot = (await service.InspectAsync(fixture.Root))!;
        var commit = await service.CommitAsync(await service.ReviewCommitAsync(snapshot, ["a.md"]), "First selected file");
        Assert.Equal("a.md", fixture.Run("ls-tree", "--name-only", commit));
        Assert.Equal("other", await File.ReadAllTextAsync(Path.Combine(fixture.Root, "other.md")));
    }

    [Fact]
    public async Task StaleSourceIndexOrHeadRejectsMutationAndCancellationLeavesIndex()
    {
        using var fixture = new GitHistoryTestWorkspace();
        var path = Path.Combine(fixture.Root, "a.md");
        await File.WriteAllTextAsync(path, "first");
        fixture.Run("add", ".");
        fixture.Run("commit", "-qm", "Baseline");
        await File.WriteAllTextAsync(path, "new");
        var service = new GitWorkspaceService();
        var snapshot = (await service.InspectAsync(fixture.Root))!;
        await Assert.ThrowsAsync<IOException>(() => service.StageAsync(snapshot, "a.md", "stale"));
        var index = await File.ReadAllBytesAsync(snapshot.IndexPath);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.StageAsync(snapshot, "a.md", WorkspaceFileOperations.Hash(System.Text.Encoding.UTF8.GetBytes("new")), new CancellationToken(true)));
        Assert.Equal(index, await File.ReadAllBytesAsync(snapshot.IndexPath));
        await service.StageAsync(snapshot, "a.md", WorkspaceFileOperations.Hash(await File.ReadAllBytesAsync(path)));
        snapshot = (await service.InspectAsync(fixture.Root))!;
        var review = await service.ReviewCommitAsync(snapshot, ["a.md"]);
        await File.WriteAllTextAsync(path, "changed after review");
        await Assert.ThrowsAsync<IOException>(() => service.CommitAsync(review, "Must not commit"));
        Assert.Equal(snapshot.Head, fixture.Run("rev-parse", "HEAD"));
        Assert.False(File.Exists(snapshot.IndexPath + ".lock"));
        fixture.Run("commit", "--no-verify", "-qm", "External commit");
        await Assert.ThrowsAsync<IOException>(() => service.CommitAsync(review, "Stale head"));
    }

    [Fact]
    public async Task FilteredDocumentsAndConflictsStayReadOnly()
    {
        using var fixture = new GitHistoryTestWorkspace();
        var path = Path.Combine(fixture.Root, "a.md");
        await File.WriteAllTextAsync(path, "first");
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, ".gitattributes"), "*.md filter=unsafe\n");
        var service = new GitWorkspaceService();
        var snapshot = (await service.InspectAsync(fixture.Root))!;
        var hash = WorkspaceFileOperations.Hash(await File.ReadAllBytesAsync(path));
        await Assert.ThrowsAsync<IOException>(() => service.StageAsync(snapshot, "a.md", hash));
        Assert.Empty(fixture.Run("ls-files"));
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, ".git", "MERGE_HEAD"), new string('0', 40));
        var merging = (await service.InspectAsync(fixture.Root))!;
        Assert.False(merging.CanMutate);
        Assert.Contains("pending Git operation", merging.Reason);
    }
}
