using System.IO.Compression;
using Xunit;

namespace Axiom.Infrastructure.Tests;

public sealed class ProtectedFolderRecycleTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FolderBackupRetainsAssetsAndCancellationRetainsOriginal(bool fail)
    {
        using var fixture = new MutationTestWorkspace();
        var folder = Path.Combine(fixture.Root, "source");
        Directory.CreateDirectory(Path.Combine(folder, "empty"));
        await File.WriteAllTextAsync(Path.Combine(folder, "a.md"), "# original");
        await File.WriteAllBytesAsync(Path.Combine(folder, "a.bin"), [1, 2, 3]);
        var hash = await new WorkspaceFileOperations(fixture.State).ReviewFolderAsync(fixture.Workspace, "source");
        var service = new ProtectedFolderRecycle(fixture.State, new TestRecycle(Path.Combine(fixture.Root, "recycled"), fail));
        if (fail)
        {
            await Assert.ThrowsAsync<IOException>(() => service.ExecuteAsync(fixture.Workspace, "source", hash));
            Assert.True(File.Exists(Path.Combine(folder, "a.md")));
        }
        else
        {
            var backup = await service.ExecuteAsync(fixture.Workspace, "source", hash);
            using var archive = ZipFile.OpenRead(backup);
            Assert.Contains(archive.Entries, item => item.FullName == "source/a.bin");
            Assert.Contains(archive.Entries, item => item.FullName == "source/empty/");
            Assert.True(File.Exists(backup + ".txt"));
            Assert.False(Directory.Exists(folder));
        }
    }

    private sealed class TestRecycle(string destination, bool fail) : IRecycleBin
    {
        public void Recycle(string path)
        {
            if (fail) throw new IOException("Simulated cancellation; source retained.");
            Directory.Move(path, destination);
        }
    }
}
