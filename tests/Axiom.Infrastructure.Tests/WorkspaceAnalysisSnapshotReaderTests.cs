using System.Text;

namespace Axiom.Infrastructure.Tests;

public sealed class WorkspaceAnalysisSnapshotReaderTests
{
    [Fact]
    public async Task Saved_Unicode_Snapshot_Detects_Changes_Without_Writing_Or_Enrolling()
    {
        var root = Path.Combine(Path.GetTempPath(), "Axiom-analysis-source-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "Café.markdown");
        await File.WriteAllTextAsync(path, "# Café 🌲\r\nSaved source", Encoding.Unicode);
        var original = await File.ReadAllBytesAsync(path);
        var reader = new WorkspaceAnalysisSnapshotReader(new FileDocumentStore());

        var snapshot = await reader.ReadAsync(root, path);

        Assert.Equal("# Café 🌲\r\nSaved source", snapshot.Text);
        Assert.StartsWith("saved:Café.markdown:", snapshot.ReferenceKey);
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
        await File.AppendAllTextAsync(path, " changed", Encoding.Unicode);
        Assert.NotEqual(snapshot.SourceHash, (await reader.ReadAsync(root, path)).SourceHash);
    }

    [Fact]
    public async Task Rejects_Outside_NonMarkdown_Oversized_Empty_And_Cancelled_Sources()
    {
        var root = Path.Combine(Path.GetTempPath(), "Axiom-analysis-bound-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var reader = new WorkspaceAnalysisSnapshotReader(new FileDocumentStore());
        var path = Path.Combine(root, "one.md");
        await File.WriteAllTextAsync(path, "source");

        await Assert.ThrowsAsync<InvalidOperationException>(() => reader.ReadAsync(root, root + "-outside/one.md"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => reader.ReadAsync(root, Path.Combine(root, "secret.txt")));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.ReadAsync(root, path, new CancellationToken(true)));
        await File.WriteAllTextAsync(path, " ");
        await Assert.ThrowsAsync<InvalidOperationException>(() => reader.ReadAsync(root, path));
        await File.WriteAllTextAsync(path, new string('x', 1_048_577));
        await Assert.ThrowsAsync<InvalidOperationException>(() => reader.ReadAsync(root, path));
    }
}
