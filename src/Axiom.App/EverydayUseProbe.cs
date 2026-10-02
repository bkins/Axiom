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
            foreach (var style in Enum.GetValues<MenuStyle>())
            {
                window.ApplyEditorPreferencesForTesting(new("Consolas", 19, true, 6, style));
                window.UpdateLayout();
                foreach (var name in new[] { "FileCommandsButton", "EditCommandsButton", "FormatCommandsButton" })
                    if (!window.FindControl<Button>(name)!.Classes.Contains(style == MenuStyle.Quiet ? "quiet-menu" : "rounded-menu"))
                        throw new IOException("Native menu style did not apply.");
            }
            if (window.Icon is null) throw new IOException("Application icon did not load.");
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
            var epub = await export.WriteAsync(snapshot, Path.Combine(root, "export.epub"), false, [path], Path.Combine(root, "state"));
            var openDocument = await export.WriteAsync(snapshot, Path.Combine(root, "export.odt"), false, [path], Path.Combine(root, "state"));
            foreach (var packagePath in new[] { epub.Path, openDocument.Path })
            {
                using var package = System.IO.Compression.ZipFile.OpenRead(packagePath);
                var content = package.GetEntry(packagePath.EndsWith(".epub", StringComparison.Ordinal) ? "EPUB/content.xhtml" : "content.xml")!;
                using var contentStream = content.Open();
                var document = System.Xml.Linq.XDocument.Load(contentStream);
                if (!document.ToString().Contains("Editing", StringComparison.Ordinal) || document.ToString().Contains("documentId", StringComparison.Ordinal))
                    throw new IOException("Additional export lost unsaved text or exposed identity.");
            }
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
            var analysisSnapshot = AnalysisSnapshot.Capture("native-unsaved-fixture", tab.Session.Text);
            CpDocumentAnalysisClient? nativeClient = null;
            var cpReview = new CpAnalysisWindow(analysisSnapshot, () => analysisSnapshot.Matches(tab.Session.Text), () => nativeClient, candidate => { nativeClient?.Dispose(); nativeClient = candidate; });
            cpReview.Show(window);
            cpReview.UpdateLayout();
            var cpReviewText = cpReview.GetLogicalDescendants().OfType<TextBox>().Single(control => control.Name == "CpSnapshotReview");
            var cpCredential = cpReview.GetLogicalDescendants().OfType<TextBox>().Single(control => control.Name == "CpCredential");
            if (!cpReviewText.IsReadOnly || cpReviewText.Text != tab.Session.Text || cpCredential.PasswordChar == '\0')
                throw new IOException("Native CP review did not preserve the reviewed source or mask the credential.");
            var nativeNetworkVerified = false;
            if (Environment.GetEnvironmentVariable("AXIOM_CP_PROBE_ENDPOINT") is { Length: > 0 } cpEndpoint
             && Environment.GetEnvironmentVariable("AXIOM_CP_PROBE_CREDENTIAL") is { Length: > 0 } cpKey)
            {
                cpReview.GetLogicalDescendants().OfType<TextBox>().Single(control => control.Name == "CpEndpoint").Text = cpEndpoint;
                cpCredential.Text = cpKey;
                cpReview.GetLogicalDescendants().OfType<Button>().Single(control => control.Name == "CpConnect")
                        .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                await WaitAsync(() => Task.FromResult(nativeClient is not null));
                var submit = cpReview.GetLogicalDescendants().OfType<Button>().Single(control => control.Name == "CpSubmit");
                submit.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                var derived = cpReview.GetLogicalDescendants().OfType<TextBox>().Single(control => control.Name == "CpDerivedOutput");
                await WaitAsync(() => Task.FromResult(!string.IsNullOrWhiteSpace(derived.Text)), 130);
                nativeNetworkVerified = cpCredential.Text == "" && !string.IsNullOrWhiteSpace(derived.Text);
                if (!nativeNetworkVerified) throw new IOException("Actual CP native submission did not clear entry or produce derived output.");
            }
            cpReview.Close();
            nativeClient?.Dispose();
            using var offlineClient = new CpDocumentAnalysisClient(new Uri(Environment.GetEnvironmentVariable("AXIOM_CP_PROBE_ENDPOINT") ?? "http://localhost:5273"),
                Environment.GetEnvironmentVariable("AXIOM_CP_PROBE_CREDENTIAL") ?? "native-fixture-only-credential-no-submission");
            var semanticReview = new CpWorkspaceAnalysisWindow(offlineClient, new[] { analysisSnapshot }, _ => true);
            semanticReview.Show(window);
            semanticReview.UpdateLayout();
            var consent = semanticReview.GetLogicalDescendants().OfType<CheckBox>().Single(control => control.Tag is AnalysisSnapshot);
            if (consent.IsChecked == true) throw new IOException("Semantic source consent was selected by default.");
            consent.IsChecked = true;
            var selectedReview = semanticReview.GetLogicalDescendants().OfType<TextBox>().Single(control => control.Name == "CpWorkspaceSnapshotReview");
            if (!selectedReview.IsReadOnly || selectedReview.Text?.Contains(tab.Session.Text, StringComparison.Ordinal) != true)
                throw new IOException("Native semantic review did not display explicitly selected source.");
            var nativeSemanticNetworkVerified = false;
            if (nativeNetworkVerified)
            {
                semanticReview.GetLogicalDescendants().OfType<Button>().Single(control => control.Name == "CpSemanticBuild")
                              .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                var semanticSearch = semanticReview.GetLogicalDescendants().OfType<Button>().Single(control => control.Name == "CpSemanticSearch");
                await WaitAsync(() => Task.FromResult(semanticSearch.IsEnabled), 130);
                semanticReview.GetLogicalDescendants().OfType<TextBox>().Single(control => control.Name == "CpSemanticQuery").Text = "Word export";
                semanticSearch.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                var semanticMatches = semanticReview.GetLogicalDescendants().OfType<ListBox>().Single(control => control.Name == "CpSemanticResults");
                await WaitAsync(() => Task.FromResult(semanticMatches.ItemCount > 0), 130);
                nativeSemanticNetworkVerified = semanticMatches.ItemsSource?.Cast<SemanticMatch>().All(match => match.Section.DocumentHash == analysisSnapshot.SourceHash) == true;
                if (!nativeSemanticNetworkVerified) throw new IOException("Actual native semantic result hashes did not match reviewed source.");
            }
            semanticReview.Close();
            if (tab.Session.Text != analysisSnapshot.Text || await File.ReadAllTextAsync(path) != "# Saved source\n")
                throw new IOException("CP review changed the working source.");
            await File.WriteAllTextAsync(Path.Combine(root, "result.json"), JsonSerializer.Serialize(new
            {
                Version = window.BuildIdentityForTesting.InformationalVersion
              ,
                BothMenuStylesApplied = true
              ,
                ApplicationIconLoaded = true
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
                EpubPackageCreated = File.Exists(epub.Path)
              ,
                OpenDocumentPackageCreated = File.Exists(openDocument.Path)
              ,
                SourceUnchanged = true
              ,
                RecoveryProtected = true
              , NativeCpReviewDisplayed = true
              , NativeSemanticConsentDisplayed = true
              , CpReviewSourcePreserved = true
              , CpNativeNetworkSubmissionVerified = nativeNetworkVerified
              , NativeSemanticNetworkSubmissionVerified = nativeSemanticNetworkVerified
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

    private static async Task WaitAsync(Func<Task<bool>> check, int timeoutSeconds = 20)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await check()) return;
            await Task.Delay(100);
        }
        throw new IOException("Native Help/export renderer did not satisfy its content check.");
    }
}
