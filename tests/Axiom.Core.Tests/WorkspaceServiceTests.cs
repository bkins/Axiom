using Axiom.Core;

namespace Axiom.Core.Tests;

public sealed class WorkspaceServiceTests
{
    [Fact]
    public async Task OpenNormalizesTheRootAndUsesTheFolderName()
    {
        var store = new MemoryWorkspaceStore();
        var service = new WorkspaceService(store);
        var root = Path.Combine(Path.GetTempPath(), "AxiomWorkspaceTests", "Docs") + Path.DirectorySeparatorChar;
        var opened = new DateTimeOffset(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);

        var workspace = await service.OpenAsync(root, opened);

        Assert.Equal(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)), workspace.RootPath);
        Assert.Equal("Docs", workspace.DisplayName);
        Assert.Equal(opened, workspace.LastOpenedUtc);
    }

    [Fact]
    public void TreeBuilderCreatesFoldersFirstThenDocumentsAndExcludesOutsidePaths()
    {
        var root = Path.Combine(Path.GetTempPath(), "AxiomWorkspaceTests", Guid.NewGuid().ToString("N"));
        var paths = new[]
        {
            Path.Combine(root, "zeta.md"),
            Path.Combine(root, "Alpha", "two.markdown"),
            Path.Combine(root, "Alpha", "one.md"),
            Path.Combine(root, "beta.md"),
            Path.Combine(Path.GetDirectoryName(root)!, "outside.md")
        };

        var tree = WorkspaceTreeBuilder.Build(root, paths);

        Assert.Equal(["Alpha", "beta.md", "zeta.md"], tree.Select(node => node.Name));
        var alpha = tree[0];
        Assert.False(alpha.IsDocument);
        Assert.Equal(["one.md", "two.markdown"], alpha.Children.Select(node => node.Name));
        Assert.All(alpha.Children, node => Assert.True(node.IsDocument));
    }

    [Fact]
    public void PinPathMustBeMarkdownInsideTheWorkspace()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "AxiomWorkspaceTests", Guid.NewGuid().ToString("N")));

        Assert.Equal(Path.Combine("nested", "note.md"), WorkspaceService.GetRelativeDocumentPath(root, Path.Combine(root, "nested", "note.md")));
        Assert.Throws<ArgumentOutOfRangeException>(() => WorkspaceService.GetRelativeDocumentPath(root, Path.Combine(root, "note.txt")));
        Assert.Throws<ArgumentOutOfRangeException>(() => WorkspaceService.GetRelativeDocumentPath(root, Path.Combine(Path.GetDirectoryName(root)!, "outside.md")));
    }

    [Fact]
    public void WorkspacePinFallbackTextIsTheRelativeDocumentPath()
    {
        var pin = new WorkspacePin(Path.Combine("nested", "note.md"), DateTimeOffset.UtcNow);

        Assert.Equal(Path.Combine("nested", "note.md"), pin.ToString());
    }

    private sealed class MemoryWorkspaceStore : IWorkspaceStore
    {
        private WorkspaceRecord? _active;

        public Task<WorkspaceRecord> OpenWorkspaceAsync(string normalizedRootPath, string displayName, DateTimeOffset openedUtc, CancellationToken cancellationToken = default)
        {
            _active = new WorkspaceRecord(Guid.NewGuid(), normalizedRootPath, displayName, openedUtc, openedUtc);
            return Task.FromResult(_active);
        }

        public Task<WorkspaceRecord?> GetActiveWorkspaceAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_active);

        public Task<IReadOnlyList<WorkspacePin>> GetPinsAsync(Guid workspaceId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkspacePin>>([]);
        public Task AddPinAsync(Guid workspaceId, string relativePath, DateTimeOffset pinnedUtc, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RemovePinAsync(Guid workspaceId, string relativePath, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
