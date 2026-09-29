using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Axiom.Core;
using Axiom.Infrastructure;
using Xunit;

namespace Axiom.App.Tests;

public sealed partial class MainWindowTests
{
    [AvaloniaFact]
    public async Task NormalCloseAndStartupRestoreCleanTabOrderActiveSelectionAndPositionsWithoutWritingMarkdown()
    {
        var root = CreateTabWorkspace();
        try
        {
            var paths = new[] { "first.md", "second.md", "third.md" }.Select(name => Path.Combine(root, name)).ToArray();
            foreach (var path in paths) await File.WriteAllTextAsync(path, "# Heading\n\nOrdinary Markdown");
            var window = CreateWindow(root);
            await window.LoadFolderAsync(root);
            foreach (var path in paths) await window.OpenDocumentAsync(path);
            await window.SelectTabForTestingAsync(window.TabsForTesting[0]);
            window.SetEditorSelectionForTesting(3, 4);
            await window.SelectTabForTestingAsync(window.TabsForTesting[1]);
            window.SetEditorSelectionForTesting(11, 8);
            window.TabsForTesting[0].VerticalOffset = 400;
            window.TabsForTesting[0].PreviewScrollRatio = 0.6;
            Assert.True(await window.RunSessionTransitionForTestingAsync(() => Task.CompletedTask));
            window.Close();
            var reopened = CreateWindow(root);
            await reopened.InitializeSessionForTestingAsync();
            Assert.Equal(paths, reopened.TabsForTesting.Select(tab => tab.Session.Path));
            Assert.Equal(paths[1], reopened.PathContextForTesting);
            Assert.Equal("Ordinary", reopened.EditorSelectedTextForTesting);
            Assert.Equal(400, reopened.TabsForTesting[0].VerticalOffset);
            Assert.Equal(0.6, reopened.TabsForTesting[0].PreviewScrollRatio);
            Assert.DoesNotContain(reopened.TabsForTesting, tab => tab.Session.IsDirty);
            foreach (var path in paths) Assert.Equal("# Heading\n\nOrdinary Markdown", await File.ReadAllTextAsync(path));
            reopened.Close();
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public async Task WorkspaceSwitchRemembersAndRestoresEachCleanOpenSet()
    {
        var root = CreateTabWorkspace();
        try
        {
            var first = Path.Combine(root, "first");
            var second = Path.Combine(root, "second");
            Directory.CreateDirectory(first);
            Directory.CreateDirectory(second);
            var firstPath = Path.Combine(first, "a.md");
            var secondPath = Path.Combine(second, "b.md");
            await File.WriteAllTextAsync(firstPath, "first");
            await File.WriteAllTextAsync(secondPath, "second");
            var window = CreateWindow(root);
            Assert.True(await window.SwitchWorkspaceAsync(first));
            await window.OpenDocumentAsync(firstPath);
            Assert.True(await window.SwitchWorkspaceAsync(second));
            await window.OpenDocumentAsync(secondPath);
            Assert.True(await window.SwitchWorkspaceAsync(first));
            Assert.Equal(firstPath, Assert.Single(window.TabsForTesting).Session.Path);
            Assert.True(await window.SwitchWorkspaceAsync(second));
            Assert.Equal(secondPath, Assert.Single(window.TabsForTesting).Session.Path);
            window.Close();
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public async Task RestoreSkipsMissingEscapedAndNonMarkdownPathsAndClampsChangedDocumentLocations()
    {
        var root = CreateTabWorkspace();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "changed.md"), "new");
            var window = CreateWindow(root);
            await window.LoadFolderAsync(root);
            var state = new SqliteStateStore(new AppDataPaths(Path.Combine(root, "state")));
            var workspace = (await state.GetActiveWorkspaceAsync())!;
            var records = new[] { "missing.md", "../outside.md", "unsupported.txt", "changed.md", "CHANGED.md" }
                .Select(path => new WorkspaceDocumentLocation(path, int.MaxValue, int.MaxValue, int.MaxValue, -10, -20, 5)).ToArray();
            await state.SaveWorkspaceSessionAsync(workspace.WorkspaceId, new WorkspaceDocumentSession(1, "changed.md", records));
            await window.RestoreCleanSessionForTestingAsync();
            Assert.Single(window.TabsForTesting);
            Assert.Equal("new", window.EditorTextForTesting);
            Assert.Equal(3, window.EditorCaretOffsetForTesting);
            Assert.Equal(0, window.TabsForTesting[0].SelectionLength);
            Assert.Equal(0, window.TabsForTesting[0].VerticalOffset);
            Assert.Equal(1, window.TabsForTesting[0].PreviewScrollRatio);
            Assert.Contains("Skipped remembered documents", window.FindControl<TextBlock>("StatusText")!.Text);
            Assert.Contains("missing.md", window.FindControl<TextBlock>("StatusText")!.Text);
            Assert.Equal("new", await File.ReadAllTextAsync(Path.Combine(root, "changed.md")));
            window.Close();
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public async Task RecoveryReplacesItsCleanRestoredTabAndNeverCreatesADuplicatePathOrWritesTheDraftToDisk()
    {
        var root = CreateTabWorkspace();
        try
        {
            var path = Path.Combine(root, "saved.md");
            await File.WriteAllTextAsync(path, "saved text");
            var original = CreateWindow(root);
            await original.LoadFolderAsync(root);
            await original.OpenDocumentAsync(path);
            Assert.True(await original.PersistCleanSessionForTestingAsync());
            original.Close();
            var state = new SqliteStateStore(new AppDataPaths(Path.Combine(root, "state")));
            await state.SaveDraftAsync(new RecoveryDraft("protected", path, "recovered edits", DateTimeOffset.UtcNow, File.GetLastWriteTimeUtc(path), false));
            var reopened = CreateWindow(root);
            reopened.RecoveryChoiceForTesting = _ => Task.FromResult("Restore");
            await reopened.InitializeSessionForTestingAsync();
            Assert.Equal(path, Assert.Single(reopened.TabsForTesting).Session.Path);
            Assert.Equal("recovered edits", reopened.EditorTextForTesting);
            Assert.True(reopened.TabsForTesting[0].Session.IsDirty);
            Assert.Equal("saved text", await File.ReadAllTextAsync(path));
            reopened.UnsavedChoiceForTesting = _ => Task.FromResult("Discard");
            await reopened.ConfirmAllTabsForTestingAsync();
            reopened.Close();
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public async Task SuccessfulSaveThenCancelStopsTheTransitionAndRetainsEveryRemainingBuffer()
    {
        var root = CreateTabWorkspace();
        try
        {
            var first = Path.Combine(root, "first.md");
            var second = Path.Combine(root, "second.md");
            await File.WriteAllTextAsync(first, "first");
            await File.WriteAllTextAsync(second, "second");
            var window = CreateWindow(root);
            await window.LoadFolderAsync(root);
            await window.OpenDocumentAsync(first);
            window.SetEditorTextForTesting("saved first edit");
            await window.OpenDocumentAsync(second);
            window.SetEditorTextForTesting("second edit");
            window.UnsavedChoiceForTesting = session => Task.FromResult(session.Path == first ? "Save" : "Cancel");
            var continued = false;
            Assert.False(await window.RunSessionTransitionForTestingAsync(() => { continued = true; return Task.CompletedTask; }));
            Assert.False(continued);
            Assert.Equal(2, window.TabsForTesting.Count);
            Assert.False(window.TabsForTesting[0].Session.IsDirty);
            Assert.True(window.TabsForTesting[1].Session.IsDirty);
            Assert.Equal("saved first edit", await File.ReadAllTextAsync(first));
            Assert.Equal("second", await File.ReadAllTextAsync(second));
            Assert.True(window.FindControl<AvaloniaEdit.TextEditor>("Editor")!.IsEnabled);
            window.UnsavedChoiceForTesting = _ => Task.FromResult("Discard");
            await window.ConfirmAllTabsForTestingAsync();
            window.Close();
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public async Task PersistenceFailureStopsCloseWorkspaceAndPortableContinuationWithoutLosingTheCleanTabs()
    {
        var root = CreateTabWorkspace();
        try
        {
            var path = Path.Combine(root, "saved.md");
            await File.WriteAllTextAsync(path, "saved");
            var window = CreateWindow(root);
            await window.LoadFolderAsync(root);
            await window.OpenDocumentAsync(path);
            window.SessionStoreForTesting = new FaultingWorkspaceSessionStore();
            var continued = false;
            Assert.False(await window.RunSessionTransitionForTestingAsync(() => { continued = true; return Task.CompletedTask; }));
            Assert.False(continued);
            Assert.Single(window.TabsForTesting);
            Assert.Equal("saved", window.EditorTextForTesting);
            Assert.True(window.FindControl<ListBox>("DocumentTabs")!.IsEnabled);
            Assert.Contains("Injected session persistence failure", window.FindControl<TextBlock>("StatusText")!.Text);
            window.SessionStoreForTesting = null;
            window.Close();
        }
        finally { Directory.Delete(root, true); }
    }
}
