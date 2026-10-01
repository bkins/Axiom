using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using AvaloniaEdit;
using Xunit;

namespace Axiom.App.Tests;

public sealed partial class MainWindowTests
{
    [AvaloniaFact]
    public async Task DocumentFindAndReplaceOperateOnUnsavedBufferAndUndoWithoutSaving()
    {
        var root = CreateTabWorkspace();
        MainWindow? window = null;
        try
        {
            var sourcePath = Path.Combine(root, "find.md");
            const string savedText = "# Café\n\nSaved text";
            await File.WriteAllTextAsync(sourcePath, savedText);
            window = CreateWindow(root);
            window.FindControl<Grid>("EditorPreviewLayout")!.Children.Remove(window.FindControl<NativeWebView>("Preview")!);
            window.Show();
            await window.LoadFolderAsync(root);
            await window.OpenDocumentAsync(sourcePath);
            const string workingText = "# Café\n\nNeedle café Needle";
            window.SetEditorTextForTesting(workingText);
            var editor = window.FindControl<TextEditor>("Editor")!;
            var panel = Assert.IsType<AvaloniaEdit.Search.SearchPanel>(editor.SearchPanel);
            panel.Open();
            panel.SearchPattern = "Needle";
            panel.FindNext(0);
            Assert.Equal("Needle", editor.SelectedText);
            Assert.Equal(workingText, window.EditorTextForTesting);
            Assert.Equal(savedText, await File.ReadAllTextAsync(sourcePath));

            panel.IsReplaceMode = true;
            panel.ReplacePattern = "Thread";
            panel.ReplaceAll();
            Assert.Equal("# Café\n\nThread café Thread", window.EditorTextForTesting);
            Assert.True(window.TabsForTesting[0].Session.IsDirty);
            Assert.Equal(savedText, await File.ReadAllTextAsync(sourcePath));
            editor.Undo();
            Assert.Equal(workingText, window.EditorTextForTesting);
            panel.Close();
        }
        finally
        {
            window?.Close();
            Directory.Delete(root, recursive: true);
        }
    }
}
