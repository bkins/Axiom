using Avalonia.Headless.XUnit;
using Axiom.App;
using Axiom.Core;
using Axiom.Infrastructure;
using Xunit;

namespace Axiom.App.Tests;

public sealed class MainWindowTests
{
    [AvaloniaFact]
    public async Task FolderListingOpeningSwitchingAndUnicodeAreTestableWithoutANativeDesktop()
    {
        var root = Path.Combine(Path.GetTempPath(), $"AxiomUiTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var alpha = Path.Combine(root, "alpha.md");
            var bravo = Path.Combine(root, "bravo.md");
            var charlie = Path.Combine(root, "nested", "charlie.markdown");
            Directory.CreateDirectory(Path.GetDirectoryName(charlie)!);
            await File.WriteAllTextAsync(alpha, "# Café ☕\n\nAlpha");
            await File.WriteAllTextAsync(bravo, "# Bravo\n\nSecond document");
            await File.WriteAllTextAsync(charlie, "# Charlie\n\nNested document");
            await File.WriteAllTextAsync(Path.Combine(root, "ignore.txt"), "not markdown");

            var window = CreateWindow(root);
            await window.LoadFolderAsync(root);

            Assert.Equal(["alpha.md", "bravo.md", Path.Combine("nested", "charlie.markdown")], window.FolderItemsForTesting.OrderBy(item => item));
            var nested = Assert.Single(window.WorkspaceTreeForTesting, node => node.Name == "nested");
            Assert.False(nested.IsDocument);
            Assert.Equal("charlie.markdown", Assert.Single(nested.Children).Name);
            await window.OpenDocumentAsync(alpha);
            Assert.Equal("# Café ☕\n\nAlpha", window.EditorTextForTesting);
            Assert.True(window.IsEnrollAvailableForTesting);

            await window.OpenDocumentAsync(bravo);
            Assert.Equal("# Bravo\n\nSecond document", window.EditorTextForTesting);
            window.Close();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task FailedWorkspaceLoadLeavesTheCurrentWorkspaceIntact()
    {
        var root = Path.Combine(Path.GetTempPath(), $"AxiomUiTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var window = CreateWindow(root);
            await window.LoadFolderAsync(root);
            var activeRoot = window.ActiveWorkspaceRootForTesting;

            await Assert.ThrowsAsync<DirectoryNotFoundException>(() =>
                window.LoadFolderAsync(Path.Combine(root, "missing")));

            Assert.Equal(activeRoot, window.ActiveWorkspaceRootForTesting);
            window.Close();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static MainWindow CreateWindow(string root)
    {
        var paths = new AppDataPaths(Path.Combine(root, "state"));
        paths.EnsureCreated();
        var log = new NullAxiomLog();
        var state = new SqliteStateStore(paths, log);
        state.InitializeAsync().GetAwaiter().GetResult();
        var files = new FileDocumentStore(log);
        var documents = new DocumentService(files, state, state, log);
        return new MainWindow(documents, new WorkspaceService(state), files, state, new MarkdownPreviewService(), new ExternalFileMonitor(), log, () => Task.CompletedTask);
    }
}
