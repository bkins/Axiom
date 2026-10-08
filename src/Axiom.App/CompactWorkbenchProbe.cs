using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using AvaloniaEdit;
using Axiom.Core;

namespace Axiom.App;

internal static class CompactWorkbenchProbe
{
    internal static string? Root { get; private set; }
    internal static void Configure(string root)
    {
        Root = Path.GetFullPath(root);
        if (Directory.Exists(Root)) throw new IOException("Choose a fresh compact workbench evidence directory.");
        Directory.CreateDirectory(Root);
        Environment.SetEnvironmentVariable("AXIOM_STATE_ROOT", Path.Combine(Root, "state"));
    }

    internal static async Task StartAsync(MainWindow window)
    {
        try
        {
            await window.StartupCompleted;
            window.Width = 1200;
            window.Height = 720;
            var workspace = Path.Combine(Root!, "workspace");
            Directory.CreateDirectory(workspace);
            var path = Path.Combine(workspace, "source.md");
            await File.WriteAllTextAsync(path, "# Saved\n\nContent\n");
            await window.SwitchWorkspaceAsync(workspace);
            await window.OpenDocumentAsync(path);
            window.SetEditorTextForTesting("# Unsaved protected content\n");
            await window.ProtectIfNeededForTestingAsync();
            var editor = window.FindControl<TextEditor>("Editor")!;
            window.ApplyEditorPreferencesForTesting(new EditorPreferences { CompactTitleBar = true });
            window.UpdateLayout();
            await Task.Delay(350);
            Require(window.IsExtendedIntoWindowDecorations, "Windows did not extend the client area into the title bar.");
            var menuButton = window.FindControl<Button>("FileCommandsButton")!;
            Require(menuButton.Content is WorkbenchIcon && window.FindControl<Button>("AppearanceButton")!.IsVisible == false, "Menu icon or Settings appearance placement is incorrect.");
            Require(menuButton.Flyout is MenuFlyout flyout && flyout.Items.OfType<MenuItem>().Select(item => item.Header?.ToString()).SequenceEqual(new[] { "File", "Edit", "Format", "Tools" }), "Hamburger menu lost command groups.");
            var title = window.FindControl<Border>("TitleDragRegion")!;
            var hwnd = window.TryGetPlatformHandle()!.Handle;
            var titleHit = HitTest(hwnd, title.PointToScreen(new Point(title.Bounds.Width / 2, title.Bounds.Height / 2)));
            var menuHit = HitTest(hwnd, menuButton.PointToScreen(new Point(menuButton.Bounds.Width / 2, menuButton.Bounds.Height / 2)));
            Require(titleHit == 2 && menuHit == 1, $"Native title/menu hit testing failed: title {titleHit}, menu {menuHit}.");
            var captionHits = new List<long>();
            GetWindowRect(hwnd, out var rectangle);
            var scale = window.RenderScaling;
            foreach (var offset in new[] { 23, 69, 115 })
                captionHits.Add(HitTest(hwnd, new PixelPoint(rectangle.Right - (int)(offset * scale), rectangle.Top + (int)(18 * scale))));
            Require(captionHits.SequenceEqual(new long[] { 20, 9, 8 }), "System caption roles were not available: " + string.Join(",", captionHits));
            window.WindowState = WindowState.Maximized;
            await Task.Delay(180);
            Require(window.WindowState == WindowState.Maximized && window.EditorTextForTesting == "# Unsaved protected content\n", "Maximize changed the draft.");
            window.WindowState = WindowState.Normal;
            await Task.Delay(180);
            window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.F10 });
            Require(menuButton.IsFocused, "F10 did not reach the main menu.");
            await window.ExecuteCommandAsync("workspace.filter");
            var filter = window.FindControl<TextBox>("ExplorerFilter")!;
            Require(filter.IsFocused, "Filename filter shortcut did not focus text input.");
            filter.Text = "source";
            await Task.Delay(220);
            filter.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            Require(filter.Text == "source" && !window.FindControl<Grid>("ExplorerFilterPanel")!.IsVisible && window.FindControl<TextBlock>("ExplorerFilterStatus")!.IsVisible, "Hiding the active filter lost its query or indicator.");
            Require(await window.RefreshClassificationAsync(), "Metadata refresh failed.");
            Require(window.FindControl<TextBlock>("MetadataRefreshStatus")!.Text!.Contains("saved files"), "Metadata result counts were absent.");
            Require(window.EditorTextForTesting == "# Unsaved protected content\n" && await File.ReadAllTextAsync(path) == "# Saved\n\nContent\n", "Workbench actions changed dirty or saved Markdown.");
            var firstTab = window.TabsForTesting.Single();
            editor.Select(2, 7);
            editor.CaretOffset = 9;
            var secondPath = Path.Combine(workspace, "second.md");
            await File.WriteAllTextAsync(secondPath, "# Second\n\nSaved second\n");
            await window.OpenDocumentAsync(secondPath);
            var secondTab = window.TabsForTesting.Last();
            editor.Select(3, 4);
            editor.CaretOffset = 7;
            var tabs = window.FindControl<ListBox>("DocumentTabs")!;
            window.Activate();
            window.UpdateLayout();
            await Task.Delay(150);
            var firstContainer = tabs.GetVisualDescendants().OfType<ListBoxItem>().Single(item => ReferenceEquals(item.DataContext, firstTab));
            await NativeProbeInput.ClickAsync(firstContainer.GetVisualDescendants().OfType<TextBlock>().First());
            await Task.Delay(200);
            Require(ReferenceEquals(editor.Document, firstTab.Document) && editor.CaretOffset == 9 && editor.SelectionStart == 2 && editor.SelectionLength == 7, "Native tab click lost the first caret or selection.");
            await window.ExecuteCommandAsync("navigation.nextTab");
            Require(ReferenceEquals(editor.Document, secondTab.Document) && editor.CaretOffset == 7 && editor.SelectionStart == 3 && editor.SelectionLength == 4, "Tab cycling lost the second caret or selection.");
            await window.SelectTabForTestingAsync(firstTab);
            Require(window.EditorTextForTesting == "# Unsaved protected content\n" && firstTab.Session.IsDirty, "Native tab navigation changed the draft.");
            Directory.CreateDirectory(Path.Combine(workspace, "empty-parent", "empty-leaf"));
            filter.Text = "";
            await window.ExecuteCommandAsync("workspace.refresh");
            var emptyParent = window.ExplorerNodesForTesting.Single(node => node.Name == "empty-parent");
            Require(emptyParent.Children.Single().Name == "empty-leaf", "Native workspace refresh lost an empty folder or its ancestor.");
            foreach (var preference in new[] { AppearancePreference.Light, AppearancePreference.Dark })
            {
                await window.ApplyAppearanceForTestingAsync(preference);
                window.UpdateLayout();
                await Task.Delay(150);
                using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.ClientSize.Width, (int)window.ClientSize.Height), new Vector(96, 96));
                bitmap.Render(window);
                bitmap.Save(Path.Combine(Root!, preference + "-avalonia-surface.png"), PngBitmapEncoderOptions.Default);
            }
            window.Width = 900;
            window.Height = 580;
            window.UpdateLayout();
            Require(window.FindControl<Border>("TopCommandBar")!.Bounds.Height <= 48, "Toolbar wrapped at the minimum window size.");
            window.ApplyEditorPreferencesForTesting(new EditorPreferences { CompactTitleBar = false });
            await Task.Delay(150);
            Require(!window.ExtendClientAreaToDecorationsHint, "Standard title-bar fallback did not apply.");
            await File.WriteAllTextAsync(Path.Combine(Root!, "result.json"), JsonSerializer.Serialize(new
            {
                Version = window.BuildIdentityForTesting.DisplayText
              , ExtendedWindowsTitleBar = true
              , NativeTitleDragHit = titleHit
              , NativeMenuClientHit = menuHit
              , NativeCaptionCloseMaximizeMinimizeHits = captionHits
              , MaximizeRestoreDirtyBufferPreserved = true
              , HamburgerGroupsAndF10 = true
              , FilterFocusEscapeQueryAndIndicator = true
              , MetadataRefreshCountsWithoutSave = true
              , MinimumWidthToolbarSingleRow = true
              , StandardTitleBarFallback = true
              , NativeTabClickAndCycleCaretSelectionRetained = true
              , EmptyFoldersSurviveRefresh = true
              , HumanChecks = "Physical dragging/system menu/caption buttons/snap, keyboard/screen reader, multiple DPI and visual acceptance remain pending. Surface images omit hosted NativeWebView content and are layout evidence only."
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception exception) { await File.WriteAllTextAsync(Path.Combine(Root!, "error.txt"), exception.ToString()); }
        finally { window.UnsavedChoiceForTesting = _ => Task.FromResult("Discard"); window.Close(); }
    }

    private static long HitTest(nint hwnd, PixelPoint point)
        => SendMessage(hwnd, 0x84, 0, (nint)((point.Y << 16) | (point.X & 0xffff))).ToInt64();

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern nint SendMessage(nint hwnd, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint hwnd, out WindowRectangle rectangle);

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRectangle { public int Left; public int Top; public int Right; public int Bottom; }

    private static void Require(bool condition, string message) { if (!condition) throw new IOException(message); }
}
