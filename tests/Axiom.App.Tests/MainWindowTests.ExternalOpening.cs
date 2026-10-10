using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Axiom.App;
using Axiom.Core;
using Axiom.Infrastructure;
using Xunit;

namespace Axiom.App.Tests;

public sealed partial class MainWindowTests
{
    [AvaloniaFact]
    public async Task ExternalOpenFindsRememberedWorkspaceAndRevealsFilteredFileWithoutWritingIt()
    {
        var root = CreateTabWorkspace();
        try
        {
            var folder = Path.Combine(root, "remembered");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, "note.md");
            const string source = "# Ordinary Markdown\r\n\r\nSource\r\n";
            await File.WriteAllTextAsync(path, source);
            var state = new SqliteStateStore(new AppDataPaths(Path.Combine(root, "state")));
            await state.InitializeAsync();
            await state.OpenWorkspaceAsync(folder, "Remembered", DateTimeOffset.UtcNow);
            var window = CreateWindow(root);
            window.FindControl<Grid>("EditorPreviewLayout")!.Children.Remove(window.FindControl<NativeWebView>("Preview")!);
            window.Show();
            await window.StartupCompleted;
            window.FilterExplorerForTesting("hidden");
            await window.OpenForwardedDocumentAsync(path);
            Assert.Equal(folder, window.ActiveWorkspaceRootForTesting);
            Assert.Equal(source, window.EditorTextForTesting);
            Assert.Null(window.TabsForTesting.Single(tab => tab.Session.Path == path).Session.DocumentId);
            Assert.Equal("", window.FindControl<TextBox>("ExplorerFilter")!.Text);
            Assert.Equal("note.md", Assert.IsType<WorkspaceExplorerNode>(window.FindControl<TreeView>("FileTree")!.SelectedItem).RelativePath);
            window.SetEditorTextForTesting(source + "Unsaved");
            await window.OpenForwardedDocumentAsync(path);
            Assert.Equal(source + "Unsaved", window.EditorTextForTesting);
            Assert.Single(window.TabsForTesting, tab => tab.Session.Path == path);
            Assert.Equal(source, await File.ReadAllTextAsync(path));
            await CloseWorkbenchTestAsync(window);
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public async Task CancelledExternalWorkspaceSwitchRetainsDirtyTextAndStandaloneOpenKeepsWorkspace()
    {
        var root = CreateTabWorkspace();
        try
        {
            var firstRoot = Path.Combine(root, "first");
            var secondRoot = Path.Combine(root, "second");
            Directory.CreateDirectory(firstRoot);
            Directory.CreateDirectory(secondRoot);
            var first = Path.Combine(firstRoot, "a.md");
            var second = Path.Combine(secondRoot, "b.md");
            var standalone = Path.Combine(root, "standalone.markdown");
            await File.WriteAllTextAsync(first, "First");
            await File.WriteAllTextAsync(second, "Second");
            await File.WriteAllTextAsync(standalone, "Standalone");
            var window = CreateWindow(root);
            window.FindControl<Grid>("EditorPreviewLayout")!.Children.Remove(window.FindControl<NativeWebView>("Preview")!);
            window.Show();
            await window.StartupCompleted;
            await window.SwitchWorkspaceAsync(secondRoot);
            await window.SwitchWorkspaceAsync(firstRoot);
            await window.OpenDocumentAsync(first);
            window.SetEditorTextForTesting("Dirty first");
            window.UnsavedChoiceForTesting = _ => Task.FromResult("Cancel");
            Assert.False(await window.OpenExternalDocumentAsync(second));
            Assert.Equal(firstRoot, window.ActiveWorkspaceRootForTesting);
            Assert.Equal("Dirty first", window.EditorTextForTesting);
            Assert.True(await window.OpenExternalDocumentAsync(standalone));
            Assert.Equal(firstRoot, window.ActiveWorkspaceRootForTesting);
            Assert.Equal("Standalone", window.EditorTextForTesting);
            Assert.Contains(window.TabsForTesting, tab => tab.Session.Text == "Dirty first" && tab.Session.IsDirty);
            Assert.False(await window.OpenExternalDocumentAsync(Path.Combine(secondRoot, "missing.md")));
            Assert.Equal(firstRoot, window.ActiveWorkspaceRootForTesting);
            Assert.Equal("Standalone", window.EditorTextForTesting);
            Assert.Equal("First", await File.ReadAllTextAsync(first));
            Assert.Equal("Second", await File.ReadAllTextAsync(second));
            await CloseWorkbenchTestAsync(window);
        }
        finally { Directory.Delete(root, true); }
    }
}
