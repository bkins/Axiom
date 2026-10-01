using Avalonia.Headless.XUnit;
using Axiom.Core;
using Axiom.Infrastructure;
using Xunit;

namespace Axiom.App.Tests;

public sealed partial class MainWindowTests
{
    [AvaloniaFact]
    public async Task TypingDuringRestoreProtectionRetainsNewTextAndReprotectsIt()
    {
        var root = CreateTabWorkspace();
        try
        {
            var state = new SqliteStateStore(new AppDataPaths(Path.Combine(root, "state")));
            await state.InitializeAsync();
            var recovery = new DelayedRecoveryStore(state);
            var files = new FileDocumentStore();
            var path = Path.Combine(root, "notes.md");
            await File.WriteAllTextAsync(path, "saved original");
            var window = new MainWindow(new DocumentService(files, recovery, state), new WorkspaceService(state), files,
                recovery, state, AppearancePreference.System, new MarkdownPreviewService(), new ExternalFileMonitor(), new NullAxiomLog(), () => Task.CompletedTask);
            await window.LoadFolderAsync(root);
            await window.OpenDocumentAsync(path);
            var restoring = window.RestoreHistoricalBufferAsync("historical");
            await recovery.Started.Task;
            window.SetEditorTextForTesting("typed during protection");
            recovery.Release.TrySetResult();
            await Assert.ThrowsAsync<IOException>(() => restoring);
            Assert.Equal("typed during protection", window.EditorTextForTesting);
            Assert.Contains(await state.GetRecoverableDraftsAsync(), draft => draft.Text == "typed during protection");
            Assert.Equal("saved original", await File.ReadAllTextAsync(path));
            window.UnsavedChoiceForTesting = _ => Task.FromResult("Discard");
            await window.ConfirmAllTabsForTestingAsync();
            window.Close();
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public async Task AtomicSaveBackupRenameDoesNotRetargetOpenDocument()
    {
        var root = CreateTabWorkspace();
        try
        {
            var path = Path.Combine(root, "notes.md");
            var backup = Path.Combine(root, ".notes.md." + Guid.NewGuid().ToString("N") + ".tmp.bak");
            await File.WriteAllTextAsync(path, "saved original");
            var window = CreateWindow(root);
            await window.LoadFolderAsync(root);
            await window.OpenDocumentAsync(path);
            await window.HandleRenameForTestingAsync(path, backup);
            Assert.Equal(path, window.TabsForTesting[0].Session.Path);
            Assert.Equal("saved original", window.EditorTextForTesting);
            window.SetEditorTextForTesting("deliberate saved revision");
            Assert.Equal(CommandOutcome.Completed, (await window.ExecuteCommandAsync("document.save")).Outcome);
            await window.HandleRenameForTestingAsync(path, backup);
            Assert.Equal(path, window.TabsForTesting[0].Session.Path);
            Assert.Equal("deliberate saved revision", await File.ReadAllTextAsync(path));
            window.Close();
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public async Task HistoricalRestoreProtectsDirtyBufferPreservesIdentityDiskAndUndoUntilSave()
    {
        var root = CreateTabWorkspace();
        try
        {
            var identity = Guid.NewGuid();
            var path = Path.Combine(root, "notes.md");
            var saved = DocumentIdCodec.Enroll("current saved", identity);
            await File.WriteAllTextAsync(path, saved);
            var window = CreateWindow(root);
            await window.LoadFolderAsync(root);
            await window.OpenDocumentAsync(path);
            await window.RestoreHistoricalBufferAsync(DocumentIdCodec.Enroll("historical body", Guid.NewGuid()));
            var restored = window.EditorTextForTesting;
            Assert.Equal(identity, DocumentIdCodec.Find(restored));
            Assert.EndsWith("historical body", restored);
            Assert.True(window.TabsForTesting[0].Session.IsDirty);
            Assert.Equal(saved, await File.ReadAllTextAsync(path));
            var state = new SqliteStateStore(new AppDataPaths(Path.Combine(root, "state")));
            Assert.Contains(await state.GetRecoverableDraftsAsync(), draft => draft.Text == restored);
            window.UndoEditorForTesting();
            Assert.Equal(saved, window.EditorTextForTesting);
            await window.ExecuteCommandAsync("editor.redo");
            Assert.Equal(restored, window.EditorTextForTesting);
            Assert.Equal(CommandOutcome.Completed, (await window.ExecuteCommandAsync("document.save")).Outcome);
            Assert.Equal(restored, await File.ReadAllTextAsync(path));
            Assert.Empty(await state.GetRecoverableDraftsAsync());
            window.Close();
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public async Task RecoveryFailureAndUnresolvedDirtyWorkPreventRestoreBeforeChangingSource()
    {
        var root = CreateTabWorkspace();
        try
        {
            var state = new SqliteStateStore(new AppDataPaths(Path.Combine(root, "state")));
            await state.InitializeAsync();
            var recovery = new FailingRecoveryStore(state);
            var files = new FileDocumentStore();
            var path = Path.Combine(root, "notes.md");
            await File.WriteAllTextAsync(path, "saved original");
            var window = new MainWindow(new DocumentService(files, recovery, state), new WorkspaceService(state), files,
                recovery, state, AppearancePreference.System, new MarkdownPreviewService(), new ExternalFileMonitor(), new NullAxiomLog(), () => Task.CompletedTask);
            await window.LoadFolderAsync(root);
            await window.OpenDocumentAsync(path);
            await Assert.ThrowsAsync<IOException>(() => window.RestoreHistoricalBufferAsync("historical"));
            Assert.Equal("saved original", window.EditorTextForTesting);
            Assert.Equal("saved original", await File.ReadAllTextAsync(path));
            window.SetEditorTextForTesting("current unsaved");
            await Assert.ThrowsAsync<IOException>(() => window.RestoreHistoricalBufferAsync("historical"));
            Assert.Equal("current unsaved", window.EditorTextForTesting);
            window.SetEditorTextForTesting("saved original");
            window.Close();
        }
        finally { Directory.Delete(root, true); }
    }
}
