using System.Text;
using Axiom.Core;
using Axiom.Infrastructure;

namespace Axiom.Infrastructure.Tests;

public sealed class WorkspaceFileOperationsTests
{
    [Fact]
    public async Task CreateRenameMoveAndRecycleKeepIdentityPinsAndActualStateCoherent()
    {
        using var fixture = new MutationTestWorkspace();
        var operations = new WorkspaceFileOperations(fixture.State, new RetainingRecycleBin(Path.Combine(fixture.Root, "recycled")));
        await operations.ExecuteAsync(fixture.Workspace, DocumentMutationKind.CreateFolder, null, "empty");
        await operations.ExecuteAsync(fixture.Workspace, DocumentMutationKind.Create, null, "a.md");
        Assert.Empty(await File.ReadAllBytesAsync(Path.Combine(fixture.Root, "a.md")));
        var id = Guid.NewGuid();
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes($"<!-- axiom:documentId={id:D} -->\r\n# Café ☕\r\n")).ToArray();
        await File.WriteAllBytesAsync(Path.Combine(fixture.Root, "a.md"), bytes);
        await fixture.State.AddPinAsync(fixture.Workspace.WorkspaceId, "a.md", DateTimeOffset.UtcNow);
        await fixture.State.RecordIdentityAsync(id, Path.Combine(fixture.Root, "a.md"));
        await operations.ExecuteAsync(fixture.Workspace, DocumentMutationKind.Rename, "a.md", "b.md");
        await operations.ExecuteAsync(fixture.Workspace, DocumentMutationKind.Move, "b.md", "empty/c.md");
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(fixture.Root, "empty", "c.md")));
        Assert.Equal(Path.Combine("empty", "c.md"), Assert.Single(await fixture.State.GetPinsAsync(fixture.Workspace.WorkspaceId)).RelativePath);
        Assert.Equal(Path.Combine(fixture.Root, "empty", "c.md"), Assert.Single(await fixture.State.GetIdentitiesAsync()).Path);
        await operations.ExecuteAsync(fixture.Workspace, DocumentMutationKind.Recycle, "empty/c.md", null);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(fixture.Root, "recycled", "c.md")));
        Assert.Empty(await fixture.State.GetPinsAsync(fixture.Workspace.WorkspaceId));
        Assert.Empty(await fixture.State.GetIdentitiesAsync());
        Assert.Empty(await fixture.State.GetPendingMutationsAsync());
        var captured = Assert.Single(await fixture.State.GetDeletedDocumentsAsync(fixture.Workspace.WorkspaceId));
        Assert.Equal(bytes, captured.Content);
        Assert.Equal(id, captured.Intent.DocumentId);
        Assert.Equal("Available", captured.Status);
        Assert.NotNull(captured.PinnedUtc);
    }

    [Fact]
    public async Task DuplicateChangesOnlyCanonicalIdAndOrdinaryCopyIsExact()
    {
        using var fixture = new MutationTestWorkspace();
        var operations = new WorkspaceFileOperations(fixture.State);
        var id = Guid.NewGuid();
        var original = new byte[] { 0xef, 0xbb, 0xbf }.Concat(Encoding.UTF8.GetBytes($"<!-- axiom:documentId={id:D} -->\r\n# Content\r\n")).ToArray();
        await File.WriteAllBytesAsync(Path.Combine(fixture.Root, "a.md"), original);
        await operations.ExecuteAsync(fixture.Workspace, DocumentMutationKind.Duplicate, "a.md", "copy.md");
        var copy = await File.ReadAllBytesAsync(Path.Combine(fixture.Root, "copy.md"));
        var copiedId = DocumentIdCodec.Find(MarkdownIdentityBytes.Decode(copy));
        Assert.NotNull(copiedId);
        Assert.NotEqual(id, copiedId);
        Assert.Equal(original, MarkdownIdentityBytes.Assign(copy, id, true));
        Assert.Equal(original, await File.ReadAllBytesAsync(Path.Combine(fixture.Root, "a.md")));
        var ordinary = Encoding.UTF8.GetBytes("# Café\r\n\n");
        await File.WriteAllBytesAsync(Path.Combine(fixture.Root, "plain.md"), ordinary);
        await operations.ExecuteAsync(fixture.Workspace, DocumentMutationKind.Duplicate, "plain.md", "plain-copy.md");
        Assert.Equal(ordinary, await File.ReadAllBytesAsync(Path.Combine(fixture.Root, "plain-copy.md")));
    }

    [Fact]
    public async Task CollisionIntroducedAfterPrepareDoesNotOverwriteAndClearsUnappliedIntent()
    {
        using var fixture = new MutationTestWorkspace();
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "a.md"), "source");
        var store = new FaultingMutationStore(fixture.State) { AfterPrepare = intent => File.WriteAllText(intent.DestinationPath!, "other writer") };
        await Assert.ThrowsAsync<IOException>(() => new WorkspaceFileOperations(store).ExecuteAsync(fixture.Workspace, DocumentMutationKind.Move, "a.md", "b.md"));
        Assert.Equal("source", await File.ReadAllTextAsync(Path.Combine(fixture.Root, "a.md")));
        Assert.Equal("other writer", await File.ReadAllTextAsync(Path.Combine(fixture.Root, "b.md")));
        Assert.Empty(await fixture.State.GetPendingMutationsAsync());
    }

    [Fact]
    public async Task CompletedMoveWithStateFailureReconcilesOnceWithoutRepeatingMove()
    {
        using var fixture = new MutationTestWorkspace();
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "a.md"), "source");
        await fixture.State.AddPinAsync(fixture.Workspace.WorkspaceId, "a.md", DateTimeOffset.UtcNow);
        var store = new FaultingMutationStore(fixture.State) { FailReconciliation = true };
        var failure = await Assert.ThrowsAsync<IOException>(() => new WorkspaceFileOperations(store).ExecuteAsync(fixture.Workspace, DocumentMutationKind.Move, "a.md", "b.md"));
        Assert.Contains("completed", failure.Message);
        Assert.False(File.Exists(Path.Combine(fixture.Root, "a.md")));
        Assert.Single(await fixture.State.GetPendingMutationsAsync());
        var operations = new WorkspaceFileOperations(fixture.State);
        Assert.Single(await operations.ReconcilePendingAsync());
        Assert.Equal("b.md", Assert.Single(await fixture.State.GetPinsAsync(fixture.Workspace.WorkspaceId)).RelativePath);
        Assert.Empty(await operations.ReconcilePendingAsync());
        Assert.Equal("source", await File.ReadAllTextAsync(Path.Combine(fixture.Root, "b.md")));
    }

    [Fact]
    public async Task SourceChangeCancellationAndRecycleFailureRetainSourceAndPins()
    {
        using var fixture = new MutationTestWorkspace();
        var source = Path.Combine(fixture.Root, "a.md");
        await File.WriteAllTextAsync(source, "original");
        await fixture.State.AddPinAsync(fixture.Workspace.WorkspaceId, "a.md", DateTimeOffset.UtcNow);
        var operations = new WorkspaceFileOperations(fixture.State, new RetainingRecycleBin(fixture.Root, true));
        await Assert.ThrowsAsync<IOException>(() => operations.ExecuteAsync(fixture.Workspace, DocumentMutationKind.Move, "a.md", "b.md", "stale hash"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operations.ExecuteAsync(fixture.Workspace, DocumentMutationKind.Move, "a.md", "b.md", cancellationToken: new CancellationToken(true)));
        await Assert.ThrowsAsync<IOException>(() => operations.ExecuteAsync(fixture.Workspace, DocumentMutationKind.Recycle, "a.md", null));
        Assert.Equal("Cancelled", Assert.Single(await fixture.State.GetDeletedDocumentsAsync(fixture.Workspace.WorkspaceId)).Status);
        Assert.Equal("original", await File.ReadAllTextAsync(source));
        Assert.Single(await fixture.State.GetPinsAsync(fixture.Workspace.WorkspaceId));
        Assert.Empty(await fixture.State.GetPendingMutationsAsync());
    }

    [Fact]
    public async Task ReparsePointAndReadOnlyMutationsAreRejected()
    {
        using var fixture = new MutationTestWorkspace();
        var source = Path.Combine(fixture.Root, "a.md");
        await File.WriteAllTextAsync(source, "original");
        File.SetAttributes(source, FileAttributes.ReadOnly);
        try
        {
            await Assert.ThrowsAnyAsync<Exception>(() => new WorkspaceFileOperations(fixture.State).ExecuteAsync(fixture.Workspace, DocumentMutationKind.Enroll, "a.md", "a.md"));
            Assert.Equal("original", await File.ReadAllTextAsync(source));
        }
        finally { File.SetAttributes(source, FileAttributes.Normal); }
        var target = Path.Combine(fixture.Root, "target");
        Directory.CreateDirectory(target);
        await File.WriteAllTextAsync(Path.Combine(target, "linked.md"), "retained");
        var link = Path.Combine(fixture.Root, "junction");
        var start = new System.Diagnostics.ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "/c", "mklink", "/J", link, target }) start.ArgumentList.Add(argument);
        using var process = System.Diagnostics.Process.Start(start)!;
        await process.WaitForExitAsync();
        Assert.Equal(0, process.ExitCode);
        try
        {
            await Assert.ThrowsAsync<IOException>(() => new WorkspaceFileOperations(fixture.State).ExecuteAsync(fixture.Workspace, DocumentMutationKind.Move, "junction/linked.md", "b.md"));
        }
        finally { Directory.Delete(link); }
        Assert.True(File.Exists(source));
    }

    [Fact]
    public async Task ForgetWorkspacePreservesFilesIdentitiesDraftsAndOtherWorkspacePins()
    {
        using var fixture = new MutationTestWorkspace();
        var other = Path.Combine(fixture.Root, "other");
        Directory.CreateDirectory(other);
        var record = await fixture.State.OpenWorkspaceAsync(other, "Other", DateTimeOffset.UtcNow);
        await fixture.State.AddPinAsync(record.WorkspaceId, "other.md", DateTimeOffset.UtcNow);
        var id = Guid.NewGuid();
        await fixture.State.RecordIdentityAsync(id, Path.Combine(fixture.Root, "a.md"));
        await fixture.State.SaveDraftAsync(new RecoveryDraft("draft", null, "protected", DateTimeOffset.UtcNow, null, true));
        await fixture.State.ForgetWorkspaceAsync(fixture.Workspace.WorkspaceId);
        Assert.True(Directory.Exists(fixture.Root));
        Assert.Single(await fixture.State.ListWorkspacesAsync());
        Assert.Single(await fixture.State.GetIdentitiesAsync());
        Assert.Single(await fixture.State.GetRecoverableDraftsAsync());
        Assert.Single(await fixture.State.GetPinsAsync(record.WorkspaceId));
    }

    [Fact]
    public async Task BulkEnrollmentPreservesBytesHandlesStaleSelectionAndIsIdempotent()
    {
        using var fixture = new MutationTestWorkspace();
        var original = new byte[] { 0xef, 0xbb, 0xbf }.Concat(Encoding.UTF8.GetBytes("# Content\r\n\nunchanged")).ToArray();
        await File.WriteAllBytesAsync(Path.Combine(fixture.Root, "a.md"), original);
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "b.md"), "old");
        var service = new BulkEnrollmentService(new WorkspaceFileOperations(fixture.State));
        var preview = await service.PreviewAsync(fixture.Workspace);
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "b.md"), "external edit");
        var results = await service.ApplyAsync(fixture.Workspace, preview);
        Assert.Contains(results, result => result.RelativePath == "a.md" && result.Status == "Enrolled");
        Assert.Contains(results, result => result.RelativePath == "b.md" && result.Status.Contains("refresh"));
        var enrolled = await File.ReadAllBytesAsync(Path.Combine(fixture.Root, "a.md"));
        Assert.True(enrolled.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }));
        Assert.EndsWith("# Content\r\n\nunchanged", MarkdownIdentityBytes.Decode(enrolled));
        Assert.Equal("external edit", await File.ReadAllTextAsync(Path.Combine(fixture.Root, "b.md")));
        await service.ApplyAsync(fixture.Workspace, await service.PreviewAsync(fixture.Workspace));
        Assert.Equal(enrolled, await File.ReadAllBytesAsync(Path.Combine(fixture.Root, "a.md")));
        Assert.Equal(2, (await fixture.State.GetIdentitiesAsync()).Select(occurrence => occurrence.DocumentId).Distinct().Count());
    }

    [Fact]
    public async Task BulkScanIncludesUnselectedDuplicatesAndRejectsWrongWorkspaceAndCancellation()
    {
        using var fixture = new MutationTestWorkspace();
        var id = Guid.NewGuid();
        foreach (var name in new[] { "a.md", "b.md" }) await File.WriteAllTextAsync(Path.Combine(fixture.Root, name), $"<!-- axiom:documentId={id:D} -->\n");
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "plain.md"), "ordinary");
        var service = new BulkEnrollmentService(new WorkspaceFileOperations(fixture.State));
        var preview = await service.PreviewAsync(fixture.Workspace);
        Assert.Equal(2, preview.Count(entry => entry.Status.Contains("Duplicate")));
        var selected = preview.Where(entry => entry.IsEligible).ToArray();
        var cancelled = await service.ApplyAsync(fixture.Workspace, selected, cancellationToken: new CancellationToken(true));
        Assert.All(cancelled, result => Assert.Contains("Unprocessed", result.Status));
        var wrong = await service.ApplyAsync(fixture.Workspace with { WorkspaceId = Guid.NewGuid() }, selected);
        Assert.All(wrong, result => Assert.Contains("Workspace changed", result.Status));
        Assert.Equal("ordinary", await File.ReadAllTextAsync(Path.Combine(fixture.Root, "plain.md")));
    }

    [Fact]
    public async Task ThousandFilePreviewReportsProgressAndCancellationWithoutMutation()
    {
        using var fixture = new MutationTestWorkspace();
        foreach (var index in Enumerable.Range(0, 1000)) await File.WriteAllTextAsync(Path.Combine(fixture.Root, $"{index}.md"), "content");
        var service = new BulkEnrollmentService(new WorkspaceFileOperations(fixture.State));
        Assert.Equal(1000, (await service.PreviewAsync(fixture.Workspace)).Count);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.PreviewAsync(fixture.Workspace, cancellationToken: cancellation.Token));
        Assert.Equal("content", await File.ReadAllTextAsync(Path.Combine(fixture.Root, "0.md")));
    }
}
