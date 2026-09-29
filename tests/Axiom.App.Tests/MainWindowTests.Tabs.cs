using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Axiom.Core;
using Axiom.Infrastructure;
using Xunit;

namespace Axiom.App.Tests;

public sealed partial class MainWindowTests
{
    [AvaloniaFact]
    public async Task ThreeTabsRetainBuffersUndoSelectionCaretAndStructuralIdentityWithoutWritingFiles()
    {
        var root = CreateTabWorkspace();
        try
        {
            var window = CreateWindow(root);
            await window.LoadFolderAsync(root);
            var paths = new[] { "first.md", "second.md", "third.md" }.Select(name => Path.Combine(root, name)).ToArray();
            foreach (var path in paths) await File.WriteAllTextAsync(path, "# Saved\n\ncontent");
            await window.OpenDocumentAsync(paths[0]);
            window.SetEditorSelectionForTesting(9, 7);
            Assert.True(window.ApplyMarkdownEditForTesting(MarkdownEditKind.Bold));
            var firstText = window.EditorTextForTesting;
            var firstCaret = window.EditorCaretOffsetForTesting;
            var firstHeading = window.OutlineEntriesForTesting[0];
            await window.OpenDocumentAsync(paths[1]);
            window.SetEditorSelectionForTesting(9, 7);
            Assert.True(window.ApplyMarkdownEditForTesting(MarkdownEditKind.Italic));
            var secondText = window.EditorTextForTesting;
            await window.OpenDocumentAsync(paths[2]);
            Assert.Equal(3, window.TabsForTesting.Count);
            await window.SelectTabForTestingAsync(window.TabsForTesting[0]);
            Assert.Equal(firstText, window.EditorTextForTesting);
            Assert.Equal(firstCaret, window.EditorCaretOffsetForTesting);
            Assert.Equal("content", window.EditorSelectedTextForTesting);
            Assert.Equal(firstHeading, window.OutlineEntriesForTesting[0]);
            window.UndoEditorForTesting();
            Assert.Equal("# Saved\n\ncontent", window.EditorTextForTesting);
            await window.SelectTabForTestingAsync(window.TabsForTesting[1]);
            Assert.Equal(secondText, window.EditorTextForTesting);
            Assert.True(window.EditorCanUndoForTesting);
            window.UndoEditorForTesting();
            foreach (var path in paths) Assert.Equal("# Saved\n\ncontent", await File.ReadAllTextAsync(path));
            window.Close();
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public async Task ReopeningNormalizedPathsReusesDirtyTabAndLabelsDuplicateBasenamesAndUntitledTabs()
    {
        var root = CreateTabWorkspace();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "nested"));
            var path = Path.Combine(root, "same.md");
            var nested = Path.Combine(root, "nested", "same.md");
            await File.WriteAllTextAsync(path, "first");
            await File.WriteAllTextAsync(nested, "second");
            var window = CreateWindow(root);
            await window.LoadFolderAsync(root);
            await window.OpenDocumentAsync(path);
            window.SetEditorTextForTesting("dirty first");
            var first = window.TabsForTesting[0];
            await window.OpenDocumentAsync(nested);
            await window.OpenDocumentAsync(Path.Combine(root, "nested", "..", "SAME.md"));
            Assert.Equal(2, window.TabsForTesting.Count);
            Assert.Equal("dirty first", window.EditorTextForTesting);
            Assert.Same(first.Document, window.FindControl<AvaloniaEdit.TextEditor>("Editor")!.Document);
            Assert.Equal("same.md *", first.Label);
            Assert.Equal(Path.Combine("nested", "same.md"), window.TabsForTesting[1].Label);
            await window.NewTabForTestingAsync();
            await window.NewTabForTestingAsync();
            Assert.NotEqual(window.TabsForTesting[2].Label, window.TabsForTesting[3].Label);
            window.UnsavedChoiceForTesting = _ => Task.FromResult("Discard");
            Assert.True(await window.ConfirmAllTabsForTestingAsync());
            window.Close();
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public async Task InactiveExternalChangesReloadCleanTabsAndRetainDirtyConflictsAndMissingBuffers()
    {
        var root = CreateTabWorkspace();
        try
        {
            var dirtyPath = Path.Combine(root, "dirty.md");
            var cleanPath = Path.Combine(root, "clean.md");
            var otherPath = Path.Combine(root, "other.md");
            foreach (var path in new[] { dirtyPath, cleanPath, otherPath }) await File.WriteAllTextAsync(path, "saved");
            var window = CreateWindow(root);
            await window.LoadFolderAsync(root);
            await window.OpenDocumentAsync(dirtyPath);
            window.SetEditorTextForTesting("local edits");
            await window.OpenDocumentAsync(cleanPath);
            await window.OpenDocumentAsync(otherPath);
            await File.WriteAllTextAsync(dirtyPath, "external edits");
            await File.WriteAllTextAsync(cleanPath, "external clean");
            await window.HandleExternalChangeForTestingAsync(dirtyPath);
            await window.HandleExternalChangeForTestingAsync(cleanPath);
            Assert.Equal("saved", window.EditorTextForTesting);
            Assert.Equal("local edits", window.TabsForTesting[0].Session.Text);
            Assert.Equal("external edits", window.TabsForTesting[0].Session.Conflict!.ExternalText);
            Assert.Contains("Conflict", window.TabsForTesting[0].Label);
            await window.SelectTabForTestingAsync(window.TabsForTesting[1]);
            Assert.Equal("external clean", window.EditorTextForTesting);
            File.Delete(cleanPath);
            await window.HandleExternalChangeForTestingAsync(cleanPath);
            Assert.Contains("Missing", window.TabsForTesting[1].Label);
            Assert.Equal("external clean", window.EditorTextForTesting);
            await window.SelectTabForTestingAsync(window.TabsForTesting[0]);
            Assert.True(window.FindControl<Border>("ConflictBanner")!.IsVisible);
            window.UnsavedChoiceForTesting = _ => Task.FromResult("Discard");
            await window.ConfirmAllTabsForTestingAsync();
            window.Close();
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public async Task RecoveryProtectsEverySavedAndUntitledTabAndUndoClearsOnlyItsDraft()
    {
        var root = CreateTabWorkspace();
        try
        {
            var path = Path.Combine(root, "saved.md");
            await File.WriteAllTextAsync(path, "saved");
            var window = CreateWindow(root);
            await window.OpenDocumentAsync(path);
            window.FindControl<AvaloniaEdit.TextEditor>("Editor")!.Document.Insert(5, " dirty");
            await window.NewTabForTestingAsync();
            window.SetEditorTextForTesting("first untitled dirty");
            await window.NewTabForTestingAsync();
            window.SetEditorTextForTesting("second untitled dirty");
            await window.ProtectIfNeededForTestingAsync();
            var state = new SqliteStateStore(new AppDataPaths(Path.Combine(root, "state")), new NullAxiomLog());
            var drafts = await state.GetRecoverableDraftsAsync();
            Assert.Equal(3, drafts.Count);
            Assert.Contains(drafts, draft => draft.Text == "saved dirty");
            Assert.Equal(2, drafts.Count(draft => draft.IsNewDocument));
            await window.SelectTabForTestingAsync(window.TabsForTesting[0]);
            window.UndoEditorForTesting();
            await window.ProtectIfNeededForTestingAsync();
            Assert.Equal(2, (await state.GetRecoverableDraftsAsync()).Count);
            Assert.Equal("saved", await File.ReadAllTextAsync(path));
            window.UnsavedChoiceForTesting = _ => Task.FromResult("Discard");
            await window.ConfirmAllTabsForTestingAsync();
            window.Close();
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public async Task DirtyCloseCancelKeepsBothTabsAndDiscardAffectsOnlyTheSelectedBuffer()
    {
        var root = CreateTabWorkspace();
        try
        {
            var window = CreateWindow(root);
            window.SetEditorTextForTesting("first draft");
            var first = window.TabsForTesting[0];
            await window.NewTabForTestingAsync();
            window.SetEditorTextForTesting("second draft");
            window.UnsavedChoiceForTesting = _ => Task.FromResult("Cancel");
            Assert.False(await window.CloseTabForTestingAsync(first));
            Assert.Equal(2, window.TabsForTesting.Count);
            Assert.Equal("first draft", first.Session.Text);
            window.UnsavedChoiceForTesting = _ => Task.FromResult("Discard");
            Assert.True(await window.CloseTabForTestingAsync(first));
            Assert.Equal("second draft", Assert.Single(window.TabsForTesting).Session.Text);
            await window.ConfirmAllTabsForTestingAsync();
            window.Close();
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public async Task FailedSaveInInactiveTabStopsWorkspaceTransitionAndPreservesOtherBuffers()
    {
        var root = CreateTabWorkspace();
        try
        {
            var path = Path.Combine(root, "saved.md");
            var second = Path.Combine(root, "second");
            Directory.CreateDirectory(second);
            await File.WriteAllTextAsync(path, "saved");
            var window = CreateWindow(root);
            await window.LoadFolderAsync(root);
            await window.OpenDocumentAsync(path);
            window.SetEditorTextForTesting("local edits");
            await window.NewTabForTestingAsync();
            window.SetEditorTextForTesting("untitled edits");
            await File.WriteAllTextAsync(path, "external edits");
            window.UnsavedChoiceForTesting = _ => Task.FromResult("Save");
            Assert.False(await window.SwitchWorkspaceAsync(second));
            Assert.Equal(root, window.ActiveWorkspaceRootForTesting);
            Assert.Equal(2, window.TabsForTesting.Count);
            Assert.Equal("local edits", window.TabsForTesting[0].Session.Text);
            Assert.Equal("untitled edits", window.TabsForTesting[1].Session.Text);
            Assert.Equal("external edits", await File.ReadAllTextAsync(path));
            window.UnsavedChoiceForTesting = _ => Task.FromResult("Discard");
            await window.ConfirmAllTabsForTestingAsync();
            window.Close();
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public async Task KeyboardCyclesTabsAndClosesOnlyTheActiveTab()
    {
        var root = CreateTabWorkspace();
        try
        {
            var window = CreateWindow(root);
            await window.NewTabForTestingAsync();
            window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Tab, KeyModifiers = KeyModifiers.Control });
            Assert.Same(window.TabsForTesting[0], window.FindControl<ListBox>("DocumentTabs")!.SelectedItem);
            window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.W, KeyModifiers = KeyModifiers.Control });
            Assert.Single(window.TabsForTesting);
            window.Close();
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public async Task InactiveRenameUpdatesExistingTabAndReopeningKeepsItsDirtyBuffer()
    {
        var root = CreateTabWorkspace();
        try
        {
            var oldPath = Path.Combine(root, "old.md");
            var newPath = Path.Combine(root, "new.md");
            await File.WriteAllTextAsync(oldPath, "saved");
            var window = CreateWindow(root);
            await window.LoadFolderAsync(root);
            await window.OpenDocumentAsync(oldPath);
            window.SetEditorTextForTesting("dirty renamed");
            var tab = window.TabsForTesting[0];
            await window.NewTabForTestingAsync();
            File.Move(oldPath, newPath);
            window.HandleRenameForTesting(oldPath, newPath);
            await window.OpenDocumentAsync(newPath);
            Assert.Equal(2, window.TabsForTesting.Count);
            Assert.Equal(newPath, tab.Session.Path);
            Assert.Equal("dirty renamed", window.EditorTextForTesting);
            Assert.Contains("new.md", tab.Label);
            window.UnsavedChoiceForTesting = _ => Task.FromResult("Discard");
            await window.ConfirmAllTabsForTestingAsync();
            window.Close();
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public async Task EveryProtectedDraftIsOfferedAndLaterRetainsOnlyTheDeferredWork()
    {
        var root = CreateTabWorkspace();
        try
        {
            var state = new SqliteStateStore(new AppDataPaths(Path.Combine(root, "state")), new NullAxiomLog());
            await state.InitializeAsync();
            foreach (var key in new[] { "restore", "later", "discard" })
                await state.SaveDraftAsync(new RecoveryDraft(key, null, key + " protected", DateTimeOffset.UtcNow, null, true));
            var window = CreateWindow(root);
            var offered = new List<string>();
            window.RecoveryChoiceForTesting = draft =>
            {
                offered.Add(draft.DraftKey);
                return Task.FromResult(draft.DraftKey == "restore" ? "Restore" : draft.DraftKey == "later" ? "Later" : "Discard");
            };
            await window.RestoreDraftsForTestingAsync();
            Assert.Equal(3, offered.Count);
            Assert.Equal("restore protected", Assert.Single(window.TabsForTesting).Session.Text);
            Assert.Equal(2, (await state.GetRecoverableDraftsAsync()).Count);
            window.UnsavedChoiceForTesting = _ => Task.FromResult("Discard");
            await window.ConfirmAllTabsForTestingAsync();
            Assert.Equal("later", Assert.Single(await state.GetRecoverableDraftsAsync()).DraftKey);
            window.Close();
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public async Task EditorScrollIsRestoredAfterSwitchingFromAShortDocument()
    {
        var root = CreateTabWorkspace();
        try
        {
            var path = Path.Combine(root, "long.md");
            await File.WriteAllTextAsync(path, string.Join('\n', Enumerable.Range(1, 500).Select(number => $"Line {number}")));
            var window = CreateWindow(root);
            var preview = window.FindControl<NativeWebView>("Preview")!;
            ((Panel)preview.Parent!).Children.Remove(preview);
            window.Show();
            await window.OpenDocumentAsync(path);
            window.UpdateLayout();
            var editor = window.FindControl<AvaloniaEdit.TextEditor>("Editor")!;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            editor.TextArea.TextView.EnsureVisualLines();
            ((Avalonia.Controls.Primitives.ILogicalScrollable)editor.TextArea.TextView).Offset = new Avalonia.Vector(0, 600);
            window.UpdateLayout();
            var offset = editor.TextArea.TextView.VerticalOffset;
            Assert.True(offset > 0, $"Editor={editor.Bounds}; View={editor.TextArea.TextView.Bounds}; Extent={editor.ExtentHeight}; Lines={editor.Document.LineCount}");
            var first = window.TabsForTesting[0];
            await window.NewTabForTestingAsync();
            window.UpdateLayout();
            await window.SelectTabForTestingAsync(first);
            window.UpdateLayout();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.True(Math.Abs(offset - editor.TextArea.TextView.VerticalOffset) < 0.01, $"Expected={offset}; Actual={editor.TextArea.TextView.VerticalOffset}; Cached={first.VerticalOffset}; Extent={editor.ExtentHeight}; Active={window.FindControl<ListBox>("DocumentTabs")!.SelectedItem == first}");
            window.Close();
        }
        finally { Directory.Delete(root, true); }
    }

    private static string CreateTabWorkspace()
    {
        var root = Path.Combine(Path.GetTempPath(), "AxiomTabs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    [AvaloniaFact]
    public async Task InFlightRecoveryBelongsToItsOriginalTabAndCannotRecreateADiscardedDraft()
    {
        var root = CreateTabWorkspace();
        try
        {
            var state = new SqliteStateStore(new AppDataPaths(Path.Combine(root, "state")), new NullAxiomLog());
            await state.InitializeAsync();
            var recovery = new DelayedRecoveryStore(state);
            var files = new FileDocumentStore(new NullAxiomLog());
            var window = new Axiom.App.MainWindow(new DocumentService(files, recovery, state), new WorkspaceService(state),
                files, recovery, state, AppearancePreference.System, new MarkdownPreviewService(), new ExternalFileMonitor(), new NullAxiomLog(), () => Task.CompletedTask);
            window.SetEditorTextForTesting("first draft");
            var first = window.TabsForTesting[0];
            var protecting = window.ProtectIfNeededForTestingAsync();
            Assert.Equal("first draft", (await recovery.Started.Task.WaitAsync(TimeSpan.FromSeconds(5))).Text);
            await window.NewTabForTestingAsync();
            window.SetEditorTextForTesting("second draft");
            window.UnsavedChoiceForTesting = _ => Task.FromResult("Discard");
            var closing = window.CloseTabForTestingAsync(first);
            Assert.False(closing.IsCompleted);
            await window.SelectTabForTestingAsync(window.TabsForTesting[1]);
            recovery.Release.TrySetResult();
            await protecting;
            Assert.True(await closing);
            await window.ProtectIfNeededForTestingAsync();
            Assert.Equal("second draft", Assert.Single(await state.GetRecoverableDraftsAsync()).Text);
            await window.ConfirmAllTabsForTestingAsync();
            window.Close();
        }
        finally { Directory.Delete(root, true); }
    }
}
