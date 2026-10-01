using Avalonia.Headless.XUnit;
using Axiom.Core;
using Xunit;

namespace Axiom.App.Tests;

public sealed partial class MainWindowTests
{
    [AvaloniaFact]
    public async Task FormattingCommandsPreserveIdentityDiskAndOneUndoWhileUnavailableMutationsDoNothing()
    {
        var root = CreateTabWorkspace();
        try
        {
            var path = Path.Combine(root, "protected.md");
            var source = "<!-- axiom:documentId=" + Guid.NewGuid().ToString("D") + " -->\n\nalpha";
            await File.WriteAllTextAsync(path, source);
            var window = CreateWindow(root);
            await window.OpenDocumentAsync(path);
            window.SetEditorSelectionForTesting(0, 5);
            Assert.Equal(CommandOutcome.Cancelled, (await window.ExecuteCommandAsync("editor.format.bold")).Outcome);
            Assert.Equal(source, window.EditorTextForTesting);
            window.SetEditorSelectionForTesting(source.Length - 5, 5);
            Assert.Equal(CommandOutcome.Completed, (await window.ExecuteCommandAsync("editor.format.bold")).Outcome);
            Assert.EndsWith("**alpha**", window.EditorTextForTesting);
            Assert.Equal(source, await File.ReadAllTextAsync(path));
            await window.ExecuteCommandAsync("editor.undo");
            Assert.Equal(source, window.EditorTextForTesting);
            Assert.Equal(CommandOutcome.Unavailable, (await window.ExecuteCommandAsync("workspace.manage.recycle")).Outcome);
            Assert.Equal(source, await File.ReadAllTextAsync(path));
            window.Close();
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public async Task RebindingAndDisablingNewRouteOnlyEffectiveGestureThroughDispatcher()
    {
        var root = CreateTabWorkspace();
        try
        {
            var window = CreateWindow(root);
            window.ConfigureShortcutsForTesting(new() { Overrides = new() { ["document.new"] = "Ctrl+J" } });
            var originalCount = window.TabsForTesting.Count;
            Assert.False(await window.ExecuteShortcutForTestingAsync(new() { Key = Avalonia.Input.Key.N, KeyModifiers = Avalonia.Input.KeyModifiers.Control }));
            Assert.Equal(originalCount, window.TabsForTesting.Count);
            var selected = new Avalonia.Input.KeyEventArgs { Key = Avalonia.Input.Key.J, KeyModifiers = Avalonia.Input.KeyModifiers.Control };
            Assert.True(await window.ExecuteShortcutForTestingAsync(selected));
            Assert.True(selected.Handled);
            Assert.Equal(originalCount + 1, window.TabsForTesting.Count);
            window.ConfigureShortcutsForTesting(new() { Overrides = new() { ["document.new"] = null } });
            Assert.False(await window.ExecuteShortcutForTestingAsync(new() { Key = Avalonia.Input.Key.J, KeyModifiers = Avalonia.Input.KeyModifiers.Control }));
            Assert.False(await window.ExecuteShortcutForTestingAsync(new() { Key = Avalonia.Input.Key.N, KeyModifiers = Avalonia.Input.KeyModifiers.Control }));
            Assert.Equal(originalCount + 1, window.TabsForTesting.Count);
            window.Close();
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public async Task SharedNewSaveAndUndoCommandsRetainDirtyTabsAndUseExistingPersistence()
    {
        var root = CreateTabWorkspace();
        try
        {
            var path = Path.Combine(root, "saved.md");
            await File.WriteAllTextAsync(path, "# Saved\n\ncontent");
            var window = CreateWindow(root);
            await window.LoadFolderAsync(root);
            await window.OpenDocumentAsync(path);
            window.SetEditorSelectionForTesting(9, 7);
            Assert.True(window.ApplyMarkdownEditForTesting(MarkdownEditKind.Bold));
            Assert.Equal(CommandOutcome.Completed, (await window.ExecuteCommandAsync("editor.undo")).Outcome);
            Assert.Equal("# Saved\n\ncontent", window.EditorTextForTesting);
            Assert.Equal(CommandOutcome.Completed, (await window.ExecuteCommandAsync("editor.redo")).Outcome);
            var dirty = window.EditorTextForTesting;
            Assert.Equal(CommandOutcome.Completed, (await window.ExecuteCommandAsync("document.new")).Outcome);
            Assert.Equal(2, window.TabsForTesting.Count);
            Assert.Equal(dirty, window.TabsForTesting[0].Session.Text);
            Assert.Equal("# Saved\n\ncontent", await File.ReadAllTextAsync(path));
            await window.SelectTabForTestingAsync(window.TabsForTesting[0]);
            Assert.Equal(CommandOutcome.Completed, (await window.ExecuteCommandAsync("document.save")).Outcome);
            Assert.Equal(dirty, await File.ReadAllTextAsync(path));
            Assert.False(window.TabsForTesting[0].Session.IsDirty);
            Assert.Equal(CommandOutcome.Unavailable, (await window.ExecuteCommandAsync("unknown")).Outcome);
            window.Close();
        }
        finally { Directory.Delete(root, true); }
    }
}
