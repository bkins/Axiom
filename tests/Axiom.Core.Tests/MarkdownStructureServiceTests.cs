using Axiom.Core;

namespace Axiom.Core.Tests;

public sealed class MarkdownStructureServiceTests
{
    private readonly MarkdownStructureService _service = new();

    [Fact]
    public void ExtractsReferenceUsesAndDefinitionsWithExactSourceLocations()
    {
        const string markdown = "# Decisions\r\n\r\nBefore ☕ [one](<notes/one file.md#summary>) and [two][choice].\r\n\r\n[choice]: ../two.markdown#result \"Decision\"\r\n";
        var structure = _service.Parse(markdown);

        Assert.Equal(markdown, structure.SourceText);
        Assert.Equal(2, structure.Links.Count);
        Assert.Equal("notes/one file.md#summary", structure.Links[0].Destination);
        Assert.Equal("../two.markdown#result", structure.Links[1].Destination);
        Assert.Equal("choice", structure.Links[1].ReferenceLabel);
        Assert.Null(structure.Links[0].ReferenceLabel);
        Assert.Equal("[one](<notes/one file.md#summary>)", Slice(markdown, structure.Links[0].Source));
        Assert.Equal("[two][choice]", Slice(markdown, structure.Links[1].Source));
        Assert.Equal(3, structure.Links[0].Source.LineNumber);
        Assert.Equal(10, structure.Links[0].Source.ColumnNumber);
        Assert.All(structure.Links, link => Assert.Equal(structure.Headings[0].NodeId, link.HeadingNodeId));
        var definition = Assert.Single(structure.ReferenceDefinitions);
        Assert.Equal("choice", definition.Label);
        Assert.Equal(5, definition.Source.LineNumber);
        Assert.StartsWith("[choice]:", Slice(markdown, definition.Source));
        Assert.Equal(structure.Links[0], structure.FindLinkAt(structure.Links[0].Source.Offset + 4));
        Assert.Null(structure.FindLinkAt(structure.Links[1].Source.EndOffset));
    }

    [Fact]
    public void ExcludesCodeImagesAndUnresolvedReferenceSyntaxWithoutInventingLinks()
    {
        const string markdown = """
            # Real
            [ordinary](a.md)
            ![image](picture.md)
            `[inline](code.md)`
            [undefined][missing]

            ```markdown
            # Example
            [fenced](example.md)
            ```

                [indented](code.md)

            [picture]: image.md
            ![reference image][picture]
            """;
        var structure = _service.Parse(markdown);

        Assert.Equal("Real", Assert.Single(structure.Headings).Text);
        Assert.Equal("a.md", Assert.Single(structure.Links).Destination);
        Assert.Equal("picture", Assert.Single(structure.ReferenceDefinitions).Label);
    }

    [Fact]
    public void SupportsFullCollapsedAndShortcutReferencesAndLinksInTables()
    {
        const string markdown = """
            [Full][ref] [ref][] [ref]

            | See |
            | --- |
            | [table](table.md) |

            [ref]: <file with spaces.md>
            """;
        var structure = _service.Parse(markdown);

        Assert.Equal(4, structure.Links.Count);
        Assert.All(structure.Links.Take(3), link => Assert.Equal("file with spaces.md", link.Destination));
        Assert.Equal("table.md", structure.Links[3].Destination);
        Assert.All(structure.Links, link => Assert.Null(link.HeadingNodeId));
    }

