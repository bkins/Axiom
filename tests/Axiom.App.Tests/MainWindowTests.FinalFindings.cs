using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Axiom.Core;
using Xunit;

namespace Axiom.App.Tests;

public sealed partial class MainWindowTests
{
    [AvaloniaFact]
    public async Task ControlSSavesInactiveDirtyTabsAndRetainsActiveDocumentAndUndo()
    {
        var root = CreateTabWorkspace();
        try
        {
            var window = CreateWindow(root);
            await window.LoadFolderAsync(root);
            var paths = new[] { "first.md", "second.md", "clean.md" }.Select(name => Path.Combine(root, name)).ToArray();
            foreach (var path in paths) await File.WriteAllTextAsync(path, "saved");
            await window.OpenDocumentAsync(paths[0]);
            window.TabsForTesting[0].Document.Insert(5, " first dirty");
            await window.OpenDocumentAsync(paths[1]);
            window.TabsForTesting[1].Document.Insert(5, " second dirty");
            var active = window.TabsForTesting[1];
            await window.OpenDocumentAsync(paths[2]);
            await window.SelectTabForTestingAsync(active);
            window.ConfigureShortcutsForTesting(new());
            Assert.True(await window.ExecuteShortcutForTestingAsync(new KeyEventArgs { Key = Key.S, KeyModifiers = KeyModifiers.Control }));
            Assert.Equal("saved first dirty", await File.ReadAllTextAsync(paths[0]));
            Assert.Equal("saved second dirty", await File.ReadAllTextAsync(paths[1]));
            Assert.Equal("saved", await File.ReadAllTextAsync(paths[2]));
            Assert.All(window.TabsForTesting, tab => Assert.False(tab.Session.IsDirty));
            Assert.Same(active, window.FindControl<ListBox>("DocumentTabs")!.SelectedItem);
            Assert.True(window.EditorCanUndoForTesting);
            window.UndoEditorForTesting();
            Assert.True(active.Session.IsDirty);
            window.UnsavedChoiceForTesting = _ => Task.FromResult("Discard");
            window.Close();
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public async Task SaveAllStopsAtExternalConflictAndRetainsLaterDirtyBuffers()
    {
        var root = CreateTabWorkspace();
        try
        {
            var window = CreateWindow(root);
            await window.LoadFolderAsync(root);
            var paths = new[] { "first.md", "conflict.md", "later.md" }.Select(name => Path.Combine(root, name)).ToArray();
            foreach (var path in paths)
            {
                await File.WriteAllTextAsync(path, "saved");
                await window.OpenDocumentAsync(path);
                window.SetEditorTextForTesting("local edits");
            }
            await File.WriteAllTextAsync(paths[1], "external edits");
            var result = await window.ExecuteCommandAsync("document.saveAll");
            Assert.Equal(CommandOutcome.Cancelled, result.Outcome);
            Assert.Equal("local edits", await File.ReadAllTextAsync(paths[0]));
            Assert.Equal("external edits", await File.ReadAllTextAsync(paths[1]));
            Assert.Equal("saved", await File.ReadAllTextAsync(paths[2]));
            Assert.True(window.TabsForTesting[1].Session.IsDirty);
            Assert.True(window.TabsForTesting[2].Session.IsDirty);
            Assert.Equal("local edits", window.EditorTextForTesting);
            window.UnsavedChoiceForTesting = _ => Task.FromResult("Discard");
            window.Close();
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public async Task WorkspaceTreeFillsBottomWhenNoWarningIsPresent()
    {
        var root = CreateTabWorkspace();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "document.md"), "# Document");
            var window = CreateWindow(root);
            await window.LoadFolderAsync(root);
            var layout = window.FindControl<Grid>("EditorPreviewLayout")!;
            layout.Children.Remove(window.FindControl<NativeWebView>("Preview")!);
            window.Show();
            await window.StartupCompleted;
            window.UpdateLayout();
            var warning = window.FindControl<TextBlock>("DuplicateWarning")!;
            var tree = window.FindControl<TreeView>("FileTree")!;
            var sidebar = window.FindControl<Border>("WorkspaceSidebar")!;
            Assert.False(warning.IsVisible);
            Assert.Equal(sidebar.Bounds.Height - sidebar.Padding.Top - sidebar.Padding.Bottom, tree.Bounds.Bottom, 1);
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            window.Closed += (_, _) => closed.TrySetResult();
            window.Close();
            await closed.Task;
        }
        finally { Directory.Delete(root, true); }
    }
}
