using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Axiom.Core;
using Axiom.Infrastructure;
using Xunit;

namespace Axiom.App.Tests;

public sealed partial class MainWindowTests
{
    [AvaloniaFact]
    public async Task CancelledAndSupersededDiscoveryRetainOldWorkspaceDirtyTabsAndRecovery()
    {
        var root = CreateTabWorkspace();
        try
        {
            var original = Path.Combine(root, "one.md");
            await File.WriteAllTextAsync(original, "Original");
            var second = Path.Combine(root, "second");
            var third = Path.Combine(root, "third");
            Directory.CreateDirectory(second);
            Directory.CreateDirectory(third);
            var window = CreateWindow(root);
            await window.LoadFolderAsync(root);
            await window.OpenDocumentAsync(original);
            window.SetEditorTextForTesting("Working unsaved text");
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            window.BeforeWorkspaceDiscoveryForTesting = async (folder, token) =>
            {
                if (folder != second) return;
                entered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            };
            var loading = window.SwitchWorkspaceAsync(second);
            await entered.Task;
            Assert.True(window.FindControl<Border>("WorkspaceLoadingPanel")!.IsVisible);
            var clock = Stopwatch.StartNew();
            window.CancelWorkspaceLoadingForTesting();
            Assert.False(await loading);
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(1));
            Assert.Equal(root, window.ActiveWorkspaceRootForTesting);
            Assert.Equal("Working unsaved text", window.TabsForTesting[0].Session.Text);
            window.UnsavedChoiceForTesting = _ => Task.FromResult("Cancel");
            entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            loading = window.SwitchWorkspaceAsync(second);
            await entered.Task;
            Assert.False(await window.SwitchWorkspaceAsync(third));
            Assert.False(await loading);
            Assert.Equal(root, window.ActiveWorkspaceRootForTesting);
            Assert.Equal(original, window.PathContextForTesting);
            Assert.True(window.TabsForTesting[0].Session.IsDirty);
            window.UnsavedChoiceForTesting = _ => Task.FromResult("Discard");
            await window.ConfirmAllTabsForTestingAsync();
            window.Close();
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public async Task ReadingModeRetainsUndoSelectionInactiveDirtyBuffersAndPersistedPreference()
    {
        var root = CreateTabWorkspace();
        try
        {
            var one = Path.Combine(root, "one.md");
            var two = Path.Combine(root, "two.md");
            await File.WriteAllTextAsync(one, "One original");
            await File.WriteAllTextAsync(two, "Two original");
            var window = CreateWindow(root);
            await window.LoadFolderAsync(root);
            await window.OpenExplorerPathAsync(one);
            var first = window.TabsForTesting[0];
            first.Document.Insert(first.Document.TextLength, " dirty");
            window.SetEditorSelectionForTesting(2, 3);
            var caret = window.EditorCaretOffsetForTesting;
            await window.ToggleReadingModeAsync();
            Assert.True(window.ReadingModeForTesting);
            Assert.False(window.FindControl<Grid>("EditorPane")!.IsVisible);
            await window.OpenExplorerPathAsync(two);
            await window.OpenExplorerPathAsync(two);
            Assert.Equal(2, window.TabsForTesting.Count);
            await window.SelectTabForTestingAsync(first);
            await window.ToggleReadingModeAsync();
            Assert.Equal(caret, window.EditorCaretOffsetForTesting);
            Assert.True(first.Document.UndoStack.CanUndo);
            Assert.True(first.Session.IsDirty);
            Assert.Equal("One original", await File.ReadAllTextAsync(one));
            var state = new SqliteStateStore(new AppDataPaths(Path.Combine(root, "state")));
            Assert.False(await state.GetReadingModeAsync());
            await window.ToggleReadingModeAsync();
            Assert.True(await state.GetReadingModeAsync());
            window.UnsavedChoiceForTesting = _ => Task.FromResult("Discard");
            await window.ConfirmAllTabsForTestingAsync();
            window.Close();
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void GeneratedOfflineHelpContainsMaintainedContextTopicsAndSafeRenderedContent()
    {
        var markdown = OfflineHelp.Read();
        Assert.Contains("Workspace loading, reading mode, and Help", markdown);
        Assert.DoesNotContain("<!-- Generated", markdown);
        var guide = new MarkdownStructureService().Parse(markdown);
        Assert.Contains("metadata", OfflineHelp.FindTopic(guide, "metadata")!.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Explicit relationships", OfflineHelp.FindTopic(guide, "Explicit relationships")!.Text);
        var html = new MarkdownPreviewService().RenderDocument(guide, null);
        Assert.Contains("script-src 'none'", html);
    }
}
