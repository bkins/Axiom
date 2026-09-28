using Avalonia.Headless.XUnit;
using Xunit;

namespace Axiom.App.Tests;

public sealed partial class MainWindowTests
{
    [AvaloniaFact]
    public async Task SwitchingCleanWorkspacesClearsDocumentAndLoadsDistinctPinsAndEmptyFolders()
    {
        var root = Path.Combine(Path.GetTempPath(), "AxiomWorkspaceUi-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var first = Path.Combine(root, "first");
            var second = Path.Combine(root, "second");
            Directory.CreateDirectory(Path.Combine(first, "empty"));
            Directory.CreateDirectory(second);
            var firstDocument = Path.Combine(first, "a.md");
            await File.WriteAllTextAsync(firstDocument, "first content");
            await File.WriteAllTextAsync(Path.Combine(second, "b.md"), "second content");
            var window = CreateWindow(root);
            await window.LoadFolderAsync(first);
            Assert.Contains(window.WorkspaceTreeForTesting, node => node.Name == "empty" && !node.IsDocument && node.Children.Count == 0);
            await window.OpenDocumentAsync(firstDocument);
            await window.ToggleCurrentPinForTestingAsync();
            Assert.True(await window.SwitchWorkspaceAsync(second));
            Assert.Equal(second, window.ActiveWorkspaceRootForTesting);
            Assert.Empty(window.WorkspacePinsForTesting);
            Assert.Empty(window.EditorTextForTesting);
            Assert.Equal(["b.md"], window.FolderItemsForTesting);
            await Assert.ThrowsAsync<DirectoryNotFoundException>(() => window.SwitchWorkspaceAsync(Path.Combine(root, "missing")));
            Assert.Equal(second, window.ActiveWorkspaceRootForTesting);
            Assert.True(await window.SwitchWorkspaceAsync(first));
            Assert.Equal("a.md", Assert.Single(window.WorkspacePinsForTesting).RelativePath);
            window.Close();
        }
        finally { Directory.Delete(root, true); }
    }
}
