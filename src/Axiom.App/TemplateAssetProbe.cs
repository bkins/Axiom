using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Axiom.Core;
using Axiom.Infrastructure;

namespace Axiom.App;

internal static class TemplateAssetProbe
{
    internal static string? Root { get; private set; }

    internal static void Configure(string root)
    {
        root = Path.GetFullPath(root);
        if (Directory.Exists(root)) throw new IOException("Choose a fresh template/asset evidence directory.");
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
            var workspaceRoot = Path.Combine(root, "workspace");
            var assets = Path.Combine(workspaceRoot, "assets");
            Directory.CreateDirectory(assets);
            var originalPath = Path.Combine(workspaceRoot, "original.md");
            await File.WriteAllTextAsync(originalPath, "# Saved\n\ncontent");
            await window.SwitchWorkspaceAsync(workspaceRoot);
            await window.OpenDocumentAsync(originalPath);
            window.SetEditorSelectionForTesting(9, 7);
            if (!window.ApplyMarkdownEditForTesting(MarkdownEditKind.Bold)) throw new IOException("Dirty edit fixture failed.");
            var dirty = window.EditorTextForTesting;
            await window.ProtectIfNeededForTestingAsync();
            var state = new SqliteStateStore(new AppDataPaths(Path.Combine(root, "state")));
            var workspace = await state.GetActiveWorkspaceAsync() ?? throw new IOException("Workspace missing.");
            var template = $"<!-- axiom:documentId={Guid.NewGuid():D} -->\n# {{{{title}}}}\n{{{{date}}}}\n\nCafé 日本語";
            var templatePath = Path.Combine(root, "template.md");
            await File.WriteAllTextAsync(templatePath, template);
            var expanded = DocumentTemplateService.Expand(template, "Created", new DateOnly(2026, 10, 1));
            // Exercise the real preview window and its Create button, then the
            // same commit/open path used by the command after user review.
            var preview = new DocumentCreationPreviewWindow(Path.Combine(workspaceRoot, "created.md"), expanded);
            var reviewed = preview.ShowDialog<bool>(window);
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (preview.Content is not Grid grid || grid.Children.OfType<TextBox>().Single().Text != expanded)
                    throw new IOException("Template preview content mismatch.");
                var button = grid.Children.OfType<Avalonia.Controls.StackPanel>().Single().Children.OfType<Button>().First();
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            });
            if (!await reviewed) throw new IOException("Create preview was not confirmed.");
            await window.CreateReviewedDocumentAsync(workspace, "created.md", expanded);
            var createdId = DocumentIdCodec.Find(window.EditorTextForTesting);
            if (createdId is null || window.EditorTextForTesting != DocumentIdCodec.Enroll(expanded, createdId.Value) || await File.ReadAllTextAsync(templatePath) != template)
                throw new IOException("Template source or creation text changed.");
            using var transfer = new DataTransfer();
            foreach (var path in new[] { originalPath, Path.Combine(workspaceRoot, "created.md"), originalPath })
            {
                var file = await window.StorageProvider.TryGetFileFromPathAsync(path) ?? throw new IOException("Drop fixture file missing.");
                transfer.Add(DataTransferItem.CreateFile(file));
            }
            window.RaiseEvent(new DragEventArgs(DragDrop.DropEvent, transfer, window, new Avalonia.Point(100, 100), KeyModifiers.None));
            await window.LastFileDrop;
            if (window.TabsForTesting.Count != 2 || window.EditorTextForTesting != dirty || !window.EditorCanUndoForTesting || window.EditorSelectedTextForTesting != "content")
                throw new IOException("Drop routing did not preserve/reuse dirty tab.");
            var sourceImage = Path.Combine(root, "source image.png");
            await using (var image = AssetLoader.Open(new Uri("avares://Axiom.App/Assets/Axiom.png")))
            await using (var output = File.Create(sourceImage)) await image.CopyToAsync(output);
            var service = new WorkspaceImportService(new WorkspaceFileOperations(state));
            var bytes = await service.ReviewAsync(sourceImage, true);
            var copied = await service.CopyReviewedAsync(workspace, "assets/copied image.png", bytes, true) ?? throw new IOException("No copied path.");
            if (!window.InsertImageReferenceForTesting(copied)) throw new IOException("Imported image reference failed.");
            if (!window.EditorTextForTesting.Contains("assets/copied%20image.png", StringComparison.Ordinal))
                throw new IOException("Relative image path was not escaped.");
            window.UndoEditorForTesting();
            if (window.EditorTextForTesting != dirty || !File.Exists(copied)) throw new IOException("Insertion Undo changed the imported asset.");
            if (!(await File.ReadAllBytesAsync(copied)).SequenceEqual(bytes) || !(await File.ReadAllBytesAsync(sourceImage)).SequenceEqual(bytes))
                throw new IOException("Import changed image bytes.");
            if (await File.ReadAllTextAsync(originalPath) != "# Saved\n\ncontent") throw new IOException("Import changed source document.");
            if (!(await state.GetRecoverableDraftsAsync()).Any(draft => draft.Text == dirty)) throw new IOException("Dirty draft missing.");
            await File.WriteAllTextAsync(Path.Combine(root, "result.json"), JsonSerializer.Serialize(new
            {
                Version = window.BuildIdentityForTesting.InformationalVersion
              , NativePreviewAndCreateButton = true
              , ReviewedCreationAndTemplateSourcePreserved = true
              , DropRoutingDirtyTabSelectionUndoPreserved = true
              , EscapedRelativeImageInsertionAndUndo = true
              , OriginalAndCopiedImageBytesPreserved = true
              , RecoveryPreserved = true
              , Exclusions = "OS picker interaction and physical desktop drag gestures were not automated; human acceptance pending."
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception exception) { await File.WriteAllTextAsync(Path.Combine(root, "error.txt"), exception.ToString()); }
        finally
        {
            foreach (var tab in window.TabsForTesting.ToArray())
            {
                await window.SelectTabForTestingAsync(tab);
                window.SetEditorTextForTesting(tab.Session.SavedText);
            }
            window.Close();
        }
    }
}
