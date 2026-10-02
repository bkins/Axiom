using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Axiom.Core;
using Axiom.Infrastructure;
using Xunit;

namespace Axiom.App.Tests;

public sealed partial class MainWindowTests
{
    [AvaloniaFact]
    public async Task WorkbenchFilteringRetainsFoldersAndLazyOutlinesUseWorkingBuffers()
    {
        var root = CreateTabWorkspace();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "notes"));
            var path = Path.Combine(root, "notes", "alpha.md");
            await File.WriteAllTextAsync(path, "# Saved\n\n## Topic\n");
            await File.WriteAllTextAsync(Path.Combine(root, "other.md"), "# Other\n");
            var window = CreateWindow(root);
            await window.LoadFolderAsync(root);
            var folder = window.ExplorerNodesForTesting.Single(node => node.Name == "notes");
            var file = Assert.Single(folder.Children);
            Assert.True(Assert.Single(file.Children).IsPlaceholder);
            await window.LoadExplorerOutlineAsync(file);
            Assert.Equal(new[] { "Saved", "Topic" }, file.Children.Select(node => node.Name));
            Assert.NotEqual(folder.Icon, file.Icon);
            Assert.NotEqual(file.Icon, file.Children[0].Icon);
            await window.OpenDocumentAsync(path);
            window.SetEditorTextForTesting("# Unsaved\n\n## Live heading\n");
            await window.LoadExplorerOutlineAsync(file);
            Assert.Equal("Live heading", file.Children[1].Name);
            await window.OpenExplorerNodeForTestingAsync(file.Children[1]);
            Assert.Equal(window.EditorTextForTesting.IndexOf("## Live", StringComparison.Ordinal), window.EditorCaretOffsetForTesting);
            window.FilterExplorerForTesting("ALPHA");
            Assert.Equal("notes", Assert.Single(window.ExplorerNodesForTesting).Name);
            Assert.Single(window.ExplorerNodesForTesting[0].Children);
            window.FilterExplorerForTesting("");
            var restoredFolder = window.ExplorerNodesForTesting.Single(node => node.Name == "notes");
            restoredFolder.IsExpanded = true;
            restoredFolder.Children[0].IsExpanded = true;
            window.FilterExplorerForTesting("other");
            window.FilterExplorerForTesting("");
            Assert.True(window.ExplorerNodesForTesting.Single(node => node.Name == "notes").Children[0].IsExpanded);
            Assert.EndsWith(" *", window.TabsForTesting.Last().Label);
            Assert.Equal("# Saved\n\n## Topic\n", await File.ReadAllTextAsync(path));
            await CloseWorkbenchTestAsync(window);
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public async Task StaleOutlineMovesOnlyToUniqueCurrentHeadingAndRejectsAmbiguity()
    {
        var root = CreateTabWorkspace();
        try
        {
            var path = Path.Combine(root, "source.md");
            await File.WriteAllTextAsync(path, "# First\n\n## Destination\n");
            var window = CreateWindow(root);
            await window.LoadFolderAsync(root);
            var file = window.ExplorerNodesForTesting.Single(node => node.IsDocument);
            await window.LoadExplorerOutlineAsync(file);
            var oldHeading = file.Children[1];
            await window.OpenDocumentAsync(path);
            window.SetEditorTextForTesting("# First\n\nNew paragraph\n\n## Destination\n");
            await window.OpenExplorerNodeForTestingAsync(oldHeading);
            Assert.Equal(window.EditorTextForTesting.IndexOf("## Destination", StringComparison.Ordinal), window.EditorCaretOffsetForTesting);
            window.SetEditorTextForTesting("# First\n\n## Destination\n\n## Destination\n");
            window.SetEditorSelectionForTesting(0, 0);
            await window.OpenExplorerNodeForTestingAsync(oldHeading);
            Assert.Equal(0, window.EditorCaretOffsetForTesting);
            Assert.Contains("ambiguous", window.FindControl<TextBlock>("StatusText")!.Text!, StringComparison.OrdinalIgnoreCase);
            await CloseWorkbenchTestAsync(window);
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public async Task EmbeddedPanelsPersistLayoutAndRetainDirtyBufferUndoAndRecovery()
    {
        var root = CreateTabWorkspace();
        try
        {
            var window = CreateWindow(root);
            window.FindControl<Grid>("EditorPreviewLayout")!.Children.Remove(window.FindControl<NativeWebView>("Preview")!);
            window.Show();
            await window.StartupCompleted;
            await window.NewTabForTestingAsync();
            window.SetEditorTextForTesting("# Protected unsaved work\n");
            window.SetEditorSelectionForTesting(2, 9);
            Assert.True(window.ApplyMarkdownEditForTesting(MarkdownEditKind.Bold));
            var text = window.EditorTextForTesting;
            var caret = window.EditorCaretOffsetForTesting;
            window.ApplyWorkbenchForTesting(new(300, 350, 1, true));
            var host = window.FindControl<Border>("ToolWindowHost")!;
            for (var index = 0; index < 6; index++)
            {
                window.ToggleToolWindowForTesting(index);
                window.UpdateLayout();
                Assert.True(host.IsVisible);
                Assert.IsAssignableFrom<Control>(window.FindControl<TabControl>("SidebarTopics")!.SelectedContent);
                Assert.Equal(text, window.EditorTextForTesting);
                Assert.Equal(caret, window.EditorCaretOffsetForTesting);
            }
            await window.SaveWorkbenchForTestingAsync();
            var store = new SqliteStateStore(new AppDataPaths(Path.Combine(root, "state")));
            var saved = await store.GetWorkbenchLayoutAsync();
            Assert.Equal(5, saved.ToolIndex);
            Assert.True(saved.ToolVisible);
            window.ToggleToolWindowForTesting(5);
            Assert.False(host.IsVisible);
            await window.ProtectIfNeededForTestingAsync();
            Assert.Contains(await store.GetRecoverableDraftsAsync(), draft => draft.Text == text);
            Assert.True(window.EditorCanUndoForTesting);
            await window.ExecuteCommandAsync("view.resetLayout");
            Assert.False(host.IsVisible);
            Assert.Equal(text, window.EditorTextForTesting);
            Assert.False((await store.GetWorkbenchLayoutAsync()).ToolVisible);
            await CloseWorkbenchTestAsync(window);
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public async Task WorkbenchSwitchClearsFilterAndDoesNotReuseAnotherWorkspaceOutline()
    {
        var root = CreateTabWorkspace();
        try
        {
            var other = Path.Combine(root, "other");
            Directory.CreateDirectory(other);
            await File.WriteAllTextAsync(Path.Combine(root, "first.md"), "# First\n");
            await File.WriteAllTextAsync(Path.Combine(other, "second.md"), "# Second\n");
            var window = CreateWindow(root);
            await window.LoadFolderAsync(root);
            window.FilterExplorerForTesting("first");
            var prior = Assert.Single(window.ExplorerNodesForTesting);
            await window.LoadFolderAsync(other);
            var current = Assert.Single(window.ExplorerNodesForTesting);
            Assert.Equal("second.md", current.Name);
            await window.LoadExplorerOutlineAsync(current);
            Assert.Equal("Second", Assert.Single(current.Children).Name);
            Assert.True(Assert.Single(prior.Children).IsPlaceholder);
            await CloseWorkbenchTestAsync(window);
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public async Task WalkthroughUsesPackagedAssetsAndAssociationFailurePreservesSource()
    {
        var root = CreateTabWorkspace();
        try
        {
            var window = CreateWindow(root);
            window.SetEditorTextForTesting("# Working document\n");
            var source = window.EditorTextForTesting;
            string? launched = null;
            window.WalkthroughLaunchForTesting = path => { launched = path; return Task.CompletedTask; };
            Assert.Equal(CommandOutcome.Completed, (await window.ExecuteCommandAsync("help.walkthrough")).Outcome);
            Assert.Equal(Path.Combine(AppContext.BaseDirectory, "Walkthrough", "index.html"), launched);
            Assert.True(File.Exists(launched));
            window.WalkthroughLaunchForTesting = _ => throw new IOException("Browser association unavailable");
            Assert.Equal(CommandOutcome.Failed, (await window.ExecuteCommandAsync("help.walkthrough")).Outcome);
            Assert.Equal(source, window.EditorTextForTesting);
            await window.ProtectIfNeededForTestingAsync();
            var state = new SqliteStateStore(new AppDataPaths(Path.Combine(root, "state")));
            Assert.Contains(await state.GetRecoverableDraftsAsync(), draft => draft.Text == source);
            await CloseWorkbenchTestAsync(window);
        }
        finally { Directory.Delete(root, true); }
    }

    private static async Task CloseWorkbenchTestAsync(MainWindow window)
    {
        window.UnsavedChoiceForTesting = _ => Task.FromResult("Discard");
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => closed.TrySetResult();
        window.Close();
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }
}
