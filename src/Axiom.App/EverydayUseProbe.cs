using System.Text.Json;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Axiom.Core;
using Axiom.Infrastructure;

namespace Axiom.App;

internal static class EverydayUseProbe
{
    internal static string? Root { get; private set; }

    internal static void Configure(string root)
    {
        root = Path.GetFullPath(root);
        if (Directory.Exists(root)) throw new IOException("Choose a fresh native evidence directory.");
        Directory.CreateDirectory(root);
        Root = root;
        Environment.SetEnvironmentVariable("AXIOM_STATE_ROOT", Path.Combine(root, "state"));
    }

    internal static async Task StartAsync(MainWindow window)
    {
        var root = Root!;
        window.UnsavedChoiceForTesting = _ => Task.FromResult("Discard");
        try
        {
            await window.StartupCompleted;
            var workspace = Path.Combine(root, "workspace");
            Directory.CreateDirectory(workspace);
            var path = Path.Combine(workspace, "one.md");
            await File.WriteAllTextAsync(path, "# Saved source\n");
            if (!await window.SwitchWorkspaceAsync(workspace)) throw new IOException("Native workspace could not open.");
            await window.OpenDocumentAsync(path);
            var tab = window.TabsForTesting[0];
            tab.Document.Insert(tab.Document.TextLength, "\nUnsaved **Word** export.\n");
            window.SetEditorSelectionForTesting(2, 3);
            var caret = window.EditorCaretOffsetForTesting;
            window.ApplyEditorPreferencesForTesting(new("Consolas", 19, true, 6));
            if (window.EditorCaretOffsetForTesting != caret || !tab.Document.UndoStack.CanUndo) throw new IOException("Preferences changed native caret or undo.");
            await window.ShowPersistentHelpForTestingAsync("Export to Word and HTML");
            var help = window.PersistentHelpForTesting!;
            await WaitAsync(async () =>
            {
                try { return (await help.PreviewForTesting.InvokeScript("document.body.innerText.includes('Export to Word and HTML')"))?.Trim().Trim('"') == "true"; }
                catch { return false; }
            });
            var host = window.FindControl<Border>("HelpPanelHost")!;
            window.Width = 900;
            window.Height = 580;
            window.UpdateLayout();
            if (host.Bounds.Width < 280 || !window.IsEnabled) throw new IOException("Persistent Help blocked or failed layout.");
            if (window.FindControl<Grid>("EditorPane")!.Bounds.Width < 100) throw new IOException("Help left no usable editor width at minimum window size.");
            tab.Document.Insert(tab.Document.TextLength, "Editing with Help open.\n");
            if (window.EditorTextForTesting != tab.Document.Text || !tab.Session.IsDirty || !tab.Document.UndoStack.CanUndo)
                throw new IOException("Persistent Help or preferences changed the working buffer.");
            help.GetLogicalDescendants().OfType<TextBox>().Single(control => control.Name == "HelpSearch").Text = "Word";
            window.UpdateLayout();
            var results = help.GetLogicalDescendants().OfType<ListBox>().Single(control => control.Name == "HelpSearchResults");
            await WaitAsync(() => Task.FromResult(results.ItemCount > 0));
            if (results.ItemCount == 0) throw new IOException("Native Help body search returned no results.");
            results.SelectedIndex = results.ItemCount - 1;
            await WaitAsync(async () =>
            {
                try { return (await help.PreviewForTesting.InvokeScript("!!document.querySelector('mark')"))?.Trim().Trim('"') == "true"; }
                catch { return false; }
            });
            var export = new DocumentExportService();
            var snapshot = new ExportSnapshot(tab.Session.Text, path);
            var html = await export.WriteAsync(snapshot, Path.Combine(root, "export.html"), false, [path], Path.Combine(root, "state"));
            var word = await export.WriteAsync(snapshot, Path.Combine(root, "export.docx"), false, [path], Path.Combine(root, "state"));
            var preview = window.FindControl<NativeWebView>("Preview")!;
            preview.NavigateToString(await File.ReadAllTextAsync(html.Path));
            await WaitAsync(async () =>
            {
                try { return (await preview.InvokeScript("document.body.innerText.includes('Editing with Help open.') && !!document.querySelector('strong')"))?.Trim().Trim('"') == "true"; }
                catch { return false; }
            });
            await window.ProtectIfNeededForTestingAsync();
            var state = new SqliteStateStore(new AppDataPaths(Path.Combine(root, "state")));
            if (!(await state.GetRecoverableDraftsAsync()).Any(draft => draft.Text == tab.Session.Text)) throw new IOException("Draft recovery did not retain export source.");
            if (await File.ReadAllTextAsync(path) != "# Saved source\n") throw new IOException("Native export changed Markdown.");
            host.IsVisible = false;
            await window.ShowPersistentHelpForTestingAsync(null);
            if (!host.IsVisible || window.PersistentHelpForTesting != help) throw new IOException("Help reopen did not retain its panel.");
            await File.WriteAllTextAsync(Path.Combine(root, "result.json"), JsonSerializer.Serialize(new
            {
                Version = window.BuildIdentityForTesting.InformationalVersion
              ,
                NativeHelpLoaded = true
              ,
                HelpSearchResults = results.ItemCount
              ,
                HelpDoesNotBlockEditing = window.IsEnabled
              ,
                MinimumWidth = window.Width
              ,
                HelpWidth = host.Bounds.Width
              ,
                PreferencesPreservedCaretBeforeEdit = caret
              ,
                UnsavedHtmlRendered = true
              ,
                WordPackageCreated = File.Exists(word.Path)
              ,
                SourceUnchanged = true
              ,
                RecoveryProtected = true
              ,
                RecentCount = (await state.GetRecentDocumentsAsync()).Count
              ,
                HumanChecks = "Word layout, physical input/focus, themes/DPI, icon/menu design and exact-candidate acceptance remain pending."
            }, new JsonSerializerOptions { WriteIndented = true }));
            window.SetEditorTextForTesting("# Saved source\n");
            window.Close();
        }
        catch (Exception exception)
        {
            await File.WriteAllTextAsync(Path.Combine(root, "error.txt"), exception.ToString());
            window.SetEditorTextForTesting("# Saved source\n");
            window.Close();
        }
    }

    private static async Task WaitAsync(Func<Task<bool>> check)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await check()) return;
            await Task.Delay(100);
        }
        throw new IOException("Native Help/export renderer did not satisfy its content check.");
    }
}