    [Fact]
    public void IdentityMaskPreservesUnicodeUtf16AndCrLfOffsetsAndFencedExamples()
    {
        var id = Guid.NewGuid();
        var markdown = $"<!-- axiom:documentId={id:D} -->\r\n\r\n# Café 😀\r\n😀 [link](next.md)\r\n\r\n```\r\n<!-- axiom:documentId={id:D} -->\r\n```";
        var structure = _service.Parse(markdown);
        var heading = Assert.Single(structure.Headings);
        var link = Assert.Single(structure.Links);
        var html = new MarkdownPreviewService().RenderDocument(structure, null);

        Assert.Equal(markdown.IndexOf("# Café", StringComparison.Ordinal), heading.Source.Offset);
        Assert.Equal(3, heading.Source.LineNumber);
        Assert.Equal(4, link.Source.LineNumber);
        Assert.Equal(4, link.Source.ColumnNumber);
        Assert.Equal("[link](next.md)", Slice(markdown, link.Source));
        Assert.Contains($"id=\"{heading.Fragment}\"", html);
        Assert.Equal(1, html.Split(id.ToString("D")).Length - 1); // only the fenced example
        Assert.Equal(id, DocumentIdCodec.Find(markdown));
    }

    [Fact]
    public void PreviewOutlineAndFragmentsAgreeForFormattedSetextAndDuplicateHeadings()
    {
        const string markdown = "# First *formatted* `heading`!\n\nCafé\n----\n\n## Same\n## Same\n## Same-1\n## Same\n";
        var structure = _service.Parse(markdown);
        var outline = new DocumentOutlineService().Parse(structure);
        var html = new MarkdownPreviewService().RenderDocument(structure, null);

        Assert.Equal(structure.Headings.Count, outline.Count);
        Assert.Equal(structure.Headings.Count, structure.Headings.Select(heading => heading.Fragment).Distinct().Count());
        Assert.Equal("first-formatted-heading", structure.Headings[0].Fragment);
        Assert.Equal("café", structure.Headings[1].Fragment);
        Assert.Equal("same-1", structure.Headings[3].Fragment);
        for (var index = 0; index < outline.Count; index++)
        {
            var heading = structure.Headings[index];
            Assert.Equal(heading.Source.Offset, outline[index].Offset);
            Assert.Equal(heading.Text, outline[index].Text);
            Assert.Contains($"id=\"{heading.Fragment}\"", html);
            Assert.Equal(heading, structure.FindHeading(heading.Fragment));
        }
        Assert.Null(structure.FindHeading("Same"));
    }

    [Fact]
    public void ReparseRetainsUnchangedNodesAndAnIsolatedHeadingRename()
    {
        const string original = "# Intro\n[one](one.md)\n\n## Results\n[two](two.md)\n\n## End\n";
        var before = _service.Parse(original);
        var after = _service.Parse("A new paragraph.\n\n" + original.Replace("Results", "Final Results"), before);

        Assert.Equal(before.Headings.Select(heading => heading.NodeId), after.Headings.Select(heading => heading.NodeId));
        Assert.Equal(before.Links.Select(link => link.NodeId), after.Links.Select(link => link.NodeId));
        Assert.Equal("final-results", after.Headings[1].Fragment);
        Assert.All(after.Links, link => Assert.Contains(after.Headings, heading => heading.NodeId == link.HeadingNodeId));
    }

    [Fact]
    public void ReconciliationDoesNotTransferIdentityToChangedDestinationOrAmbiguousDuplicates()
    {
        var before = _service.Parse("# Same\n# Same\n[link](before.md)");
        var after = _service.Parse("# Same\n# Same\n# Same\n[link](after.md)", before);

        Assert.All(after.Headings, heading => Assert.DoesNotContain(before.Headings, oldHeading => oldHeading.NodeId == heading.NodeId));
        Assert.NotEqual(before.Links[0].NodeId, after.Links[0].NodeId);
        var reordered = _service.Parse("# Two\n# One", _service.Parse("# One\n# Two"));
        Assert.Equal(2, reordered.Headings.Select(heading => heading.NodeId).Distinct().Count());
    }

    [Fact]
    public void EmptyDocumentHasNoInventedStructures()
    {
        var empty = _service.Parse(string.Empty);

        Assert.Empty(empty.Headings);
        Assert.Empty(empty.Links);
        Assert.Empty(empty.ReferenceDefinitions);
        Assert.Null(empty.FindLinkAt(0));
    }

    private static string Slice(string text, DocumentSourceRange source) => text.Substring(source.Offset, source.Length);
}
