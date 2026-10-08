using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Axiom.Core;
using Xunit;

namespace Axiom.App.Tests;

public sealed partial class MainWindowTests
{
    [AvaloniaFact]
    public async Task WholeOrderedItemDeletionRenumbersAndUndoesAsOneEdit()
    {
        var root = CreateTabWorkspace();
        try
        {
            var window = CreateWindow(root);
            window.FindControl<Grid>("EditorPreviewLayout")!.Children.Remove(window.FindControl<NativeWebView>("Preview")!);
            window.Show();
            await window.StartupCompleted;
            var editor = window.FindControl<AvaloniaEdit.TextEditor>("Editor")!;
            var text = "5. First\n6. Remove\n7. Last\n";
            window.SetEditorTextForTesting(text);
            window.SetEditorSelectionForTesting("5. First\n".Length, "6. Remove\n".Length);
            editor.TextArea.Focus();
            window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Delete });
            Assert.Equal("5. First\n6. Last\n", window.EditorTextForTesting);
            window.UndoEditorForTesting();
            Assert.Equal(text, window.EditorTextForTesting);
            await CloseWorkbenchTestAsync(window);
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public async Task StationaryDragNearExplorerEdgeScrollsAndStopsOnLeave()
    {
        var root = CreateTabWorkspace();
        try
        {
            for (var index = 0; index < 80; index++) await File.WriteAllTextAsync(Path.Combine(root, $"note-{index:D2}.md"), "saved");
            var window = CreateWindow(root);
            window.FindControl<Grid>("EditorPreviewLayout")!.Children.Remove(window.FindControl<NativeWebView>("Preview")!);
            window.Show();
            await window.StartupCompleted;
            await window.LoadFolderAsync(root);
            window.UpdateLayout();
            var offset = await window.RouteExplorerEdgeScrollForTestingAsync();
            Assert.True(offset > 0);
            Assert.False(window.ExplorerEdgeScrollRunningForTesting);
            await CloseWorkbenchTestAsync(window);
        }
        finally { Directory.Delete(root, true); }
    }
}
