using System.Text;
using Axiom.Core;
using Axiom.Infrastructure;
using Xunit;

namespace Axiom.Infrastructure.Tests;

public sealed class PdfExportTests
{
    [Fact]
    public async Task PdfUsesSafeImmutableHtmlProjectionWithPageSettingsAndRepeatingTableHeaders()
    {
        using var fixture = new MutationTestWorkspace();
        var renderer = new CapturingRenderer();
        var source = Path.Combine(fixture.Root, "source.md");
        await File.WriteAllTextAsync(source, "saved source");
        var output = Path.Combine(fixture.Root, "out.pdf");
        var markdown = DocumentIdCodec.Enroll("# Café 一\n\nUnsaved **bold**\n\n| Header | Second |\n|---|---|\n| Cell | Row |\n\n[unsafe](javascript:alert) ![missing](missing.png)", Guid.NewGuid());
        var result = await new DocumentExportService(renderer).WriteAsync(new(markdown, source), output, false, [source], fixture.State.Paths.Root, pdfPageSize: PdfPageSize.A4);
        Assert.Contains("size:A4 portrait", renderer.Html);
        Assert.Contains("counter(page)", renderer.Html);
        Assert.Contains("<thead>", renderer.Html);
        Assert.Contains("<strong>bold</strong>", renderer.Html);
        Assert.Contains("Café 一", System.Net.WebUtility.HtmlDecode(renderer.Html));
        Assert.DoesNotContain("documentId", renderer.Html);
        Assert.DoesNotContain("href=\"javascript:", renderer.Html);
        Assert.Contains("missing", renderer.Html);
        Assert.Contains("default-src 'none'", renderer.Html);
        Assert.Equal("saved source", await File.ReadAllTextAsync(source));
        Assert.Equal(renderer.Bytes, await File.ReadAllBytesAsync(output));
        Assert.Equal(3, result.Warnings.Count);
    }

    [Fact]
    public async Task RendererFailureInvalidPdfAndCancellationPreserveExistingOutput()
    {
        using var fixture = new MutationTestWorkspace();
        var output = Path.Combine(fixture.Root, "out.pdf");
        await File.WriteAllTextAsync(output, "original");
        var renderer = new CapturingRenderer { Failure = new IOException("Renderer failed") };
        var service = new DocumentExportService(renderer);
        await Assert.ThrowsAsync<IOException>(() => service.WriteAsync(new("new", null), output, true, [], fixture.State.Paths.Root));
        renderer.Failure = null;
        renderer.Bytes = Encoding.UTF8.GetBytes("invalid output");
        await Assert.ThrowsAsync<IOException>(() => service.WriteAsync(new("new", null), output, true, [], fixture.State.Paths.Root));
        renderer.CancelAfterRendering = true;
        renderer.Bytes = Encoding.UTF8.GetBytes("%PDF-1.7\nfixture");
        using var cancellation = new CancellationTokenSource();
        renderer.Cancellation = cancellation;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.WriteAsync(new("new", null), output, true, [], fixture.State.Paths.Root, cancellation.Token));
        Assert.Equal("original", await File.ReadAllTextAsync(output));
        Assert.Empty(Directory.GetFiles(fixture.Root, "*.tmp"));
    }

    [Fact]
    public async Task SourceStateAndUnconfirmedOverwriteAreRejectedBeforeRendering()
    {
        using var fixture = new MutationTestWorkspace();
        var output = Path.Combine(fixture.Root, "out.pdf");
        await File.WriteAllTextAsync(output, "original");
        var renderer = new CapturingRenderer();
        var service = new DocumentExportService(renderer);
        await Assert.ThrowsAsync<IOException>(() => service.WriteAsync(new("new", output), output, true, [], fixture.State.Paths.Root));
        await Assert.ThrowsAsync<IOException>(() => service.WriteAsync(new("new", null), output, true, [output], fixture.State.Paths.Root));
        await Assert.ThrowsAsync<IOException>(() => service.WriteAsync(new("new", null), output, false, [], fixture.State.Paths.Root));
        await Assert.ThrowsAsync<IOException>(() => service.WriteAsync(new("new", null), Path.Combine(fixture.State.Paths.Root, "out.pdf"), true, [], fixture.State.Paths.Root));
        Assert.Null(renderer.Html);
    }

    private sealed class CapturingRenderer : IPdfExportRenderer
    {
        public string? Html { get; private set; }
        public byte[] Bytes { get; set; } = Encoding.UTF8.GetBytes("%PDF-1.7\nfixture");
        public Exception? Failure { get; set; }
        public bool CancelAfterRendering { get; set; }
        public CancellationTokenSource? Cancellation { get; set; }

        public Task<PdfRenderResult> RenderAsync(string html, PdfPageSize pageSize, CancellationToken cancellationToken)
        {
            Html = html;
            if (Failure is not null) throw Failure;
            if (CancelAfterRendering) Cancellation!.Cancel();
            return Task.FromResult(new PdfRenderResult(Bytes, ["Fixture renderer warning."]));
        }
    }
}
