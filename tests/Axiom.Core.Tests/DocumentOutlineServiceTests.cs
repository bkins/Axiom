using Axiom.Core;

namespace Axiom.Core.Tests;

public sealed class DocumentOutlineServiceTests
{
    [Fact]
    public void ParsesAtxSetextAndFormattedHeadingsButNotFencedCode()
    {
        const string markdown = """
            # First *formatted* heading

            ```markdown
            ## Not a heading
            ```

            Second heading
            --------------

            ## Duplicate
            ## Duplicate
            """;

        var outline = new DocumentOutlineService().Parse(markdown);

        Assert.Equal(4, outline.Count);
        Assert.Equal([1, 2, 2, 2], outline.Select(entry => entry.Level));
        Assert.Equal(["First formatted heading", "Second heading", "Duplicate", "Duplicate"], outline.Select(entry => entry.Text));
        Assert.NotEqual(outline[2].Offset, outline[3].Offset);
        Assert.DoesNotContain(outline, entry => entry.Text == "Not a heading");
    }

    [Fact]
    public void EmptyAndHeadingFreeDocumentsHaveNoOutline()
    {
        var service = new DocumentOutlineService();

        Assert.Empty(service.Parse(string.Empty));
        Assert.Empty(service.Parse("ordinary paragraph\n\n```\n# code\n```"));
    }
}
