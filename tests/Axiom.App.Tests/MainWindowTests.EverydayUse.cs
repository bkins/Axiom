using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Headless;
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
            var layout = window.FindControl<Grid>("EditorPreviewLayout")!;
            layout.Children.Remove(window.FindControl<NativeWebView>("Preview")!);
            var previewPlaceholder = new Border();
            Grid.SetColumn(previewPlaceholder, 2);
            layout.Children.Add(previewPlaceholder);
            window.Show();
            await window.StartupCompleted;
            window.UpdateLayout();
            var editorPane = window.FindControl<Grid>("EditorPane")!;
            Assert.True(editorPane.Bounds.Width > 100);
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
            window.UpdateLayout();
            Assert.True(editorPane.IsVisible);
            Assert.True(editorPane.Bounds.Width > 100, $"Restored editor width: {editorPane.Bounds.Width}; columns: {layout.ColumnDefinitions}");
            for (var round = 0; round < 3; round++)
            {
                await window.ToggleReadingModeAsync();
                window.UpdateLayout();
                Assert.False(editorPane.IsVisible);
                await window.ToggleReadingModeAsync();
                window.UpdateLayout();
                Assert.True(editorPane.Bounds.Width > 100);
            }
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
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            window.Closed += (_, _) => closed.TrySetResult();
            window.Close();
            await closed.Task;
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public async Task SidebarF1UsesSelectedTopicIncludingHeaderAndEmptyRelations()
    {
        var root = CreateTabWorkspace();
        try
        {
            var window = CreateWindow(root);
            var layout = window.FindControl<Grid>("EditorPreviewLayout")!;
            layout.Children.Remove(window.FindControl<NativeWebView>("Preview")!);
            window.Show();
            await window.StartupCompleted;
            string? requested = null;
            window.HelpRequestedForTesting = context => { requested = context; return Task.CompletedTask; };
            window.ToggleToolWindowForTesting(0);
            window.FindControl<Button>("RelationshipAdd")!.Focus();
            window.KeyPress(Key.F1, RawInputModifiers.None, PhysicalKey.F1, null);
            window.KeyRelease(Key.F1, RawInputModifiers.None, PhysicalKey.F1, null);
            Assert.Equal("Explicit relationships", requested);
            var guide = new MarkdownStructureService().Parse(OfflineHelp.Read());
            Assert.Equal("Explicit relationships", OfflineHelp.FindTopic(guide, requested)!.Text);
            window.ToggleToolWindowForTesting(1);
            window.FindControl<Button>("MetadataEnroll")!.Focus();
            window.KeyPress(Key.F1, RawInputModifiers.None, PhysicalKey.F1, null);
            window.KeyRelease(Key.F1, RawInputModifiers.None, PhysicalKey.F1, null);
            Assert.Equal("Document metadata and classification", requested);
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            window.Closed += (_, _) => closed.TrySetResult();
            window.Close();
            await closed.Task;
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
