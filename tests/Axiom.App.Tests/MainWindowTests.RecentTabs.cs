using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Xunit;

namespace Axiom.App.Tests;

public sealed partial class MainWindowTests
{
    private static void PressTab(MainWindow window, bool reverse = false)
    {
        var args = new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent, Key = Key.Tab,
            KeyModifiers = KeyModifiers.Control | (reverse ? KeyModifiers.Shift : KeyModifiers.None)
        };
        window.FindControl<AvaloniaEdit.TextEditor>("Editor")!.RaiseEvent(args);
        Assert.True(args.Handled);
    }

    private static void ReleaseControl(MainWindow window) => window.RaiseEvent(new KeyEventArgs
    {
        RoutedEvent = InputElement.KeyUpEvent, Key = Key.LeftCtrl
    });

    [AvaloniaFact]
    public async Task QuickControlTabRepeatedlyTogglesMostRecentTabsAndRetainsDirtyUndo()
    {
        var root = CreateTabWorkspace();
        try
        {
            var window = CreateWindow(root);
            window.FindControl<Grid>("EditorPreviewLayout")!.Children.Remove(window.FindControl<NativeWebView>("Preview")!);
            window.Show();
            await window.StartupCompleted;
            await window.NewTabForTestingAsync();
            await window.NewTabForTestingAsync();
            var tabs = window.TabsForTesting.ToArray();
            await window.SelectTabForTestingAsync(tabs[0]);
            window.FindControl<AvaloniaEdit.TextEditor>("Editor")!.Document.Insert(0, "unsaved first");
            await window.SelectTabForTestingAsync(tabs[2]);
            for (var index = 0; index < 6; index++)
            {
                PressTab(window);
                Assert.Same(index % 2 == 0 ? tabs[0] : tabs[2], window.FindControl<ListBox>("DocumentTabs")!.SelectedItem);
                ReleaseControl(window);
            }
            Assert.Equal("unsaved first", tabs[0].Session.Text);
            Assert.True(tabs[0].Document.UndoStack.CanUndo);
            window.UnsavedChoiceForTesting = _ => Task.FromResult("Discard");
            await window.ConfirmAllTabsForTestingAsync();
            window.Close();
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public async Task HeldControlCyclesFrozenRecentOrderWrapsAndReversesThenCommitsOnRelease()
    {
        var root = CreateTabWorkspace();
        try
        {
            var window = CreateWindow(root);
            window.FindControl<Grid>("EditorPreviewLayout")!.Children.Remove(window.FindControl<NativeWebView>("Preview")!);
            window.Show();
            await window.StartupCompleted;
            await window.NewTabForTestingAsync();
            await window.NewTabForTestingAsync();
            var tabs = window.TabsForTesting.ToArray();
            await window.SelectTabForTestingAsync(tabs[0]); // recent order: 0, 2, 1
            foreach (var expected in new[] { tabs[2], tabs[1], tabs[0], tabs[2] })
            {
                PressTab(window);
                Assert.Same(expected, window.FindControl<ListBox>("DocumentTabs")!.SelectedItem);
            }
            PressTab(window, reverse: true);
            Assert.Same(tabs[0], window.FindControl<ListBox>("DocumentTabs")!.SelectedItem);
            PressTab(window, reverse: true);
            Assert.Same(tabs[1], window.FindControl<ListBox>("DocumentTabs")!.SelectedItem);
            ReleaseControl(window);
            PressTab(window);
            Assert.Same(tabs[0], window.FindControl<ListBox>("DocumentTabs")!.SelectedItem);
            ReleaseControl(window);
            await window.CloseTabForTestingAsync(tabs[0]);
            PressTab(window);
            Assert.Same(tabs[1], window.FindControl<ListBox>("DocumentTabs")!.SelectedItem);
            ReleaseControl(window);
            window.Close();
        }
        finally { Directory.Delete(root, true); }
    }
}
