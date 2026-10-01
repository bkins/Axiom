namespace Axiom.Core.Tests;

public sealed class StructuredWikiTests
{
    [Fact]
    public void WikiSyntaxUsesSharedSourceRangesAndIgnoresEscapesAndCode()
    {
        var source = "# Source\n\n[[Target#Deep heading|Read this]] and [[id:aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa]]\n\n`[[inline code]]` \\[[escaped]]\n\n```md\n[[fenced code]]\n```\n";
        var structure = new MarkdownStructureService().Parse(source);
        Assert.Equal(2, structure.Links.Count);
        var first = structure.Links[0];
        Assert.Equal("Read this", first.Text);
        Assert.Equal("Target#Deep heading", Uri.UnescapeDataString(first.Destination[WikiLinkResolver.Prefix.Length..]));
        Assert.Equal("[[Target#Deep heading|Read this]]", source.Substring(first.Source.Offset, first.Source.Length));
        Assert.Equal(first, structure.FindLinkAt(first.Source.Offset + 3));
        var preview = new MarkdownPreviewService().RenderDocument(structure, null, linkToken: "safe-token");
        Assert.Contains("axiom-link://follow/safe-token/0", preview);
        Assert.Contains("Read this</a>", preview);
        Assert.Contains("[[inline code]]", preview);
    }

    [Fact]
    public void StructureIncludesExactTaskCodeTableSectionRangesAndReconcilesOrdinaryEdits()
    {
        var parser = new MarkdownStructureService();
        var source = "# Heading\n\n- [ ] Todo\n- [x] Done\n\n```csharp\nvar value = 1;\n```\n\n| A | B |\n|---|---|\n| 1 | 2 |\n";
        var before = parser.Parse(source);
        Assert.Equal(2, before.Nodes.Count(node => node.Kind == DocumentStructureNodeKind.Task));
        Assert.Contains(before.Nodes, node => node.Kind == DocumentStructureNodeKind.Task && node.Checked == true);
        Assert.Contains(before.Nodes, node => node.Kind == DocumentStructureNodeKind.Code && node.Language == "csharp");
        Assert.Single(before.Nodes, node => node.Kind == DocumentStructureNodeKind.Table);
        Assert.Equal(source.Length, before.Nodes.Single(node => node.Kind == DocumentStructureNodeKind.Section).Source.EndOffset);
        var after = parser.Parse("intro\n\n" + source.Replace("[ ] Todo", "[x] Todo", StringComparison.Ordinal), before);
        foreach (var original in before.Nodes)
            Assert.Contains(after.Nodes, current => current.Kind == original.Kind && current.NodeId == original.NodeId);
        Assert.All(after.Nodes, node => Assert.InRange(node.Source.EndOffset, node.Source.Offset, after.SourceText.Length));
    }

    [Fact]
    public void WikiTargetsResolveAliasesIdentityAndPathsAndExposeAmbiguityAndMissingTargets()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "WikiFixture"));
        var parser = new MarkdownStructureService();
        var identity = Guid.NewGuid();
        var first = Path.Combine(root, "one", "Target.md");
        var second = Path.Combine(root, "two", "Target.md");
        var documents = new Dictionary<string, DocumentStructure>
        {
            [first] = parser.Parse($"<!-- axiom:documentId={identity:D} -->\n# Unique title\n\n## Deep heading\n")
          , [second] = parser.Parse("# Other title\n")
        };
        var resolver = new WikiLinkResolver(root, documents, [new(identity, [], null, ["Alias"])]);
        string Link(string target) => WikiLinkResolver.Prefix + Uri.EscapeDataString(target);
        Assert.Equal(MarkdownLinkResolutionStatus.Ambiguous, resolver.Resolve(first, Link("Target")).Status);
        Assert.Equal(2, resolver.Resolve(first, Link("Target")).Candidates.Count);
        Assert.Equal(first, resolver.Resolve(first, Link("Alias")).TargetPath);
        Assert.Equal(first, resolver.Resolve(first, Link("id:" + identity)).TargetPath);
        Assert.Equal(second, resolver.Resolve(first, Link("two/Target.md")).TargetPath);
        var heading = resolver.Resolve(first, Link("Unique title#Deep heading"));
        Assert.Equal("deep-heading", heading.Fragment);
        Assert.Equal(documents[first].Headings[1].Source, heading.Source);
        Assert.Equal(MarkdownLinkResolutionStatus.MissingDocument, resolver.Resolve(first, Link("Missing")).Status);
        Assert.Equal(MarkdownLinkResolutionStatus.InvalidPath, resolver.Resolve(first, Link("../escape.md")).Status);
        Assert.Equal(MarkdownLinkResolutionStatus.InvalidPath, resolver.Resolve(first, Link("https://example.com")).Status);
        Assert.Equal(MarkdownLinkResolutionStatus.OutsideWorkspace, resolver.Resolve(Path.Combine(Path.GetTempPath(), "outside.md"), Link("Alias")).Status);
        Assert.Equal(MarkdownLinkResolutionStatus.Unverified, new WikiLinkResolver(root, documents, complete: false).Resolve(first, Link("Alias")).Status);
    }
}
