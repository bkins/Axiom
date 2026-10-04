using Axiom.App;
using Xunit;

namespace Axiom.App.Tests;

public sealed class NativeHtmlContentTests
{
    [Fact]
    public void LargeUnicodeHtmlUsesAnExactPrivateFileAndCleansOnlyOwnedFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "AxiomHtml-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var unrelated = Path.Combine(root, "unrelated.html"); File.WriteAllText(unrelated, "Keep");
            var content = new NativeHtmlContent(root);
            Uri? loaded = null;
            var html = "<html><body>" + new string('界', 600_000) + "</body></html>";
            content.Load(html, _ => throw new Exception("Large HTML must not use NavigateToString"), uri => loaded = uri);
            Assert.NotNull(loaded);
            Assert.Equal(html, File.ReadAllText(loaded.LocalPath));
            Assert.True(content.Allows(loaded));
            Assert.False(content.Allows(new Uri(unrelated)));
            Assert.False(content.Allows(new Uri(loaded.AbsoluteUri + "?other")));
            Assert.False(content.Allows(new Uri("https://example.com/")));
            content.Dispose();
            Assert.False(File.Exists(loaded.LocalPath));
            Assert.Equal("Keep", File.ReadAllText(unrelated));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void SmallContentUsesTheStringRouteAndFailuresRemainCatchable()
    {
        var root = Path.Combine(Path.GetTempPath(), "AxiomHtml-" + Guid.NewGuid().ToString("N"));
        using var content = new NativeHtmlContent(root);
        string? loaded = null;
        content.Load("<p>Small</p>", text => loaded = text, _ => throw new Exception("Unexpected file transport"));
        Assert.Equal("<p>Small</p>", loaded);
        Assert.False(Directory.Exists(root));
        Assert.Throws<IOException>(() => content.Load("Small", _ => throw new IOException("Adapter failure"), _ => { }));
    }
}
