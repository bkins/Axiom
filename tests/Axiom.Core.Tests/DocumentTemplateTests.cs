using Axiom.Core;

namespace Axiom.Core.Tests;

public sealed class DocumentTemplateTests
{
    [Fact]
    public void ExpansionPreservesMarkdownAndLiteralUnknownTokensWithoutReexpandingTitle()
    {
        var text = DocumentTemplateService.Expand("# {{title}}\r\n{{date}}\n{{unknown}} {{TITLE}}", "Café {{date}}", new DateOnly(2026, 10, 1));
        Assert.Equal("# Café {{date}}\r\n2026-10-01\n{{unknown}} {{TITLE}}", text);
    }

    [Fact]
    public void OnlyCanonicalFirstLineIdentityIsRemoved()
    {
        var marker = $"<!-- axiom:documentId={Guid.NewGuid():D} -->";
        var source = "\uFEFF" + marker + "\r\n# {{title}}\n```\n" + marker + "\n```";
        var result = DocumentTemplateService.Expand(source, "New", new DateOnly(2026, 10, 1));
        Assert.Equal("# New\n```\n" + marker + "\n```", result);
        Assert.Null(DocumentIdCodec.Find(result));
        Assert.Contains(marker, source);
        Assert.Equal("", DocumentTemplateService.Expand(marker, "New", new DateOnly(2026, 10, 1)));
        Assert.Equal("Example " + marker, DocumentTemplateService.Expand("Example " + marker, "New", new DateOnly(2026, 10, 1)));
        Assert.Throws<ArgumentException>(() => DocumentTemplateService.Expand("", "bad\ntitle", new DateOnly(2026, 10, 1)));
    }
}
