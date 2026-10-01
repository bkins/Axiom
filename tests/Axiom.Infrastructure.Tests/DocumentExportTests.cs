using System.Text;
using Axiom.Core;
using Axiom.Infrastructure;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;
using Xunit;

namespace Axiom.Infrastructure.Tests;

public sealed class DocumentExportTests
{
    [Fact]
    public async Task BothFormatsPreserveStructuredContentImagesAndSourceWithoutIdentityLeakage()
    {
        using var fixture = new MutationTestWorkspace();
        var path = Path.Combine(fixture.Root, "source.md");
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jlp8AAAAASUVORK5CYII=");
        await File.WriteAllBytesAsync(Path.Combine(fixture.Root, "image.png"), png);
        var markdown = DocumentIdCodec.Enroll("# Café\n\n**Bold** and *italic* and `code` [link](https://example.com)\n\n- [x] Done\n- [ ] Todo\n\n3. Three\n4. Four\n\n- Another list\n\n> Quotation\n\n| A | B |\n|---|---|\n| 一 | Two |\n\n```cs\nvar x = 1;\n```\n\n![image](image.png)", Guid.NewGuid());
        await File.WriteAllTextAsync(path, "saved source");
        var snapshot = new ExportSnapshot(markdown, path);
        var service = new DocumentExportService();
        var htmlPath = Path.Combine(fixture.Root, "out.html");
        var wordPath = Path.Combine(fixture.Root, "out.docx");
        var html = await service.WriteAsync(snapshot, htmlPath, false, [path], fixture.State.Paths.Root);
        var word = await service.WriteAsync(snapshot, wordPath, false, [path], fixture.State.Paths.Root);
        Assert.Empty(html.Warnings);
        Assert.Empty(word.Warnings);
        var text = await File.ReadAllTextAsync(htmlPath);
        Assert.Contains("Café</h1>", System.Net.WebUtility.HtmlDecode(text));
        Assert.Contains("data:image/png;base64,", text);
        Assert.Contains("☑", System.Net.WebUtility.HtmlDecode(text));
        Assert.DoesNotContain("documentId", text);
        Assert.Equal("saved source", await File.ReadAllTextAsync(path));
        using var document = WordprocessingDocument.Open(wordPath, false);
        var errors = new OpenXmlValidator().Validate(document).Select(error => error.Description + " at " + error.Path?.XPath).ToArray();
        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors));
        Assert.Contains("Café", document.MainDocumentPart!.Document!.InnerText);
        Assert.DoesNotContain("documentId", document.MainDocumentPart!.Document!.InnerText);
        Assert.Single(document.MainDocumentPart!.ImageParts);
        Assert.NotEmpty(document.MainDocumentPart!.Document!.Descendants<Table>());
        Assert.NotEmpty(document.MainDocumentPart!.Document!.Descendants<NumberingProperties>());
        Assert.NotEmpty(document.MainDocumentPart!.HyperlinkRelationships);
        using var imageStream = document.MainDocumentPart!.ImageParts.Single().GetStream();
        using var copy = new MemoryStream();
        imageStream.CopyTo(copy);
        Assert.Equal(png, copy.ToArray());
    }

    [Fact]
    public async Task UnsafeLinksRemoteAndUntitledImagesAreWarnedWithoutFetchOrExecution()
    {
        using var fixture = new MutationTestWorkspace();
        var service = new DocumentExportService();
        var snapshot = new ExportSnapshot("[unsafe](javascript:alert) ![remote](https://invalid.example/image.png) ![local](missing.png) <script>alert(1)</script>", null);
        var output = Path.Combine(fixture.Root, "safe.html");
        var result = await service.WriteAsync(snapshot, output, false, [], fixture.State.Paths.Root);
        Assert.Equal(3, result.Warnings.Count);
        var html = await File.ReadAllTextAsync(output);
        Assert.DoesNotContain("href=\"javascript:", html);
        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain("src=\"https:", html);
        Assert.Contains("remote", html);
    }

    [Fact]
    public async Task DeclinedOverwriteCancellationAndProtectedDestinationsPreserveBytes()
    {
        using var fixture = new MutationTestWorkspace();
        var service = new DocumentExportService();
        var output = Path.Combine(fixture.Root, "existing.html");
        await File.WriteAllTextAsync(output, "original");
        var snapshot = new ExportSnapshot("new", null);
        await Assert.ThrowsAsync<IOException>(() => service.WriteAsync(snapshot, output, false, [], fixture.State.Paths.Root));
        await Assert.ThrowsAsync<IOException>(() => service.WriteAsync(snapshot, output, true, [output], fixture.State.Paths.Root));
        await Assert.ThrowsAsync<IOException>(() => service.WriteAsync(snapshot, Path.Combine(fixture.State.Paths.Root, "state.html"), true, [], fixture.State.Paths.Root));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.WriteAsync(snapshot, output, true, [], fixture.State.Paths.Root, cancellation.Token));
        Assert.Equal("original", await File.ReadAllTextAsync(output));
        Assert.Empty(Directory.GetFiles(fixture.Root, "*.tmp"));
        await service.WriteAsync(snapshot, output, true, [], fixture.State.Paths.Root);
        Assert.Contains("new", await File.ReadAllTextAsync(output));
    }

    [Fact]
    public async Task FailedCommitCleansTemporaryAndHeadingLinksUseRealBookmarks()
    {
        using var fixture = new MutationTestWorkspace();
        var service = new DocumentExportService();
        var directory = Path.Combine(fixture.Root, "directory.html");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "keep.txt"), "keep");
        var failure = await Record.ExceptionAsync(() => service.WriteAsync(new("# Test", null), directory, true, [], fixture.State.Paths.Root));
        Assert.True(failure is IOException or UnauthorizedAccessException, failure?.ToString());
        Assert.Equal("keep", await File.ReadAllTextAsync(Path.Combine(directory, "keep.txt")));
        Assert.Empty(Directory.GetFiles(fixture.Root, "*.tmp"));
        var output = Path.Combine(fixture.Root, "links.docx");
        var result = await service.WriteAsync(new("# Same\n\n[Jump](#same) and **`bold code`**\n\n# Same\n\n[Next](#same-1)", null), output, false, [], fixture.State.Paths.Root);
        Assert.Empty(result.Warnings);
        using var document = WordprocessingDocument.Open(output, false);
        Assert.Empty(new OpenXmlValidator().Validate(document));
        var body = document.MainDocumentPart!.Document!.Body!;
        var names = body.Descendants<BookmarkStart>().Select(bookmark => bookmark.Name!.Value).ToArray();
        Assert.All(body.Descendants<Hyperlink>(), link => Assert.Contains(link.Anchor!.Value, names));
    }
}
