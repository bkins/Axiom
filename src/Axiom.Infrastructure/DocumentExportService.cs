using Axiom.Core;

namespace Axiom.Infrastructure;

public sealed class DocumentExportService
{
    private readonly IPdfExportRenderer? _pdfRenderer;

    public DocumentExportService(IPdfExportRenderer? pdfRenderer = null)
    {
        _pdfRenderer = pdfRenderer;
    }

    public async Task<ExportResult> WriteAsync(ExportSnapshot snapshot
                                            , string destination
                                            , bool replace
                                            , IEnumerable<string> protectedPaths
                                            , string stateRoot
                                            , CancellationToken cancellationToken = default
                                            , PdfPageSize pdfPageSize = PdfPageSize.Letter)
    {
        var path = Path.GetFullPath(destination);
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is not ".html" and not ".docx" and not ".txt" and not ".pdf") throw new IOException("Choose a .html, .docx, .txt or .pdf output file.");
        if (extension == ".pdf" && _pdfRenderer is null) throw new IOException("PDF export requires the native document renderer. Run export from Axiom.");
        if (!Enum.IsDefined(pdfPageSize)) throw new ArgumentOutOfRangeException(nameof(pdfPageSize));
        if (snapshot.Markdown.Length > 4 * 1024 * 1024) throw new IOException("Export supports documents up to 4 million characters. Split this document before exporting.");
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (protectedPaths.Append(snapshot.SourcePath ?? string.Empty).Where(value => !string.IsNullOrEmpty(value)).Any(value => Path.GetFullPath(value).Equals(path, comparison)))
            throw new IOException("Export cannot replace an open or source document.");
        var root = Path.GetFullPath(stateRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (path.StartsWith(root, comparison)) throw new IOException("Choose an output location outside Axiom application state.");
        if (File.Exists(path) && !replace) throw new IOException("Confirm replacement of the existing output first.");
        return await Task.Run(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var document = new MarkdownExportService().Parse(snapshot);
            var warnings = document.Warnings.ToList();
            byte[] bytes;
            if (extension == ".pdf")
            {
                var html = System.Text.Encoding.UTF8.GetString(HtmlExportWriter.Write(document, snapshot, warnings, pdfPageSize));
                var rendered = await _pdfRenderer!.RenderAsync(html, pdfPageSize, cancellationToken);
                bytes = rendered.Bytes;
                warnings.AddRange(rendered.Warnings);
                if (bytes.Length < 8 || !bytes.AsSpan(0, 5).SequenceEqual("%PDF-"u8)) throw new IOException("PDF renderer returned invalid output; the destination was preserved.");
            }
            else bytes = extension switch
            {
                ".html" => HtmlExportWriter.Write(document, snapshot, warnings)
              , ".txt" => TextExportWriter.Write(document, warnings)
              , _ => WordExportWriter.Write(document, snapshot, warnings)
            };
            if (bytes.Length > 128 * 1024 * 1024) throw new IOException("Export output exceeds 128 MiB. Reduce document size or images.");
            cancellationToken.ThrowIfCancellationRequested();
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.WriteThrough))
                {
                    await stream.WriteAsync(bytes, cancellationToken);
                    stream.Flush(true);
                }
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(temporary, path, replace);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return new ExportResult(path, warnings.Distinct().ToArray());
        }, cancellationToken);
    }
}
