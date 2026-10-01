using System.Text;
using System.Text.Json;
using Avalonia.Platform;
using Axiom.Core;
using Axiom.Infrastructure;

namespace Axiom.App;

internal static class PdfExportProbe
{
    internal static string? Root { get; private set; }

    internal static void Configure(string root)
    {
        root = Path.GetFullPath(root);
        if (Directory.Exists(root)) throw new IOException("Choose a fresh PDF evidence directory.");
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
            var path = Path.Combine(workspace, "source.md");
            await File.WriteAllTextAsync(path, "# Saved source\n");
            await using (var image = AssetLoader.Open(new Uri("avares://Axiom.App/Assets/Axiom.png")))
            await using (var target = File.Create(Path.Combine(workspace, "image.png")))
                await image.CopyToAsync(target);
            await window.SwitchWorkspaceAsync(workspace);
            await window.OpenDocumentAsync(path);
            var fixture = new StringBuilder("# PDF export acceptance fixture\n\nCafé — 日本語 一 — Ελληνικά — Привет — 😀\n\nUnsaved **bold**, *italic* and `code`. [Safe link](https://example.com).\n\n- [x] Done\n- [ ] Todo\n  - Nested\n\n3. Three\n4. Four\n\n> Quotation paragraph.\n\n![Local sample](image.png) ![Missing image](missing.png)\n\n```text\n" + new string('X', 250) + "\nEnd of long code\n```\n\n");
            fixture.AppendLine("## Repeating table\n\n| Header A | Header B |\n|---|---|");
            for (var index = 1; index <= 100; index++) fixture.AppendLine($"| Table row {index} | Café 日本語 — content wraps safely {new string('W', 60)} |");
            fixture.AppendLine("\n## Oversized table row\n\n| Tall header | Other header |\n|---|---|\n| " + string.Join(" ", Enumerable.Range(1, 900).Select(index => "TallToken" + index)) + " | Short cell |\n");
            for (var index = 1; index <= 45; index++) fixture.AppendLine($"\n## Section {index}\n\nParagraph {index}: searchable unsaved content with Unicode 一 and a long URL https://example.com/{new string('a', 300)}\n");
            fixture.AppendLine("\nFinal fixture marker\n");
            var markdown = DocumentIdCodec.Enroll(fixture.ToString(), Guid.NewGuid());
            window.SetEditorTextForTesting(markdown);
            window.SetEditorSelectionForTesting(2, 3);
            var caret = window.EditorCaretOffsetForTesting;
            var selection = window.EditorSelectedTextForTesting;
            var undo = window.TabsForTesting[0].Document.UndoStack.CanUndo;
            await window.ProtectIfNeededForTestingAsync();
            var state = new SqliteStateStore(new AppDataPaths(Path.Combine(root, "state")));
            var draftsBefore = await state.GetRecoverableDraftsAsync();
            var captured = new ExportSnapshot(window.EditorTextForTesting, path);
            var service = new DocumentExportService(new NativePdfExportRenderer(Path.Combine(root, "state")));
            var results = new List<ExportResult>();
            foreach (var size in Enum.GetValues<PdfPageSize>())
            {
                var result = await service.WriteAsync(captured, Path.Combine(root, size + ".pdf"), false, [path], Path.Combine(root, "state"), pdfPageSize: size);
                results.Add(result);
            }
            if (await File.ReadAllTextAsync(path) != "# Saved source\n" || window.EditorTextForTesting != markdown || !window.TabsForTesting[0].Session.IsDirty)
                throw new IOException("PDF export modified source or active-buffer state.");
            if (window.EditorCaretOffsetForTesting != caret || window.EditorSelectedTextForTesting != selection || window.TabsForTesting[0].Document.UndoStack.CanUndo != undo)
                throw new IOException("PDF export modified caret, selection or undo.");
            var draftsAfter = await state.GetRecoverableDraftsAsync();
            if (draftsBefore.Count != draftsAfter.Count || draftsBefore.Any(before => !draftsAfter.Any(after => after.DraftKey == before.DraftKey && after.Text == before.Text)))
                throw new IOException("PDF export modified protected recovery text.");
            await File.WriteAllTextAsync(Path.Combine(root, "fixture.md"), markdown);
            await File.WriteAllTextAsync(Path.Combine(root, "result.json"), JsonSerializer.Serialize(new
            {
                Version = window.BuildIdentityForTesting.InformationalVersion
              , Results = results
              , SourceUnchanged = true
              , DirtyBufferPreserved = true
              , CaretSelectionUndoPreserved = true
              , RecoveryTextPreserved = true
              , HumanAcceptance = "Pending until all planned features are implemented."
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception exception) { await File.WriteAllTextAsync(Path.Combine(root, "error.txt"), exception.ToString()); }
        finally { window.SetEditorTextForTesting("# Saved source\n"); window.Close(); }
    }
}
