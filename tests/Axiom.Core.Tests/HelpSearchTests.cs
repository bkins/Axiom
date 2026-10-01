using Axiom.Core;
using Xunit;

namespace Axiom.Core.Tests;

public sealed class HelpSearchTests
{
    [Fact]
    public void FindsBodyHeadingAndUnicodeWithoutRequiringHeadingMatch()
    {
        var service = new HelpSearchService();
        const string markdown = "# Guide\n\nWelcome\n\n## Export\n\nUse **Word** for café documents.\n\n## Recovery\n\nSaved buffers stay safe.";
        Assert.Empty(service.Search(markdown, "  "));
        Assert.Empty(service.Search(markdown, "missing"));
        var body = Assert.Single(service.Search(markdown, "WORD"));
        Assert.Equal("Export", body.Topic);
        Assert.Equal("export", body.Fragment);
        Assert.Contains("Word", body.Excerpt);
        Assert.Single(service.Search(markdown, "CAFÉ"));
        Assert.Single(service.Search(markdown, "Recovery"));
    }
}
