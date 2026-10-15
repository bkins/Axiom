using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using AvaloniaEdit;
using Axiom.Core;
using Xunit;

namespace Axiom.App.Tests;

public sealed partial class MainWindowTests
{
    [AvaloniaFact]
    public async Task DiscoveryLayoutAndCommandPlacementRetainExactEditorAndResultObjects()
    {
        var root = CreateTabWorkspace();
        try
        {
            var path = Path.Combine(root, "a.md"); await File.WriteAllTextAsync(path, "# Saved\n\nretained passage\n");
            var window = CreateWindow(root);
            var preview = window.FindControl<NativeWebView>("Preview")!; ((Panel)preview.Parent!).Children.Remove(preview);
            window.Show(); await window.StartupCompleted;
            await window.SwitchWorkspaceAsync(root); await window.OpenDocumentAsync(path);
            var editor = window.FindControl<TextEditor>("Editor")!;
            editor.Document.Insert(editor.Document.TextLength, "draft");
            var document = editor.Document; var source = editor.Text; editor.Select(1, 2);
            var query = window.FindControl<TextBox>("WorkspaceSearchBox")!; query.Text = "retained";
            await window.SearchWorkspaceForTestingAsync(query.Text);
            var results = window.FindControl<ListBox>("WorkspaceSearchResults")!; results.SelectedIndex = 0;
            var selected = results.SelectedItem;
            await window.ExecuteCommandAsync("navigation.returnToDiscovery");
            await window.ExecuteCommandAsync("view.discoveryPlacement");
            Assert.Equal(2, Grid.GetRow(window.FindControl<Border>("ToolWindowHost")!));
            Assert.Equal(DiscoveryPlacement.Bottom, window.EditorPreferencesForTesting.DiscoveryPlacement);
            window.ApplyEditorPreferencesForTesting(window.EditorPreferencesForTesting with { CommandPlacement = CommandPlacement.Side });
            Assert.True(window.FindControl<Border>("SideCommandHost")!.IsVisible);
            Assert.Equal(ToolbarLayout.Read(ToolbarLayout.DefaultCommands), window.VisibleToolbarIdsForTesting);
            await window.ExecuteCommandAsync("view.mode.zen"); Assert.False(window.FindControl<Border>("SideCommandHost")!.IsVisible);
            await window.ExecuteCommandAsync("view.mode.standard"); Assert.True(window.FindControl<Border>("SideCommandHost")!.IsVisible);
            await window.ExecuteCommandAsync("view.discoveryPlacement");
            Assert.Equal(4, Grid.GetColumn(window.FindControl<Border>("ToolWindowHost")!));
            Assert.Same(document, editor.Document); Assert.Equal(source, editor.Text);
            Assert.Equal(1, editor.SelectionStart); Assert.Equal(2, editor.SelectionLength);
            Assert.True(editor.Document.UndoStack.CanUndo); Assert.Same(selected, results.SelectedItem); Assert.Equal("retained", query.Text);
            editor.Document.UndoStack.Undo(); Assert.DoesNotContain("draft", editor.Text);
            Assert.DoesNotContain("draft", await File.ReadAllTextAsync(path));
            Assert.Contains("CP connects automatically", window.FindControl<TextBlock>("DiscoveryContext")!.Text);
            await CloseWorkbenchTestAsync(window);
        }
        finally { Directory.Delete(root, true); }
    }
}
