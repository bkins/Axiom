using Axiom.Core;

namespace Axiom.Core.Tests;

public sealed class MarkdownImageReferenceServiceTests
{
    private readonly MarkdownImageReferenceService _service = new();

    [Fact]
    public void CreatesEncodedForwardSlashPathRelativeToTheDocument()
    {
        using var fixture = new ImageFixture();
        var document = fixture.CreateDocument("docs/chapter.md");
        var image = fixture.CreateImage("assets/diagram one.png");

        var result = _service.CreateInsertion("Before ", 7, 0, fixture.Root, document, image);

        Assert.True(result.Success);
        Assert.Equal("Before ![diagram one](../assets/diagram%20one.png)", result.Edit!.Text);
        Assert.Equal("diagram one", result.Edit.Text.Substring(result.Edit.SelectionStart, result.Edit.SelectionLength));
    }

    [Fact]
    public void SelectedSingleLineTextBecomesAltTextAndCaretMovesAfterTheImage()
    {
        using var fixture = new ImageFixture();
        var document = fixture.CreateDocument("chapter.md");
        var image = fixture.CreateImage("images/architecture.svg");

        var result = _service.CreateInsertion("System map", 0, 10, fixture.Root, document, image);

        Assert.Equal("![System map](images/architecture.svg)", result.Edit!.Text);
        Assert.Equal(result.Edit.Text.Length, result.Edit.SelectionStart);
        Assert.Equal(0, result.Edit.SelectionLength);
    }

    [Fact]
    public void FilenamePlaceholderAndSelectedAltTextAreEscaped()
    {
        using var fixture = new ImageFixture();
        var document = fixture.CreateDocument("chapter.md");
        var image = fixture.CreateImage("a_[draft].png");

        var result = _service.CreateInsertion(string.Empty, 0, 0, fixture.Root, document, image);

        Assert.Equal("![a \\[draft\\]](a_%5Bdraft%5D.png)", result.Edit!.Text);
    }

    [Theory]
    [InlineData("image.txt", "supported image")]
    [InlineData("missing.png", "no longer exists")]
    public void UnsupportedOrMissingImagesChangeNothing(string relativeImage, string expectedError)
    {
        using var fixture = new ImageFixture();
        var document = fixture.CreateDocument("chapter.md");
        var image = relativeImage.EndsWith(".txt", StringComparison.Ordinal) ? fixture.CreateImage(relativeImage) : Path.Combine(fixture.Root, relativeImage);

        var result = _service.CreateInsertion("source", 0, 0, fixture.Root, document, image);

        Assert.False(result.Success);
        Assert.Contains(expectedError, result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ImageOutsideWorkspaceIsRejected()
    {
        using var fixture = new ImageFixture();
        using var outside = new ImageFixture();
        var document = fixture.CreateDocument("chapter.md");
        var image = outside.CreateImage("outside.png");

        var result = _service.CreateInsertion("source", 0, 0, fixture.Root, document, image);

        Assert.False(result.Success);
        Assert.Contains("inside the active workspace", result.Error);
    }

    private sealed class ImageFixture : IDisposable
    {
        public ImageFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), $"AxiomImageTests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public string CreateDocument(string relativePath)
        {
            var path = Prepare(relativePath);
            File.WriteAllText(path, "# Document");
            return path;
        }

        public string CreateImage(string relativePath)
        {
            var path = Prepare(relativePath);
            File.WriteAllBytes(path, [1, 2, 3]);
            return path;
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);

        private string Prepare(string relativePath)
        {
            var path = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            return path;
        }
    }
}
