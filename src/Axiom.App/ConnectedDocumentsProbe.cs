using System.Diagnostics;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Axiom.Core;
using Axiom.Infrastructure;

namespace Axiom.App;

internal static class ConnectedDocumentsProbe
{
    internal static string? Root { get; private set; }
    private static bool _verify;

    internal static void Configure(string root, bool verify)
    {
        root = Path.GetFullPath(root);
        if (!verify && (Directory.Exists(Path.Combine(root, "state")) || Directory.Exists(Path.Combine(root, "workspace-1000"))))
            throw new IOException("Native connected-document probe requires fresh state and workspace directories.");
        if (verify && !File.Exists(Path.Combine(root, "expected-session.json"))) throw new IOException("Capture the clean session before verifying startup.");
        Directory.CreateDirectory(root);
        Root = root;
        _verify = verify;
        Environment.SetEnvironmentVariable("AXIOM_STATE_ROOT", Path.Combine(root, "state"));
    }

    internal static async Task StartAsync(MainWindow window)
    {
        var root = Root!;
        window.UnsavedChoiceForTesting = _ => Task.FromResult("Discard");
        try
        {
            await window.StartupCompleted;
            var state = new SqliteStateStore(new AppDataPaths(Path.Combine(root, "state")));
            if (_verify)
            {
                var expected = JsonSerializer.Deserialize<WorkspaceDocumentSession>(await File.ReadAllTextAsync(Path.Combine(root, "expected-session.json")))!;
                var paths = window.TabsForTesting.Select(tab => Path.GetRelativePath(window.ActiveWorkspaceRootForTesting!, tab.Session.Path!)).ToArray();
                if (!paths.SequenceEqual(expected.Documents.Select(document => document.RelativePath))
                    || Path.GetRelativePath(window.ActiveWorkspaceRootForTesting!, window.PathContextForTesting!) != expected.ActiveRelativePath
                    || window.EditorCaretOffsetForTesting != expected.Documents.Single(document => document.RelativePath == expected.ActiveRelativePath).CaretOffset
                    || window.TabsForTesting.Any(tab => tab.Session.IsDirty)) throw new IOException("Native startup did not restore the clean tab set and active caret.");
                var original = JsonSerializer.Deserialize<ConnectedDocumentsProbeResult>(await File.ReadAllTextAsync(Path.Combine(root, "capture-result.json")))!;
                if (!window.ReadingModeForTesting) throw new IOException("Native startup did not restore reading mode.");
                await File.WriteAllTextAsync(Path.Combine(root, "result.json"), JsonSerializer.Serialize(original with { CleanStartupRestored = true, ReadingModeRestored = true }));
                window.Close();
                return;
            }
            var workspace = await Task.Run(() => LinkIndexBenchmarkProbe.GenerateAsync(root, 1000));
            var source = Path.Combine(workspace, "native-source.md");
            var target = Path.Combine(workspace, "native-target.md");
            var sourceId = Guid.NewGuid();
            var sourceText = DocumentIdCodec.Enroll("# Source\n\n[Follow](native-target.md#destination)\n", sourceId);
            var targetText = string.Concat(Enumerable.Repeat("Paragraph before the heading.\n\n", 100)) + "# Destination\n\nTarget body\n"
                + string.Concat(Enumerable.Repeat("Paragraph after the heading.\n\n", 100));
            var metadataId = Guid.NewGuid();
            targetText = DocumentIdCodec.Enroll(targetText, metadataId);
            await File.WriteAllTextAsync(source, sourceText);
            await File.WriteAllTextAsync(target, targetText);
            var workspaceClock = Stopwatch.StartNew();
            if (!await window.SwitchWorkspaceAsync(workspace)) throw new IOException("Could not open the native workload workspace.");
            var open1000Ms = workspaceClock.Elapsed.TotalMilliseconds;
            await window.OpenDocumentAsync(source);
            var preview = window.FindControl<NativeWebView>("Preview")!;
            await WaitAsync(async () => (await preview.InvokeScript("document.querySelector('a[href^=\"axiom-link:\"]') !== null"))?.Trim().Trim('"') == "true");
            await preview.InvokeScript("document.querySelector('a[href^=\"axiom-link:\"]').click()");
            await WaitAsync(() => Task.FromResult(window.PathContextForTesting == target));
            await WaitAsync(async () => (await preview.InvokeScript("(() => { const heading = document.getElementById('destination'); if (!heading) return false; const top = heading.getBoundingClientRect().top; return top >= -2 && top < 100; })()"))?.Trim().Trim('"') == "true");
            await window.NavigateHistoryForTestingAsync(false);
            var samples = new List<double>();
            for (var round = 0; round < 5 && samples.Count < 50; round++)
            {
                var indexing = window.RebuildBacklinksForTestingAsync();
                while (!indexing.IsCompleted)
                {
                    var tab = window.TabsForTesting.Single(tab => tab.Session.Path == source);
                    var clock = Stopwatch.StartNew();
                    tab.Document.Insert(tab.Document.TextLength, "x");
                    if (tab.Session.Text != tab.Document.Text) throw new IOException("Native document edit was not applied to its buffer.");
                    samples.Add(clock.Elapsed.TotalMilliseconds);
                    await Task.Delay(10);
                }
                await indexing;
            }
            if (samples.Count < 20) throw new IOException("Indexing completed before enough native edit samples were measured.");
            var p95 = samples.OrderBy(value => value).ElementAt((int)Math.Ceiling(samples.Count * 0.95) - 1);
            if (p95 >= 50) throw new IOException($"Native edit-to-buffer p95 {p95:F2} ms exceeds the 50 ms target.");
            await window.ConfirmAllTabsForTestingAsync();
            await window.OpenDocumentAsync(target);
            await window.RefreshClassificationAsync();
            window.FindControl<TextBox>("MetadataTags")!.Text = "Native tag\nSecond";
            window.FindControl<TextBox>("MetadataType")!.Text = "Note";
            window.FindControl<TextBox>("MetadataAliases")!.Text = "Native alternate name";
            if (!await window.ApplyMetadataAsync()) throw new IOException("Native metadata panel did not apply.");
            var classificationService = new ClassificationService(new FileDocumentStore(), state, state);
            var evidence = await Task.Run(() => classificationService.DiscoverAsync(workspace));
            var exported = MetadataTransferService.Export(evidence);
            var transferPath = Path.Combine(root, "metadata.json");
            await MetadataTransferService.WriteAsync(transferPath, exported);
            var transferred = await MetadataTransferService.ReadAsync(transferPath);
            transferred = transferred with { Records = transferred.Records.Select(item => item with { Type = "Decision" }).ToArray() };
            var proposals = MetadataTransferService.Preview(transferred, evidence);
            var importWindow = new MetadataImportWindow(proposals, changes => Task.Run(() => classificationService.ApplyAsync(evidence, changes)));
            importWindow.Opened += async (_, _) =>
            {
                await Task.Delay(100);
                try
                {
                    importWindow.GetLogicalDescendants().OfType<CheckBox>().Single(box => box.Name == "Replace" + metadataId.ToString("N")).IsChecked = true;
                    importWindow.GetLogicalDescendants().OfType<Button>().Single(button => button.Name == "ApplyImport").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                catch (Exception exception)
                {
                    File.WriteAllText(Path.Combine(root, "error.txt"), exception.ToString());
                    importWindow.Close();
                }
            };
            await importWindow.ShowDialog(window);
            if ((await state.GetClassificationsAsync()).Single(value => value.DocumentId == metadataId).Type != "Decision")
                throw new IOException("Native reviewed metadata import did not commit.");
            evidence = await Task.Run(() => classificationService.DiscoverAsync(workspace));
            var relationEditor = new RelationshipEditorWindow(metadataId, evidence.Documents.Where(document => document.DocumentId == sourceId).ToArray(), null,
                change => Task.Run(() => classificationService.ApplyConnectedAsync(evidence, [], [change])));
            relationEditor.Opened += async (_, _) =>
            {
                await Task.Delay(100);
                try
                {
                    relationEditor.GetLogicalDescendants().OfType<TextBox>().Single(box => box.Name == "RelationshipNote").Text = "Native related note";
                    relationEditor.GetLogicalDescendants().OfType<Button>().Single(button => button.Name == "ApplyRelationship").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                catch (Exception exception) { File.WriteAllText(Path.Combine(root, "error.txt"), exception.ToString()); relationEditor.Close(); }
            };
            await relationEditor.ShowDialog(window);
            var relationship = (await state.GetRelationshipsAsync()).Single();
            await window.FollowRelationshipAsync(relationship);
            if (window.PathContextForTesting != source) throw new IOException("Native outgoing relationship did not navigate.");
            await window.FollowRelationshipAsync(relationship);
            if (window.PathContextForTesting != target) throw new IOException("Native incoming relationship did not navigate.");
            evidence = await Task.Run(() => classificationService.DiscoverAsync(workspace));
            transferred = MetadataTransferService.Export(evidence, await state.GetRelationshipsAsync());
            await MetadataTransferService.WriteAsync(Path.Combine(root, "connected-metadata-v2.json"), transferred);
            transferred = await MetadataTransferService.ReadAsync(Path.Combine(root, "connected-metadata-v2.json"));
            transferred = transferred with { Relationships = transferred.Relationships!.Select(item => item with { Note = "Imported relation note" }).ToArray() };
            proposals = MetadataTransferService.Preview(transferred, evidence, await state.GetRelationshipsAsync());
            var relationImport = new MetadataImportWindow(proposals, (changes, relations) => Task.Run(() => classificationService.ApplyConnectedAsync(evidence, changes, relations)));
            relationImport.Opened += async (_, _) =>
            {
                await Task.Delay(100);
                try
                {
                    relationImport.GetLogicalDescendants().OfType<CheckBox>().Single(box => box.Name == "Replace" + relationship.RelationshipId.ToString("N")).IsChecked = true;
                    relationImport.GetLogicalDescendants().OfType<Button>().Single(button => button.Name == "ApplyImport").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                catch (Exception exception) { File.WriteAllText(Path.Combine(root, "error.txt"), exception.ToString()); relationImport.Close(); }
            };
            await relationImport.ShowDialog(window);
            if (AssertRelationshipNote(await state.GetRelationshipsAsync()) != "Imported relation note") throw new IOException("Native relationship import did not commit.");
            var tabBeforeReading = window.TabsForTesting.Single(tab => tab.Session.Path == target);
            tabBeforeReading.Document.Insert(tabBeforeReading.Document.TextLength, " Native unsaved reading work");
            var workingText = tabBeforeReading.Session.Text;
            await window.ToggleReadingModeAsync();
            await window.OpenExplorerPathAsync(source);
            await window.OpenExplorerPathAsync(source);
            await window.OpenExplorerPathAsync(target);
            if (!window.ReadingModeForTesting || window.TabsForTesting.Count != 2 || tabBeforeReading.Session.Text != workingText || !tabBeforeReading.Document.UndoStack.CanUndo)
                throw new IOException("Reading/explorer workflow lost working buffers or undo.");
            await window.ToggleReadingModeAsync();
            var discoveryEdits = new List<double>();
            var discovering = window.RefreshClassificationAsync();
            while (!discovering.IsCompleted)
            {
                var editClock = Stopwatch.StartNew();
                tabBeforeReading.Document.Insert(tabBeforeReading.Document.TextLength, "x");
                if (tabBeforeReading.Document.Text != tabBeforeReading.Session.Text) throw new IOException("Discovery blocked a native edit.");
                discoveryEdits.Add(editClock.Elapsed.TotalMilliseconds);
                await Task.Delay(10);
            }
            await discovering;
            if (discoveryEdits.Count < 20) throw new IOException("Too few native discovery edit samples.");
            var discoveryP95 = discoveryEdits.Order().ElementAt((int)Math.Ceiling(discoveryEdits.Count * 0.95) - 1);
            if (discoveryP95 >= 50) throw new IOException("Native discovery edit p95 exceeded 50 ms.");
            var stressWorkspace = await Task.Run(() => LinkIndexBenchmarkProbe.GenerateAsync(root, 10000));
            var loading = window.SwitchWorkspaceAsync(stressWorkspace);
            await Task.Delay(100);
            if (!window.FindControl<Border>("WorkspaceLoadingPanel")!.IsVisible) throw new IOException("Native workspace loading indicator did not appear.");
            workspaceClock.Restart();
            window.FindControl<Button>("WorkspaceLoadingCancel")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (await loading || window.ActiveWorkspaceRootForTesting != workspace || tabBeforeReading.Session.Text != tabBeforeReading.Document.Text || !tabBeforeReading.Session.IsDirty)
                throw new IOException("Native loading cancellation did not retain the original dirty workspace.");
            var cancelMs = workspaceClock.Elapsed.TotalMilliseconds;
            if (cancelMs >= 1000) throw new IOException("Native loading cancellation exceeded one second.");
            workspaceClock.Restart();
            if (!await window.SwitchWorkspaceAsync(stressWorkspace)) throw new IOException("Native stress workspace did not open.");
            var open10000Ms = workspaceClock.Elapsed.TotalMilliseconds;
            if (!await window.SwitchWorkspaceAsync(workspace)) throw new IOException("Native original workspace did not restore after stress opening.");
            await window.OpenDocumentAsync(target);
            await window.ToggleReadingModeAsync();
            var help = new HelpWindow("metadata", state.Paths.Root, AppearancePreference.System);
            var helpLoaded = false;
            help.Opened += async (_, _) =>
            {
                try
                {
                    await WaitAsync(async () =>
                    {
                        try { return (await help.PreviewForTesting.InvokeScript("document.body.innerText.includes('Document metadata and classification')"))?.Trim().Trim('"') == "true"; }
                        catch { return false; }
                    });
                    helpLoaded = true;
                }
                catch (Exception exception) { File.WriteAllText(Path.Combine(root, "error.txt"), exception.ToString()); }
                finally { help.Close(); }
            };
            await help.ShowDialog(window);
            if (!helpLoaded) throw new IOException("Offline native Help did not load the maintained guide.");
            window.SetEditorSelectionForTesting(targetText.IndexOf("# Destination", StringComparison.Ordinal), 0);
            var record = (await state.GetActiveWorkspaceAsync())!;
            var index = (await state.GetLinkIndexAsync(record.WorkspaceId))!;
            var unchanged = await File.ReadAllTextAsync(source) == sourceText && await File.ReadAllTextAsync(target) == targetText;
            if (!unchanged) throw new IOException("Native workflow changed saved Markdown.");
            var report = new ConnectedDocumentsProbeResult(Environment.MachineName, true, true, index.Documents.Count, samples.Count, p95, unchanged, false, true, true, true, true, true, false, true,
                cancelMs, open1000Ms, open10000Ms, discoveryP95);
            await File.WriteAllTextAsync(Path.Combine(root, "capture-result.json"), JsonSerializer.Serialize(report));
            window.Closed += (_, _) =>
            {
                var remembered = state.GetWorkspaceSessionAsync(record.WorkspaceId).GetAwaiter().GetResult();
                File.WriteAllText(Path.Combine(root, "expected-session.json"), JsonSerializer.Serialize(remembered));
            };
            window.Close();
        }
        catch (Exception exception)
        {
            File.WriteAllText(Path.Combine(root, "error.txt"), exception.ToString());
            Environment.ExitCode = 1;
            window.Close();
        }
    }

    private static async Task WaitAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!await condition())
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException("The native preview/navigation condition was not reached.");
            await Task.Delay(50);
        }
    }

    private static string? AssertRelationshipNote(IReadOnlyList<DocumentRelationship> relationships) => relationships.Single().Note;
}
