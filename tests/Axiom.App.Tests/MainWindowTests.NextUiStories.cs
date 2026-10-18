using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Axiom.Core;
using Xunit;

namespace Axiom.App.Tests;

public sealed partial class MainWindowTests
{
    [AvaloniaFact]
    public async Task PreviewTasksToggleUnsavedSourceWithOneUndoAndRejectStaleOrForgedRequests()
    {
        var root = CreateTabWorkspace();
        try
        {
            var path = Path.Combine(root, "tasks.md");
            const string source = "# Tasks\r\n- [ ] Task one\r\n- [x] Task two\r\n```md\r\n- [ ] Example\r\n```\r\n";
            await File.WriteAllTextAsync(path, source);
            var window = CreateWindow(root);
            await window.OpenDocumentAsync(path);
            var token = window.PrepareTaskPreviewForTesting();
            var offset = source.IndexOf("[ ]", StringComparison.Ordinal) + 1;
            var request = new Uri($"axiom-task://toggle/{token}/{offset}/0");
            Assert.False(window.TogglePreviewTaskForTesting(new Uri($"axiom-task://toggle/wrong/{offset}/0")));
            Assert.True(window.TogglePreviewTaskForTesting(request));
            Assert.Contains("- [x] Task one", window.EditorTextForTesting);
            Assert.False(window.TogglePreviewTaskForTesting(request));
            Assert.Equal(source, await File.ReadAllTextAsync(path));
            await window.ExecuteCommandAsync("editor.undo");
            Assert.Equal(source, window.EditorTextForTesting);
            token = window.PrepareTaskPreviewForTesting();
            window.SetEditorTextForTesting(source + "new unsaved text");
            Assert.False(window.TogglePreviewTaskForTesting(new Uri($"axiom-task://toggle/{token}/{offset}/0")));
            Assert.Contains("new unsaved text", window.EditorTextForTesting);
            window.SetEditorTextForTesting(source);
            await window.ToggleReadingModeAsync();
            token = window.PrepareTaskPreviewForTesting();
            var checkedOffset = source.IndexOf("[x]", StringComparison.Ordinal) + 1;
            Assert.True(window.TogglePreviewTaskForTesting(new Uri($"axiom-task://toggle/{token}/{checkedOffset}/1")));
            Assert.True(window.ReadingModeForTesting);
            Assert.Contains("- [ ] Task two", window.EditorTextForTesting);
            window.Close();
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public async Task FolderContextHasOneCopyPastePairAndCopyExcludesUnsavedChanges()
    {
        var root = CreateTabWorkspace();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "source", "empty"));
            var path = Path.Combine(root, "source", "note.md");
            await File.WriteAllTextAsync(path, "# Saved snapshot");
            var window = CreateWindow(root);
            await window.LoadFolderAsync(root);
            await window.OpenDocumentAsync(path);
            window.SetEditorTextForTesting("# Unsaved edit");
            var result = await window.ExecuteExplorerCommandForTestingAsync("workspace.copyFile", "source", true);
            Assert.Equal(CommandOutcome.Completed, result.Outcome);
            var snapshot = Assert.IsType<Axiom.Infrastructure.FolderClipboardSnapshot>(window.FolderClipboardForTesting);
            Assert.Equal("# Saved snapshot", System.Text.Encoding.UTF8.GetString(Assert.Single(snapshot.Files).Content));
            var labels = window.WorkspaceContextMenuForTesting("source", true).Items.OfType<MenuItem>().Select(item => item.Header?.ToString()).ToArray();
            Assert.Single(labels, label => label == "Copy");
            Assert.Single(labels, label => label == "Paste");
            Assert.DoesNotContain("Copy folder…", labels);
            Assert.Equal("# Unsaved edit", window.EditorTextForTesting);
            Assert.Equal("# Saved snapshot", await File.ReadAllTextAsync(path));
            window.Close();
        }
        finally { Directory.Delete(root, true); }
    }
}
