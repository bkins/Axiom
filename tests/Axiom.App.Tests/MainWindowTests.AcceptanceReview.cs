using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Axiom.Core;
using Axiom.Infrastructure;
using Xunit;

namespace Axiom.App.Tests;

public sealed partial class MainWindowTests
{
    [AvaloniaTheory]
    [InlineData(MenuStyle.Quiet)]
    [InlineData(MenuStyle.Rounded)]
    public async Task AltAndF10ReachFileCommandsAndAltFShowsTheMenuWithoutEditing(MenuStyle style)
    {
        var root = Path.Combine(Path.GetTempPath(), "AxiomMenuReview-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var window = CreateWindow(root);
            var preview = window.FindControl<NativeWebView>("Preview")!;
            ((Panel)preview.Parent!).Children.Remove(preview);
            window.Show();
            window.ApplyEditorPreferencesForTesting(new(MenuStyle: style));
            var button = window.FindControl<Button>("FileCommandsButton")!;
            Assert.Contains(style == MenuStyle.Quiet ? "quiet-menu" : "rounded-menu", button.Classes);
            window.UpdateLayout();
            window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.F10 });
            Assert.True(button.IsFocused);
            window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.LeftAlt, KeyModifiers = KeyModifiers.Alt });
            window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = Key.LeftAlt });
            Assert.True(button.IsFocused);
            window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.F, KeyModifiers = KeyModifiers.Alt });
            Assert.True(button.Flyout!.IsOpen);
            Assert.Empty(window.EditorTextForTesting);
            button.Flyout.Hide();
            window.Close();
            await Task.CompletedTask;
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public async Task RestoredFileAppearsAutomaticallyWithoutLosingDirtyBufferSelectionOrRecovery()
    {
        var root = Path.Combine(Path.GetTempPath(), "AxiomRestoreReview-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "nested"));
        try
        {
            var current = Path.Combine(root, "current.md");
            await File.WriteAllTextAsync(current, "saved content");
            var window = CreateWindow(root);
            await window.LoadFolderAsync(root);
            await window.OpenDocumentAsync(current);
            var nested = Assert.Single(window.WorkspaceTreeForTesting, node => node.Name == "nested");
            nested.IsExpanded = true;
            window.SetEditorTextForTesting("unsaved local changes");
            window.SetEditorSelectionForTesting(8, 5);
            await window.ProtectIfNeededForTestingAsync();
            var caret = window.EditorCaretOffsetForTesting;
            await File.WriteAllTextAsync(Path.Combine(root, "nested", "restored.md"), "restored bytes");
            for (var attempt = 0; attempt < 100 && !window.FolderItemsForTesting.Contains(Path.Combine("nested", "restored.md")); attempt++)
                await Task.Delay(50);
            Assert.Contains(Path.Combine("nested", "restored.md"), window.FolderItemsForTesting);
            Assert.Equal("unsaved local changes", window.EditorTextForTesting);
            Assert.Equal("local", window.EditorSelectedTextForTesting);
            Assert.Equal(caret, window.EditorCaretOffsetForTesting);
            Assert.True(Assert.Single(window.WorkspaceTreeForTesting, node => node.Name == "nested").IsExpanded);
            var state = new SqliteStateStore(new AppDataPaths(Path.Combine(root, "state")));
            Assert.Contains(await state.GetRecoverableDraftsAsync(), draft => draft.Text == "unsaved local changes");
            window.SetEditorTextForTesting("saved content");
            window.Close();
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public async Task BulkReviewStartsEmptyAndClickTogglesMultipleRowsWithoutModifiers()
    {
        var root = Path.Combine(Path.GetTempPath(), "AxiomBulkReview-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var first = Path.Combine(root, "a.md");
            var second = Path.Combine(root, "b.md");
            await File.WriteAllTextAsync(first, "alpha");
            await File.WriteAllTextAsync(second, "bravo");
            var state = new SqliteStateStore(new AppDataPaths(Path.Combine(root, "state")));
            await state.InitializeAsync();
            var workspace = await state.OpenWorkspaceAsync(root, "Review", DateTimeOffset.UtcNow);
            var window = new Axiom.App.BulkEnrollmentWindow(workspace, new BulkEnrollmentService(new WorkspaceFileOperations(state)));
            window.Show();
            var list = window.GetLogicalDescendants().OfType<ListBox>().Single(control => control.Name == "EnrollmentFiles");
            var selectAll = window.GetLogicalDescendants().OfType<Button>().Single(control => control.Name == "SelectAllEligible");
            for (var attempt = 0; attempt < 100 && !selectAll.IsEnabled; attempt++) await Task.Delay(50);
            Assert.True(selectAll.IsEnabled);
            Assert.Empty(list.SelectedItems!);
            Assert.Equal(2, list.ItemCount);
            window.UpdateLayout();
            ClickRow(0);
            ClickRow(1);
            Assert.Equal(2, list.SelectedItems!.Count);
            ClickRow(0);
            Assert.Single(list.SelectedItems!);
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
            window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
            Assert.Equal(2, list.SelectedItems!.Count);
            window.GetLogicalDescendants().OfType<Button>().Single(control => control.Name == "ClearEnrollmentSelection").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Empty(list.SelectedItems!);
            selectAll.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(2, list.SelectedItems!.Count);
            Assert.Equal("alpha", await File.ReadAllTextAsync(first));
            Assert.Equal("bravo", await File.ReadAllTextAsync(second));
            window.Close();

            void ClickRow(int index)
            {
                var container = list.ContainerFromIndex(index)!;
                var point = container.TranslatePoint(new Point(20, container.Bounds.Height / 2), window)!.Value;
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
            }
        }
        finally { Directory.Delete(root, true); }
    }
}
