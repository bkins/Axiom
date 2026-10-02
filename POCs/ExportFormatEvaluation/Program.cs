using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Axiom.Core;
using Axiom.Infrastructure;

namespace Axiom.ExportFormatEvaluation;

internal static class Program
{
    public static async Task Main(string[] arguments)
    {
        if (arguments.Length != 1) throw new ArgumentException("Pass a fresh evaluation output directory.");
        var output = Path.GetFullPath(arguments[0]);
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Evaluation output already exists; it was preserved.");
        Directory.CreateDirectory(output);
        var source = Path.Combine(output, "source.md");
        var saved = Encoding.UTF8.GetBytes("# Saved Markdown\n\nOriginal source is unchanged.");
        await File.WriteAllBytesAsync(source, saved);
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jlp8AAAAASUVORK5CYII=");
        await File.WriteAllBytesAsync(Path.Combine(output, "image.png"), png);
        var markdown = DocumentIdCodec.Enroll("# Café ☕\n\nUnsaved **bold** and *italic* and `code`.\n\n[Heading](#café-) [Web](https://example.com) [Local](other.md) [Unsafe](javascript:alert)\n\n## Section 二\n\n- [x] Done\n- [ ] Todo\n\n3. Three\n4. Four\n\n> Quote\n\n| Column | Value |\n|---|---|\n| 一 | Two |\n\n```cs\nvar value = 1;\n```\n\n![Pixel](image.png) ![Again](image.png) ![Remote](https://invalid.example/image.png)\n\n<script>alert(1)</script>", Guid.NewGuid());
        var export = new DocumentExportService();
        var snapshot = new ExportSnapshot(markdown, source);
        var epub = Path.Combine(output, "export.epub");
        var epubResult = await export.WriteAsync(snapshot, epub, false, [source], Path.Combine(output, "state"));
        var openDocument = await export.WriteAsync(snapshot, Path.Combine(output, "export.odt"), false, [source], Path.Combine(output, "state"));
        var bytes = await File.ReadAllBytesAsync(epub);
        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        if (archive.Entries[0].FullName != "mimetype" || archive.Entries[0].CompressedLength != archive.Entries[0].Length)
            throw new IOException("EPUB mimetype ordering/compression failed.");
        var entry = archive.GetEntry("EPUB/content.xhtml")!;
        using var contentStream = entry.Open();
        var content = XDocument.Load(contentStream);
        XNamespace html = "http://www.w3.org/1999/xhtml";
        if (content.Descendants(html + "script").Any() || content.ToString().Contains("documentId", StringComparison.Ordinal))
            throw new IOException("Identity or active-content leakage.");
        if (archive.Entries.Count(value => value.FullName.StartsWith("EPUB/images/", StringComparison.Ordinal)) != 1)
            throw new IOException("Image deduplication failed.");
        var retainedSource = await File.ReadAllBytesAsync(source);
        if (!saved.SequenceEqual(retainedSource)) throw new IOException("Source changed.");
        var evidence = new
        {
            FixtureOnly = true
          , ProductionExportEnabled = true
          , EpubSha256 = Convert.ToHexString(SHA256.HashData(bytes))
          , SourceUnchanged = true
          , UnsavedContentRendered = content.ToString().Contains("Unsaved", StringComparison.Ordinal)
          , IdentityExcluded = true
          , ImageDeduplicated = true
          , UnsafeContentExcluded = true
          , Warnings = epubResult.Warnings.Concat(openDocument.Warnings).Distinct().ToArray()
          , ReaderFidelityVerified = false
        };
        var json = JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(output, "evaluation.json"), json);
        Console.WriteLine(json);
    }
}
