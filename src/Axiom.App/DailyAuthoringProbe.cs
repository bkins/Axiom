using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Editing;
using Axiom.Core;
using Axiom.Infrastructure;

namespace Axiom.App;

internal static class DailyAuthoringProbe
{
    internal static string? Root { get; private set; }
    internal static void Configure(string root)
    {
        Root = Path.GetFullPath(root);
        if (Directory.Exists(Root)) throw new IOException("Choose a fresh daily authoring evidence directory.");
        Directory.CreateDirectory(Root);
        Environment.SetEnvironmentVariable("AXIOM_STATE_ROOT", Path.Combine(Root, "state"));
    }

    internal static async Task StartAsync(MainWindow window)
    {
        try
        {
            await window.StartupCompleted;
            var workspace = Path.Combine(Root!, "workspace"); Directory.CreateDirectory(workspace);
            foreach (var name in new[] { "a.md", "b.md", "c.md" }) await File.WriteAllTextAsync(Path.Combine(workspace, name), "abcd\nefgh\nijkl\n");
            await window.SwitchWorkspaceAsync(workspace);
            foreach (var name in new[] { "a.md", "b.md", "c.md" }) await window.OpenDocumentAsync(Path.Combine(workspace, name));
            var active = window.TabsForTesting.Last();
            var editor = window.FindControl<TextEditor>("Editor")!;
            var keys = new List<string>();
            window.AddHandler(Avalonia.Input.InputElement.KeyDownEvent, (_, args) => keys.Add(args.Key + "/" + args.KeyModifiers), Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
            window.Topmost = true; window.WindowState = WindowState.Normal;
            editor.TextArea.Focus(); window.Activate(); window.UpdateLayout(); await Task.Delay(150);
            await NativeProbeInput.ClickAsync(editor);
            editor.CaretOffset = 1;
            editor.TextArea.Focus(); await Task.Delay(100);
            NativeProbeInput.Key(0x28, shift: true, alt: true);
            await Task.Delay(100);
            Require(editor.TextArea.Selection is RectangleSelection, "Native Shift+Alt+Down alone did not create a rectangular selection. Keys: " + string.Join(", ", keys) + "; focus: " + window.FocusManager?.GetFocusedElement()?.GetType().Name);
            await RoundTripRectangleAsync(window, editor, active);
            NativeProbeInput.Type(window, "X"); await Task.Delay(150);
            Require(window.EditorTextForTesting == "aXbcd\neXfgh\nijkl\n", "Native vertical-only rectangular typing did not insert in both rows. Actual: " + window.EditorTextForTesting);
            window.UndoEditorForTesting(); Require(window.EditorTextForTesting == "abcd\nefgh\nijkl\n", "Vertical-only rectangular typing did not Undo as one action.");
            editor.Select(1, 0); editor.CaretOffset = 1;
            NativeProbeInput.Key(0x28, shift: true, alt: true);
            NativeProbeInput.Key(0x27, shift: true, alt: true);
            await Task.Delay(100);
            Require(editor.TextArea.Selection is RectangleSelection, "Native Shift+Alt did not create a rectangular selection.");
            await RoundTripRectangleAsync(window, editor, active);
            NativeProbeInput.Type(window, "X"); await Task.Delay(150);
            Require(window.EditorTextForTesting == "aXcd\neXgh\nijkl\n", "Native rectangular typing did not change both selected rows.");
            window.UndoEditorForTesting(); Require(window.EditorTextForTesting == "abcd\nefgh\nijkl\n", "Rectangular typing did not Undo as one action.");
            window.SetEditorTextForTesting("abcd\nefgh\nijkl\nDirty text\n");
            editor.Select(1, 2); editor.CaretOffset = 3;
            await window.ProtectIfNeededForTestingAsync();
            var dirty = editor.Text; var document = editor.Document;
            var tabs = window.FindControl<ListBox>("DocumentTabs")!;
            window.UpdateLayout(); await Task.Delay(100);
            var containers = tabs.GetVisualDescendants().OfType<ListBoxItem>().ToArray();
            var source = containers.Single(item => ReferenceEquals(item.DataContext, active));
            var target = containers.Single(item => ReferenceEquals(item.DataContext, window.TabsForTesting.First()));
            await NativeProbeInput.DragAsync(source.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == active.Label), target.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == window.TabsForTesting.First().Label));
            await Task.Delay(300);
            Require(ReferenceEquals(window.TabsForTesting.First(), active) && ReferenceEquals(editor.Document, document) && editor.Text == dirty && editor.SelectionStart == 1 && editor.SelectionLength == 2, "Native tab dragging changed the active buffer or failed to reorder.");
            var state = new SqliteStateStore(new AppDataPaths(Path.Combine(Root!, "state")));
            var record = (await state.GetActiveWorkspaceAsync())!;
            Require((await state.GetWorkspaceSessionAsync(record.WorkspaceId))!.Documents.First().RelativePath == "c.md", "Tab order was not persisted.");
            Require((await state.GetRecoverableDraftsAsync()).Any(draft => draft.Text == dirty), "Dragging lost protected recovery.");
            await window.ExecuteCommandAsync("document.new");
            var created = window.TabsForTesting.Last(); var identity = created.Session.DocumentId;
            Require(identity is not null && created.Session.IsDirty, "New did not assign a recoverable identity.");
            window.SetEditorTextForTesting(created.Session.Text + "# New content\n");
            await window.ProtectIfNeededForTestingAsync();
            var recovered = (await state.GetRecoverableDraftsAsync()).Single(draft => draft.DraftKey == created.Session.DraftKey);
            Require(DocumentSession.Recover(recovered).DocumentId == identity, "Recovery changed the new document identity.");
            var footer = window.FindControl<Border>("BottomStatusBar")!;
            window.UpdateLayout();
            Require(footer.Bounds.Height <= 68 && !window.FindControl<TextBlock>("DocumentFormatText")!.IsVisible && window.FindControl<TextBlock>("RecoveryText")!.IsVisible, "Footer was not concise or hid recovery.");
            Require(window.FindControl<TextBlock>("WorkspaceFilesHeader")!.Text == record.DisplayName, "Explorer header did not show the workspace name.");
            var terminal = RepositoryTerminal.CreateStartInfo(workspace);
            Require(terminal.WorkingDirectory == workspace && terminal.ArgumentList.SequenceEqual(new[] { "-NoProfile", "-NoExit" }), "Terminal launch did not preserve the reviewed directory or safe arguments.");
            foreach (var appearance in new[] { AppearancePreference.Light, AppearancePreference.Dark })
            {
                await window.ApplyAppearanceForTestingAsync(appearance); window.UpdateLayout(); await Task.Delay(150);
                using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize((int)window.ClientSize.Width, (int)window.ClientSize.Height), new Vector(96, 96));
                bitmap.Render(window); bitmap.Save(Path.Combine(Root!, appearance + "-avalonia-surface.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            }
            await File.WriteAllTextAsync(Path.Combine(Root!, "result.json"), JsonSerializer.Serialize(new
            {
                Version = window.BuildIdentityForTesting.InformationalVersion
              , NativeShiftAltTypingAndUndo = true
              , NativeVerticalOnlyTypingAndUndo = true
              , NativeRectangleTabRoundTripAndTyping = true
              , NativeTabDragPreservesBufferAndSelection = true
              , TabOrderAndRecoveryPersisted = true
              , NewIdentitySurvivesRecovery = true
              , ConciseFooterAndWorkspaceHeader = true
              , TerminalRootAndArgumentConfiguration = true
              , HumanChecks = "Human visual/DPI, sustained use, selection-loss reproduction and release acceptance remain pending. Earlier layout/Terminal human evidence is retained for its exact candidate."
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception exception) { await File.WriteAllTextAsync(Path.Combine(Root!, "error.txt"), exception.ToString()); }
        finally { window.UnsavedChoiceForTesting = _ => Task.FromResult("Discard"); window.Close(); }
    }

    private static async Task RoundTripRectangleAsync(MainWindow window, TextEditor editor, DocumentTab active)
    {
        var selection = editor.TextArea.Selection;
        var segments = selection.Segments.Select(segment => (segment.StartOffset, segment.Length)).ToArray();
        var caret = editor.TextArea.Caret.Position;
        var tabs = window.FindControl<ListBox>("DocumentTabs")!;
        var other = window.TabsForTesting.First(tab => tab != active && tab.Session.Path is not null);
        async Task ClickTabAsync(DocumentTab tab)
        {
            window.UpdateLayout();
            var container = tabs.GetVisualDescendants().OfType<ListBoxItem>().Single(item => ReferenceEquals(item.DataContext, tab));
            await NativeProbeInput.ClickAsync(container.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == tab.Label));
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!ReferenceEquals(editor.Document, tab.Document) && DateTime.UtcNow < deadline) await Task.Delay(25);
            Require(ReferenceEquals(editor.Document, tab.Document), "Native tab click did not activate its document.");
        }
        await ClickTabAsync(other);
        await ClickTabAsync(active);
        Require(editor.TextArea.Selection is RectangleSelection &&
            segments.SequenceEqual(editor.TextArea.Selection.Segments.Select(segment => (segment.StartOffset, segment.Length))) &&
            caret == editor.TextArea.Caret.Position, "Native tab round trip lost rectangle shape, segments or caret.");
        editor.TextArea.Focus();
        await Task.Delay(100);
    }

    private static void Require(bool condition, string message) { if (!condition) throw new IOException(message); }
}
