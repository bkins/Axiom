using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Axiom.Core;
using Axiom.Infrastructure;

namespace Axiom.Infrastructure.Tests;

public sealed class AdditionalExportTests
{
    [Theory]
    [InlineData("epub", "EPUB/content.xhtml")]
    [InlineData("odt", "content.xml")]
    public async Task PackagesUnsavedUnicodeStructureAndDeduplicatedImagesWithoutSourceIdentity(string extension, string contentPath)
    {
        using var fixture = new MutationTestWorkspace();
        var source = Path.Combine(fixture.Root, "source.md");
        await File.WriteAllTextAsync(source, "saved source");
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jlp8AAAAASUVORK5CYII=");
        await File.WriteAllBytesAsync(Path.Combine(fixture.Root, "image.png"), png);
        var markdown = DocumentIdCodec.Enroll("# Café ☕\n\nUnsaved **bold** and *italic* and `code`.\n\n- [x] Done\n- [ ] Todo\n\n3. Three\n4. Four\n\n> Quote\n\n| A | B |\n|---|---|\n| 一 | Two |\n\n```cs\nline one\n  line two\n```\n\n![Pixel](image.png) ![Again](image.png)", Guid.NewGuid());
        var output = Path.Combine(fixture.Root, "out." + extension);
        var result = await new DocumentExportService().WriteAsync(new(markdown, source), output, false, [source], fixture.State.Paths.Root);
        Assert.Empty(result.Warnings);
        Assert.Equal("saved source", await File.ReadAllTextAsync(source));
        using var archive = ZipFile.OpenRead(output);
        Assert.Equal("mimetype", archive.Entries[0].FullName);
        Assert.Equal(archive.Entries[0].Length, archive.Entries[0].CompressedLength);
        using var stream = archive.GetEntry(contentPath)!.Open();
        var content = XDocument.Load(stream);
        var xml = content.ToString();
        Assert.Contains("Café", xml);
        Assert.Contains("☕", xml);
        Assert.Contains("Unsaved", xml);
        Assert.DoesNotContain("documentId", xml);
        Assert.Contains("☑", xml);
        Assert.Contains("一", xml);
        Assert.Contains(content.Descendants(), element => element.Name.LocalName == "table");
        var image = Assert.Single(archive.Entries, entry => entry.FullName.EndsWith(".png", StringComparison.Ordinal));
        using var imageStream = image.Open();
        using var imageBytes = new MemoryStream();
        imageStream.CopyTo(imageBytes);
        Assert.Equal(png, imageBytes.ToArray());
        foreach (var entry in archive.Entries.Where(entry => entry.FullName.EndsWith(".xml", StringComparison.Ordinal) || entry.FullName.EndsWith(".xhtml", StringComparison.Ordinal) || entry.FullName.EndsWith(".opf", StringComparison.Ordinal)))
        {
            using var entryStream = entry.Open();
            _ = XDocument.Load(entryStream);
        }
    }

    [Theory]
    [InlineData("epub", "EPUB/content.xhtml")]
    [InlineData("odt", "content.xml")]
    public async Task UnsafeRemoteAndUnpackagedLocalResourcesAreOmittedWithWarnings(string extension, string contentPath)
    {
        using var fixture = new MutationTestWorkspace();
        var output = Path.Combine(fixture.Root, "out." + extension);
        var snapshot = new ExportSnapshot("# Safe\n\n[local](other.md) [unsafe](javascript:alert) ![remote](https://invalid.example/pixel.png) <script>alert(1)</script>", null);
        var result = await new DocumentExportService().WriteAsync(snapshot, output, false, [], fixture.State.Paths.Root);
        Assert.NotEmpty(result.Warnings);
        using var archive = ZipFile.OpenRead(output);
        using var stream = archive.GetEntry(contentPath)!.Open();
        var document = XDocument.Load(stream);
        Assert.DoesNotContain(document.Descendants(), element => element.Name.LocalName == "script");
        Assert.DoesNotContain(document.Descendants().Attributes(), attribute => attribute.Value.Contains("javascript:", StringComparison.Ordinal) || attribute.Value.Contains("invalid.example", StringComparison.Ordinal) || attribute.Value == "other.md");
    }

    [Theory]
    [InlineData("epub")]
    [InlineData("odt")]
    public async Task CancellationOutputFailureAndProtectedPathsPreserveExistingBytes(string extension)
    {
        using var fixture = new MutationTestWorkspace();
        var output = Path.Combine(fixture.Root, "out." + extension);
        var retained = Encoding.UTF8.GetBytes("retained output");
        await File.WriteAllBytesAsync(output, retained);
        var service = new DocumentExportService();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.WriteAsync(new("# New", null), output, true, [], fixture.State.Paths.Root, new CancellationToken(true)));
        Assert.Equal(retained, await File.ReadAllBytesAsync(output));
        await Assert.ThrowsAsync<IOException>(() => service.WriteAsync(new("# New", output), output, true, [], fixture.State.Paths.Root));
        await Assert.ThrowsAsync<IOException>(() => service.WriteAsync(new("# New", null), output, true, [output], fixture.State.Paths.Root));
        await Assert.ThrowsAsync<IOException>(() => service.WriteAsync(new("# New", null), Path.Combine(fixture.State.Paths.Root, "state." + extension), true, [], fixture.State.Paths.Root));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => service.WriteAsync(new("Invalid \uD800 surrogate", null), output, true, [], fixture.State.Paths.Root));
        Assert.Equal(retained, await File.ReadAllBytesAsync(output));
        Assert.Empty(Directory.GetFiles(fixture.Root, "*.tmp"));
    }
}
