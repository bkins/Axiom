using System.IO.Compression;
using Axiom.Core;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Xunit;

namespace Axiom.Infrastructure.Tests;

public sealed class StrikethroughTests
{
    [Fact]
    public async Task PreviewAndStructuredExportsRetainNestedStrikeWhileTextStaysReadable()
    {
        using var fixture = new MutationTestWorkspace();
        var snapshot = new ExportSnapshot("~~**done**~~ and `~~code~~`", null);
        var structure = new MarkdownStructureService();
        var html = structure.Parse(snapshot.Markdown);
        var runs = new MarkdownExportService().Parse(snapshot).Blocks.Single().Runs;
        Assert.Contains(runs, run => run.Text == "done" && run.Strike && run.Bold && !run.Italic);
        Assert.Contains(runs, run => run.Text == "~~code~~" && run.Code && !run.Strike);
        foreach (var extension in new[] { "html", "docx", "odt", "epub", "txt" })
        {
            var path = Path.Combine(fixture.Root, "out." + extension);
            await new DocumentExportService().WriteAsync(snapshot, path, false, [], fixture.State.Paths.Root);
            if (extension == "html") Assert.Contains("<del>", await File.ReadAllTextAsync(path));
            if (extension == "txt") Assert.Contains("done", await File.ReadAllTextAsync(path));
            if (extension == "docx")
            { using var doc = WordprocessingDocument.Open(path, false); Assert.NotEmpty(doc.MainDocumentPart!.Document!.Descendants<Strike>()); }
            if (extension is "epub" or "odt")
            {
                using var archive = ZipFile.OpenRead(path);
                var entry = archive.GetEntry(extension == "odt" ? "content.xml" : "OEBPS/content.xhtml") ?? archive.Entries.First(item => item.FullName.EndsWith(".xhtml"));
                using var reader = new StreamReader(entry.Open());
                var content = await reader.ReadToEndAsync();
                Assert.Contains(extension == "odt" ? "text-line-through-style" : "del>", content);
            }
        }
    }

    [Fact]
    public void FormattingTogglesSelectionAndProtectsCodeAndCanonicalIdentity()
    {
        var edits = new MarkdownEditService();
        var edit = edits.Apply("done", 0, 4, MarkdownEditKind.Strikethrough);
        Assert.Equal("~~done~~", edit.Text);
        Assert.Equal("done", edits.Apply(edit.Text, edit.SelectionStart, edit.SelectionLength, MarkdownEditKind.Strikethrough).Text);
        Assert.False(edits.Apply("`code`", 1, 4, MarkdownEditKind.Strikethrough).Changed);
        var identity = DocumentIdCodec.Enroll("body", Guid.NewGuid());
        Assert.False(edits.Apply(identity, 0, 10, MarkdownEditKind.Strikethrough).Changed);
    }
}
