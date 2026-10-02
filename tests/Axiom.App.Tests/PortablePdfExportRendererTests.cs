using System.Text;
using Axiom.Core;
using Axiom.Infrastructure;
using SkiaSharp;
using Xunit;

namespace Axiom.App.Tests;

public sealed class PortablePdfExportRendererTests
{
    [Theory]
    [InlineData(PdfPageSize.Letter)]
    [InlineData(PdfPageSize.A4)]
    public async Task Export_PaginatesOfflineFixture_ProtectsSourceAndDestination(PdfPageSize size)
    {
        var evidence = Environment.GetEnvironmentVariable("AXIOM_PDF_EVIDENCE_ROOT");
        var root = evidence ?? Path.Combine(Path.GetTempPath(), "AxiomPdf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var source = Path.Combine(root, size + "-source.md");
        var destination = Path.Combine(root, size + ".pdf");
        await File.WriteAllTextAsync(source, "saved source", TestContext.Current.CancellationToken);
        using (var bitmap = new SKBitmap(64, 32))
        {
            bitmap.Erase(SKColors.Teal);
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            await File.WriteAllBytesAsync(Path.Combine(root, size + "-image.png"), data.ToArray(), TestContext.Current.CancellationToken);
        }
        var fixture = new StringBuilder("# Offline PDF fixture\n\nCafé — 日本語 一 — Ελληνικά — Привет — 😀\n\nUnsaved **bold**, *italic* and `code`. [Safe link](https://example.com).\n\n- [x] Done\n- [ ] Todo\n  - Nested\n\n3. Three\n4. Four\n\n> Quotation paragraph.\n\n![Missing image](missing.png)\n\n```text\n" + new string('X', 250) + "\nEnd of long code\n```\n\n| Header A | Header B |\n|---|---|\n");
        for (var index = 1; index <= 100; index++) fixture.AppendLine($"| Table row {index} | Café 日本語 — content wraps {new string('W', 60)} |");
        fixture.AppendLine("\n| Tall header | Other header |\n|---|---|\n| " + string.Join(" ", Enumerable.Range(1, 900).Select(index => "TallToken" + index)) + " | Short cell |\n");
        for (var index = 1; index <= 45; index++) fixture.AppendLine($"\n## Section {index}\n\nParagraph {index}: searchable unsaved content.\n");
        fixture.AppendLine("\nFinal fixture marker\n");
        fixture.AppendLine($"\n![Local sample]({size}-image.png)\n");
        var snapshot = new ExportSnapshot(DocumentIdCodec.Enroll(fixture.ToString(), Guid.NewGuid()), source);
        var service = new DocumentExportService(structuredPdfRenderer: new PortablePdfExportRenderer());

        try
        {
            var result = await service.WriteAsync(snapshot, destination, false, [source], Path.Combine(root, "state"), cancellationToken: TestContext.Current.CancellationToken, pdfPageSize: size);
            var bytes = await File.ReadAllBytesAsync(destination, TestContext.Current.CancellationToken);

            Assert.True(bytes.AsSpan(0, 5).SequenceEqual("%PDF-"u8));
            Assert.Contains(result.Warnings, warning => warning.Contains("missing.png", StringComparison.Ordinal));
            Assert.Equal("saved source", await File.ReadAllTextAsync(source, TestContext.Current.CancellationToken));
            await Assert.ThrowsAsync<IOException>(() => service.WriteAsync(snapshot, destination, false, [source], Path.Combine(root, "state"), TestContext.Current.CancellationToken));
            await Assert.ThrowsAsync<IOException>(() => service.WriteAsync(snapshot, source, true, [source], Path.Combine(root, "state"), TestContext.Current.CancellationToken));
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.WriteAsync(snapshot, destination, true, [source], Path.Combine(root, "state"), cancelled.Token));
            Assert.Equal(bytes, await File.ReadAllBytesAsync(destination, TestContext.Current.CancellationToken));
            Assert.Empty(Directory.EnumerateFiles(root, "*.tmp"));
        }
        finally { if (evidence is null) Directory.Delete(root, true); }
    }
}
