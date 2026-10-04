using System.Net;
using System.Text.RegularExpressions;
using Axiom.Core;

namespace Axiom.Core.Tests;

public sealed class AuthoringPolishTests
{
    [Theory]
    [InlineData("note")]
    [InlineData("info")]
    [InlineData("tip")]
    [InlineData("warning")]
    [InlineData("danger")]
    [InlineData("important")]
    [InlineData("caution")]
    public void CalloutsAfterOrdinaryTextHideMarkersPreserveBodiesAndRenderRepeatedly(string kind)
    {
        var source = $"# Before\n\nOrdinary **formatted** paragraph.\n\n>[!{kind}] Custom title\n> Body **bold**\n>\n> - Item\n>\n> > Nested\n\n> [!unknown] Keep literal\n> Body";
        var structure = new MarkdownStructureService().Parse(source);
        foreach (var token in new string?[] { null, "current" })
        {
            var html = new MarkdownPreviewService().RenderDocument(structure, null, linkToken: token);
            var body = html[(html.IndexOf("<body>", StringComparison.Ordinal) + 6)..];
            Assert.DoesNotContain($"[!{kind}]", body);
            Assert.Contains("class=\"callout-icon\"", body);
            Assert.Contains("Custom title", body);
            Assert.Contains("Body <strong>bold</strong>", body);
            Assert.Contains("<li>Item</li>", body);
            Assert.Contains("Nested", body);
            Assert.Contains("[!unknown]", body);
            Assert.Equal(source, structure.SourceText);
        }
    }

    [Theory]
    [InlineData("c#")]
    [InlineData("csharp")]
    [InlineData("CSHARP")]
    public void CSharpColourPreservesEncodedSourceWithoutExecutingIt(string language)
    {
        const string code = "public string GetString(int index)\n{\n\t// <script>alert(1)</script>\n\treturn \"<b>value</b>\" + 42;\n}";
        var html = new MarkdownPreviewService().RenderDocument($"```{language}\n{code}\n```", null);
        var match = Regex.Match(html, "<pre><code class=\"language-csharp\">(.*?)</code></pre>", RegexOptions.Singleline);
        Assert.True(match.Success);
        Assert.Contains("csharp-keyword", match.Value);
        Assert.Contains("csharp-comment", match.Value);
        Assert.Contains("csharp-string", match.Value);
        Assert.Contains("csharp-number", match.Value);
        Assert.Equal(code + "\n", WebUtility.HtmlDecode(Regex.Replace(match.Groups[1].Value, "<[^>]+>", "")));
        Assert.DoesNotContain("<script>", match.Value);
    }

    [Fact]
    public void LexicalStatesDoNotColourKeywordsInsideMultilineCommentsOrStrings()
    {
        const string code = "/* public\nreturn */\nvar text = @\"return \"\"public\"\"\nclass\";\nvar raw = \"\"\"return\npublic\"\"\";\nreturn 42;";
        var tokens = CSharpSyntax.Tokenize(code);
        Assert.Equal(new[] { "var", "var", "return" }, tokens.Where(token => token.Kind == "keyword").Select(token => code.Substring(token.Offset, token.Length)));
        Assert.Equal(2, tokens.Count(token => token.Kind == "string"));
        Assert.Empty(CSharpSyntax.Tokenize(new string('a', 1_048_577)));
        Assert.DoesNotContain("csharp-keyword", new MarkdownPreviewService().RenderDocument("```text\npublic return\n```", null).Split("<body>")[1]);
    }
}
