using Axiom.Core;
using Xunit;

namespace Axiom.Core.Tests;

public sealed class PreviewLinkNavigationTests
{
    [Fact]
    public void PreviewLinksUseStructuralLocationsWithoutEnablingDocumentScriptsOrChangingImages()
    {
        var structure = new MarkdownStructureService().Parse("[First](other.md#heading)\n\n[Second][target]\n\n[target]: other.md#heading\n\n![Image](picture.png)\n\n`[Code](bad.md)`\n\n<script>alert(1)</script>");
        var html = new MarkdownPreviewService().RenderDocument(structure, null, linkToken: "render123");
        Assert.Contains("href=\"axiom-link://follow/render123/0\"", html);
        Assert.Contains("href=\"axiom-link://follow/render123/1\"", html);
        Assert.DoesNotContain("axiom-link://follow/render123/2", html);
        Assert.Contains("script-src 'none'", html);
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("<img src=\"picture.png\"", html);
        Assert.Equal(2, structure.Links.Count);
        Assert.Equal("other.md#heading", structure.Links[0].Destination);
    }
}
