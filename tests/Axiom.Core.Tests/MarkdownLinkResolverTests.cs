using Axiom.Core;

namespace Axiom.Core.Tests;

public sealed class MarkdownLinkResolverTests
{
    private readonly string _root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "AxiomLinks"));

    [Theory]
    [InlineData("../notes/one%20file.markdown#caf%C3%A9", "notes/one file.markdown", "café", 2)]
    [InlineData("../notes/one file.md#summary-1", "notes/one file.md", "summary-1", 3)]
    [InlineData("#summary", "chapters/source.md", "summary", 1)]
    [InlineData("../notes/%2520.md", "notes/%20.md", "", 0)]
    [InlineData("../notes/a%23b.md#", "notes/a#b.md", "", 0)]
    public async Task ResolvesRelativePathsAndDecodedExactHeadingFragments(string destination, string relative, string fragment, int line)
    {
        var reader = new LinkTargetReaderStub();
        var source = Path.Combine(_root, "chapters", "source.md");
        var result = await new MarkdownLinkResolver(reader).ResolveAsync(_root, source, destination);

        Assert.Equal(MarkdownLinkResolutionStatus.Resolved, result.Status);
        Assert.Equal(Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar)), result.TargetPath);
        Assert.Equal(fragment, result.Fragment);
        Assert.Equal(line == 0 ? null : (int?)line, result.Source?.LineNumber);
        Assert.Equal(result.TargetPath, reader.LastPath);
    }

    [Theory]
    [InlineData("../../outside.md", MarkdownLinkResolutionStatus.OutsideWorkspace)]
    [InlineData("../%2E%2E/outside.md", MarkdownLinkResolutionStatus.OutsideWorkspace)]
    [InlineData("https://example.com/a.md", MarkdownLinkResolutionStatus.UnsupportedScheme)]
    [InlineData("javascript%3Aalert(1)", MarkdownLinkResolutionStatus.UnsupportedScheme)]
    [InlineData("file:///C:/docs/a.md", MarkdownLinkResolutionStatus.UnsupportedScheme)]
    [InlineData("/absolute.md", MarkdownLinkResolutionStatus.AbsolutePath)]
    [InlineData("\\\\server\\share\\a.md", MarkdownLinkResolutionStatus.AbsolutePath)]
    [InlineData("a.md?query=yes", MarkdownLinkResolutionStatus.UnsupportedQuery)]
    [InlineData("a.pdf", MarkdownLinkResolutionStatus.UnsupportedDocument)]
    [InlineData("bad%2.md", MarkdownLinkResolutionStatus.InvalidPath)]
    [InlineData("bad%GG.md", MarkdownLinkResolutionStatus.InvalidPath)]
    [InlineData("bad% 2.md", MarkdownLinkResolutionStatus.InvalidPath)]
    [InlineData("bad%FF.md", MarkdownLinkResolutionStatus.InvalidPath)]
    [InlineData("bad%00.md", MarkdownLinkResolutionStatus.InvalidPath)]
    [InlineData("a.md#bad%00", MarkdownLinkResolutionStatus.InvalidPath)]
    public async Task RejectsUnsupportedDestinationsBeforeAnyTargetRead(string destination, MarkdownLinkResolutionStatus expected)
    {
        var reader = new LinkTargetReaderStub();
        var result = await new MarkdownLinkResolver(reader).ResolveAsync(_root, Path.Combine(_root, "chapters", "source.md"), destination);

        Assert.Equal(expected, result.Status);
        Assert.Equal(0, reader.Reads);
        Assert.Null(result.TargetPath);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    [Fact]
    public async Task UsesUnsavedTargetStructureAndReportsMissingHeadingWithoutInventingALocation()
    {
        var reader = new LinkTargetReaderStub();
        var resolver = new MarkdownLinkResolver(reader);
        var open = new MarkdownStructureService().Parse("\n\n# Unsaved Title");
        var source = Path.Combine(_root, "source.md");
        var found = await resolver.ResolveAsync(_root, source, "target.md#unsaved-title", _ => open);
        var missing = await resolver.ResolveAsync(_root, source, "target.md#Summary");

        Assert.Equal(MarkdownLinkResolutionStatus.Resolved, found.Status);
        Assert.Equal(3, found.Source?.LineNumber);
        Assert.Equal(MarkdownLinkResolutionStatus.MissingHeading, missing.Status);
        Assert.Equal(Path.Combine(_root, "target.md"), missing.TargetPath);
        Assert.Equal("Summary", missing.Fragment);
        Assert.Null(missing.Source);
    }

    [Theory]
    [InlineData(MarkdownLinkResolutionStatus.MissingDocument)]
    [InlineData(MarkdownLinkResolutionStatus.ExcludedPath)]
    [InlineData(MarkdownLinkResolutionStatus.Unavailable)]
    public async Task PropagatesReaderOutcomesWithoutUsingAnOpenOverlayToBypassExclusions(MarkdownLinkResolutionStatus status)
    {
        var reader = new LinkTargetReaderStub { Result = new(status, Message: "Unavailable target.") };
        var overlayConsulted = false;
        var result = await new MarkdownLinkResolver(reader).ResolveAsync(_root, Path.Combine(_root, "source.md"), "a.md#summary", _ =>
        {
            overlayConsulted = true;
            return null;
        });

        Assert.Equal(status, result.Status);
        Assert.False(overlayConsulted);
        Assert.Null(result.Source);
    }

    [Fact]
    public async Task WorkspaceUnsavedSourceAndCancellationHaveExplicitBoundaries()
    {
        var reader = new LinkTargetReaderStub();
        var resolver = new MarkdownLinkResolver(reader);
        Assert.Equal(MarkdownLinkResolutionStatus.NoWorkspace, (await resolver.ResolveAsync(null, null, "a.md")).Status);
        Assert.Equal(MarkdownLinkResolutionStatus.SourceNotSaved, (await resolver.ResolveAsync(_root, null, "a.md")).Status);
        Assert.Equal(MarkdownLinkResolutionStatus.OutsideWorkspace, (await resolver.ResolveAsync(_root, Path.Combine(Path.GetTempPath(), "outside.md"), "a.md")).Status);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => resolver.ResolveAsync(_root, Path.Combine(_root, "source.md"), "a.md", cancellationToken: cancellation.Token));
        Assert.Equal(0, reader.Reads);
    }
}
