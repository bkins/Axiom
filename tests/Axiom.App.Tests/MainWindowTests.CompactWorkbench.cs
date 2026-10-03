using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using AvaloniaEdit;
using Axiom.Core;
using Xunit;

namespace Axiom.App.Tests;

public sealed partial class MainWindowTests
{
    [AvaloniaFact]
    public async Task FilterDisclosureAndDisabledReasonRetainDraftAndRefuseUnavailableAction()
    {
        var root = CreateTabWorkspace();
        try
        {
            var window = CreateWindow(root);
            var preview = window.FindControl<NativeWebView>("Preview")!;
            ((Panel)preview.Parent!).Children.Remove(preview);
            window.Show();
            await window.StartupCompleted;
            window.SetEditorTextForTesting("Protected draft");
            var editor = window.FindControl<TextEditor>("Editor")!;
            editor.Document.Insert(editor.Document.TextLength, "!");
            Assert.True(editor.Document.UndoStack.CanUndo);
            await window.ExecuteCommandAsync("workspace.filter");
            var filter = window.FindControl<TextBox>("ExplorerFilter")!;
            filter.Text = "retained query";
            filter.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            Assert.Equal("retained query", filter.Text);
            Assert.False(window.FindControl<Grid>("ExplorerFilterPanel")!.IsVisible);
            var host = window.FindControl<Border>("InsertImageCommandHost")!;
            Assert.True(host.Focusable);
            host.Focus();
            Assert.Contains("Save", window.FindControl<TextBlock>("StatusText")!.Text!);
            Assert.Equal(CommandOutcome.Unavailable, (await window.ExecuteCommandAsync("editor.insertImage")).Outcome);
            Assert.Equal("Protected draft!", window.EditorTextForTesting);
            Assert.True(editor.Document.UndoStack.CanUndo);
            await CloseWorkbenchTestAsync(window);
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public async Task MetadataRefreshReportsSavedCountsAndUnchangedWithoutSavingDirtySource()
    {
        var root = CreateTabWorkspace();
        try
        {
            var path = Path.Combine(root, "one.md");
            var source = DocumentIdCodec.Enroll("# Saved", Guid.NewGuid());
            await File.WriteAllTextAsync(path, source);
            var window = CreateWindow(root);
            await window.LoadFolderAsync(root);
            await window.OpenDocumentAsync(path);
            window.SetEditorTextForTesting(source + "\nUnsaved text");
            Assert.True(await window.RefreshClassificationAsync());
            var status = window.FindControl<TextBlock>("MetadataRefreshStatus")!;
            Assert.Contains("1 saved files, 1 eligible identities", status.Text);
            Assert.True(await window.RefreshClassificationAsync());
            Assert.StartsWith("Unchanged", status.Text);
            Assert.EndsWith("Unsaved text", window.EditorTextForTesting);
            Assert.Equal(source, await File.ReadAllTextAsync(path));
            await CloseWorkbenchTestAsync(window);
        }
        finally { Directory.Delete(root, true); }
    }
}
