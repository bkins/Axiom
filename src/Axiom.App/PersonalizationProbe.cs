using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Axiom.Core;

namespace Axiom.App;

internal static class PersonalizationProbe
{
    internal static string? Root { get; private set; }
    internal static void Configure(string root)
    {
        root = Path.GetFullPath(root);
        if (Directory.Exists(root)) throw new IOException("Choose a fresh personalization evidence directory.");
        Directory.CreateDirectory(root); Root = root;
        Environment.SetEnvironmentVariable("AXIOM_STATE_ROOT", Path.Combine(root, "state"));
    }
    internal static async Task StartAsync(MainWindow window)
    {
        try
        {
            await window.StartupCompleted;
            var editor = window.FindControl<AvaloniaEdit.TextEditor>("Editor")!;
            var preview = window.FindControl<Avalonia.Controls.NativeWebView>("Preview")!;
            var workspace = Path.Combine(Root!, "workspace"); Directory.CreateDirectory(workspace);
            var path = Path.Combine(workspace, "source.md");
            var saved = "# Viewing modes\n\n" + string.Join("\n\n", Enumerable.Range(1,250).Select(index => "Paragraph " + index + " keeps source and reading positions visible."));
            await File.WriteAllTextAsync(path, saved);
            await window.SwitchWorkspaceAsync(workspace); await window.OpenDocumentAsync(path);
            window.UnsavedChoiceForTesting = _ => Task.FromResult("Discard");
            var customize = window.ExecuteCommandAsync("application.toolbar");
            var dialog = await DialogAsync();
            var saveIndex = dialog.Items.ToList().FindIndex(item => item.Command.Id == "document.save");
            dialog.Choices.SelectedIndex = saveIndex; Click(dialog, "Move up");
            var image = dialog.Items.Single(item => item.Command.Id == "editor.insertImage");
            dialog.Choices.ScrollIntoView(image); await Task.Delay(100);
            var check = dialog.Choices.GetLogicalDescendants().OfType<CheckBox>().Single(item => Equals(item.Content, image.Command.Title));
            check.IsChecked = false; Click(dialog, "Apply"); await customize;
            var configured = window.EditorPreferencesForTesting;
            Require(!configured.ToolbarCommands.Contains("editor.insertImage") && window.VisibleToolbarIdsForTesting.ToList().IndexOf("document.save") < window.VisibleToolbarIdsForTesting.ToList().IndexOf("document.new"), "Toolbar checkbox/reorder did not apply.");
            Require(window.VisibleToolbarIdsForTesting[0] == "menu", "Mandatory menu was hidden or moved.");
            var cancel = window.ExecuteCommandAsync("application.toolbar"); dialog = await DialogAsync(); Click(dialog,"Reset"); Click(dialog,"Cancel"); await cancel;
            Require(window.EditorPreferencesForTesting == configured, "Cancelled Reset changed persisted toolbar.");
            var state = new Axiom.Infrastructure.SqliteStateStore(new Axiom.Infrastructure.AppDataPaths(Path.Combine(Root!, "state")));
            await state.InitializeAsync();
            Require((await state.GetEditorPreferencesAsync()).ToolbarCommands == configured.ToolbarCommands, "Toolbar did not persist.");
            window.ApplyEditorPreferencesForTesting(configured with { ToolbarCommands = "" });
            Require(window.VisibleToolbarIdsForTesting.SequenceEqual(new[] { "menu" }), "All-hidden toolbar lost mandatory menu.");
            await window.ExecuteCommandAsync("view.reading"); Require(window.ReadingModeForTesting, "Hidden toolbar command became unavailable.");
            await window.ExecuteCommandAsync("view.reading");
            window.ApplyEditorPreferencesForTesting(configured);
            editor.Document.Insert(editor.Text.Length, "\n\nUnsaved native edit");
            var dirty = editor.Text; var undo = editor.CanUndo; editor.CaretOffset = editor.Document.GetLineByNumber(200).Offset;
            editor.TextArea.Caret.BringCaretToView(); await Task.Delay(300);
            window.ToggleToolWindowForTesting(0);
            var tool = window.FindControl<Border>("ToolWindowHost")!; var wasToolVisible = tool.IsVisible;
            var originalState = window.WindowState;
            await window.ExecuteCommandAsync("view.mode.fullscreen"); await Task.Delay(200);
            Require(window.WindowState == WindowState.FullScreen, "Fullscreen did not change native window state.");
            editor.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            Require(window.ViewModeForTesting == WorkbenchViewMode.Standard && window.WindowState == originalState, "Escape did not restore fullscreen.");
            await window.ExecuteCommandAsync("view.mode.zen"); await Task.Delay(180); window.UpdateLayout();
            Require(window.FindControl<Grid>("EditorPane")!.IsVisible && !preview.IsVisible && !window.FindControl<Border>("TopCommandBar")!.IsVisible && !window.FindControl<Border>("WorkspaceSidebar")!.IsVisible, "Zen did not focus the editor.");
            Capture(window,"zen-avalonia-surface.png");
            editor.Document.Insert(editor.Text.Length, "\nZen edit"); dirty = editor.Text;
            editor.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape }); await Task.Delay(180);
            Require(tool.IsVisible == wasToolVisible && preview.IsVisible && editor.Text == dirty && editor.CanUndo && undo, "Zen lost layout, source or Undo.");
            await window.ProtectIfNeededForTestingAsync();
            var editorOffset = editor.TextArea.TextView.VerticalOffset;
            await window.ExecuteCommandAsync("view.mode.presentation"); await Task.Delay(650);
            Require(window.ReadingModeForTesting && preview.IsVisible && !window.FindControl<Grid>("EditorPane")!.IsVisible && window.FindControl<Border>("ViewModeExitBar")!.IsVisible, "Presentation did not focus Preview with exit controls.");
            await preview.InvokeScript("window.scrollTo(0,0)");
            Click(window,"Next page"); await Task.Delay(700);
            Require(await preview.InvokeScript("window.scrollY > 0") == "true", "Presentation next did not navigate document.");
            await preview.InvokeScript("window.dispatchEvent(new KeyboardEvent('keydown',{key:'Escape',bubbles:true}))");
            await WaitAsync(() => window.ViewModeForTesting == WorkbenchViewMode.Standard); await Task.Delay(200);
            Require(!window.ReadingModeForTesting && Math.Abs(editor.TextArea.TextView.VerticalOffset - editorOffset) < 60 && editor.Text == dirty, "Preview Escape failed to restore source position.");
            await window.ExecuteCommandAsync("view.reading");
            await window.ExecuteCommandAsync("view.mode.zen"); await window.ExecuteCommandAsync("view.mode.standard");
            Require(window.ReadingModeForTesting, "Focused view did not restore prior reading mode.");
            await window.ExecuteCommandAsync("view.reading");
            Require(window.FindControl<Grid>("EditorPreviewLayout")!.ColumnDefinitions[2].Width.Value > 0, "Reading mode restored a focused editor layout instead of the previous split.");
            await window.ExecuteCommandAsync("view.mode.compact");
            Require(window.EditorPreferencesForTesting.CompactWorkbench && (await state.GetEditorPreferencesAsync()).CompactWorkbench, "Compact density did not persist.");
            window.UpdateLayout(); Capture(window,"compact-avalonia-surface.png");
            var reset = window.ExecuteCommandAsync("application.toolbar"); dialog = await DialogAsync(); Click(dialog,"Reset"); Click(dialog,"Apply"); await reset;
            Require(window.EditorPreferencesForTesting.ToolbarCommands == ToolbarLayout.DefaultCommands && window.VisibleToolbarIdsForTesting.Contains("editor.insertImage"), "Reset did not restore default toolbar.");
            Require(editor.Text == dirty && await File.ReadAllTextAsync(path) == saved, "Personalization saved or discarded the dirty source.");
            editor.Undo(); Require(!editor.Text.Contains("Zen edit"), "Viewing mode broke editor Undo.");
            await File.WriteAllTextAsync(Path.Combine(Root!,"result.json"), JsonSerializer.Serialize(new
            {
                Version = window.BuildIdentityForTesting.DisplayText
              , ToolbarCheckboxReorderPersistenceCancelReset = true
              , MandatoryMenuAndHiddenCommandsAvailable = true
              , FullscreenNativeStateAndEditorEscape = true
              , ZenLayoutEditUndoRecoveryPreserved = true
              , PresentationNextAndNativeWebViewEscape = true
              , PriorLayoutReadingModeAndSourcePositionRestored = true
              , CompactDensityPersisted = true
              , SavedAndDirtySourcePreserved = true
              , HumanChecks = "Physical keyboard/clipboard, visual comfort, screen readers, DPI and final acceptance remain pending. Avalonia surface images omit hosted WebView."
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception exception) { await File.WriteAllTextAsync(Path.Combine(Root!,"error.txt"),exception.ToString()); }
        finally { window.UnsavedChoiceForTesting = _ => Task.FromResult("Discard"); window.Close(); }
    }
    private static void Capture(MainWindow window,string name)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.ClientSize.Width,(int)window.ClientSize.Height),new Vector(96,96));
        bitmap.Render(window); bitmap.Save(Path.Combine(Root!,name),PngBitmapEncoderOptions.Default);
    }
    private static void Click(Control host,string content) => host.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content,content)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static async Task<ToolbarCustomizationWindow> DialogAsync()
    {
        ToolbarCustomizationWindow? dialog = null;
        await WaitAsync(() => (dialog = ((IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!).Windows.OfType<ToolbarCustomizationWindow>().FirstOrDefault()) is not null);
        await Task.Delay(100); return dialog!;
    }
    private static async Task WaitAsync(Func<bool> condition) { for (var i=0;i<60;i++) { if(condition()) return; await Task.Delay(100); } throw new IOException("Native personalization condition timed out."); }
    private static void Require(bool value,string message) { if(!value) throw new IOException(message); }
}
