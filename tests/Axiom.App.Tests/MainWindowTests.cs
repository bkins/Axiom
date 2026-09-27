using Avalonia.Headless.XUnit;
using Avalonia.Input;
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
            Assert.Equal(1280, window.Width);
            Assert.Equal(720, window.Height);
            await window.LoadFolderAsync(root);

            Assert.False(string.IsNullOrWhiteSpace(window.BuildIdentityForTesting.ProductVersion));
            Assert.Contains(window.BuildIdentityForTesting.ProductVersion, window.BuildIdentityForTesting.DisplayText);
            Assert.Contains(Path.GetFileName(root), window.WorkspaceContextForTesting);
            Assert.Equal(root, window.PathContextForTesting);
            Assert.Contains("Ordinary Markdown", window.DocumentStateForTesting);
            Assert.Contains("New", window.DocumentStateForTesting);
            Assert.False(string.IsNullOrWhiteSpace(window.RecoveryContextForTesting));
            Assert.Equal(["alpha.md", "bravo.md", Path.Combine("nested", "charlie.markdown")], window.FolderItemsForTesting.OrderBy(item => item));
            var nested = Assert.Single(window.WorkspaceTreeForTesting, node => node.Name == "nested");
            Assert.False(nested.IsDocument);
            Assert.Equal("charlie.markdown", Assert.Single(nested.Children).Name);
            await window.OpenDocumentAsync(alpha);
            Assert.Equal("# Café ☕\n\nAlpha", window.EditorTextForTesting);
            Assert.True(window.IsEnrollAvailableForTesting);
            Assert.Contains("Saved", window.DocumentStateForTesting);
            Assert.Equal(alpha, window.PathContextForTesting);
            window.SetEditorTextForTesting(window.EditorTextForTesting + "\nEdit");
            Assert.Contains("Unsaved", window.DocumentStateForTesting);
            window.SetEditorTextForTesting("# Café ☕\n\nAlpha");

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

    [AvaloniaFact]
    public async Task WorkspaceSearchFindsAndOpensANestedMatch()
    {
        var root = Path.Combine(Path.GetTempPath(), $"AxiomUiTests-{Guid.NewGuid():N}");
        var nested = Path.Combine(root, "nested");
        Directory.CreateDirectory(nested);
        try
        {
            var matchPath = Path.Combine(nested, "match.md");
            await File.WriteAllTextAsync(matchPath, "# Search\n\nA unique Needle appears here.");
            var window = CreateWindow(root);
            await window.LoadFolderAsync(root);

            await window.SearchWorkspaceForTestingAsync("needle");
            var result = Assert.Single(window.SearchResultsForTesting);
            await window.OpenSearchResultForTestingAsync(result);

            Assert.Equal(Path.Combine("nested", "match.md"), result.RelativePath);
            Assert.Equal("Needle", window.EditorSelectedTextForTesting);
            window.Close();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task OutlineUsesTheCurrentBufferAndNavigatesDuplicateHeadingsByOffset()
    {
        var root = Path.Combine(Path.GetTempPath(), $"AxiomUiTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "outline.md");
            const string savedText = "# Start\n\n## Repeat\n\nText\n\n## Repeat";
            await File.WriteAllTextAsync(path, savedText);
            var window = CreateWindow(root);
            await window.LoadFolderAsync(root);
            await window.OpenDocumentAsync(path);

            Assert.Equal(3, window.OutlineEntriesForTesting.Count);
            Assert.NotEqual(window.OutlineEntriesForTesting[1].Offset, window.OutlineEntriesForTesting[2].Offset);
            window.SetEditorTextForTesting(window.EditorTextForTesting + "\n\n### Unsaved heading");
            Assert.Equal("Unsaved heading", window.OutlineEntriesForTesting[^1].Text);
            var finalHeading = window.OutlineEntriesForTesting[^1];
            window.NavigateToOutlineEntryForTesting(finalHeading);

            Assert.Equal(finalHeading.Offset, window.EditorCaretOffsetForTesting);
            window.SetEditorTextForTesting(savedText);
            window.Close();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task OrdinaryMarkdownCanBePinnedWithoutChangingTheFile()
    {
        var root = Path.Combine(Path.GetTempPath(), $"AxiomUiTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "ordinary.md");
            const string text = "# Ordinary\n\nNo identity marker.";
            await File.WriteAllTextAsync(path, text);
            var window = CreateWindow(root);
            await window.LoadFolderAsync(root);
            await window.OpenDocumentAsync(path);

            await window.ToggleCurrentPinForTestingAsync();

            Assert.Equal("ordinary.md", Assert.Single(window.WorkspacePinsForTesting).RelativePath);
            Assert.Equal("Unpin", window.PinButtonTextForTesting);
            Assert.Equal(text, await File.ReadAllTextAsync(path));
            File.Delete(path);
            await window.LoadFolderAsync(root);
            Assert.Single(window.WorkspacePinsForTesting);
            window.Close();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task UndoingBackToSavedTextClearsTheCurrentSessionsRecoveryDraft()
    {
        var root = Path.Combine(Path.GetTempPath(), $"AxiomUiTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "recovery.md");
            const string savedText = "# Saved";
            await File.WriteAllTextAsync(path, savedText);
            var paths = new AppDataPaths(Path.Combine(root, "state"));
            paths.EnsureCreated();
            var state = new SqliteStateStore(paths, new NullAxiomLog());
            await state.InitializeAsync();
            var files = new FileDocumentStore(new NullAxiomLog());
            var documents = new DocumentService(files, state, state, new NullAxiomLog());
            var window = new MainWindow(documents, new WorkspaceService(state), files, state, new MarkdownPreviewService(), new ExternalFileMonitor(), new NullAxiomLog(), () => Task.CompletedTask);
            await window.LoadFolderAsync(root);
            await window.OpenDocumentAsync(path);

            window.SetEditorTextForTesting("# Temporary");
            await window.ProtectIfNeededForTestingAsync();
            Assert.Single(await state.GetRecoverableDraftsAsync());

            window.SetEditorTextForTesting(savedText);
            await window.ProtectIfNeededForTestingAsync();

            Assert.Empty(await state.GetRecoverableDraftsAsync());
            Assert.Equal("Recovery ready", window.RecoveryContextForTesting);
            window.Close();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task FormattingActionIsOneUndoableUnsavedEditAndParticipatesInRecovery()
    {
        var root = Path.Combine(Path.GetTempPath(), $"AxiomUiTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "format.md");
            await File.WriteAllTextAsync(path, "Format me");
            var window = CreateWindow(root);
            await window.LoadFolderAsync(root);
            await window.OpenDocumentAsync(path);
            window.SetEditorSelectionForTesting(0, 6);

            Assert.True(window.ApplyMarkdownEditForTesting(MarkdownEditKind.Bold));

            Assert.Equal("**Format** me", window.EditorTextForTesting);
            Assert.Contains("Unsaved", window.DocumentStateForTesting);
            Assert.True(window.EditorCanUndoForTesting);
            await window.ProtectIfNeededForTestingAsync();
            Assert.StartsWith("Protected ", window.RecoveryContextForTesting);

            window.UndoEditorForTesting();

            Assert.Equal("Format me", window.EditorTextForTesting);
            Assert.Contains("Saved", window.DocumentStateForTesting);
            window.Close();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task MarkdownShortcutsAreScopedToTheEditor()
    {
        var root = Path.Combine(Path.GetTempPath(), $"AxiomUiTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "shortcuts.md");
            await File.WriteAllTextAsync(path, "text");
            var window = CreateWindow(root);
            await window.LoadFolderAsync(root);
            await window.OpenDocumentAsync(path);
            window.SetEditorSelectionForTesting(0, 4);

            Assert.False(window.HandleMarkdownShortcutForTesting(Key.B, KeyModifiers.Control, editorHasFocus: false));
            Assert.Equal("text", window.EditorTextForTesting);
            Assert.True(window.HandleMarkdownShortcutForTesting(Key.B, KeyModifiers.Control, editorHasFocus: true));
            Assert.Equal("**text**", window.EditorTextForTesting);
            Assert.False(window.HandleMarkdownShortcutForTesting(Key.B, KeyModifiers.Control | KeyModifiers.Alt, editorHasFocus: true));
            window.UndoEditorForTesting();
            Assert.Equal("text", window.EditorTextForTesting);
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
