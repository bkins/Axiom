using Axiom.Core;

namespace Axiom.Infrastructure;

public sealed class DocumentExportService
{
    public async Task<ExportResult> WriteAsync(ExportSnapshot snapshot
                                            , string destination
                                            , bool replace
                                            , IEnumerable<string> protectedPaths
                                            , string stateRoot
                                            , CancellationToken cancellationToken = default)
    {
        var path = Path.GetFullPath(destination);
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is not ".html" and not ".docx" and not ".txt") throw new IOException("Choose a .html, .docx or .txt output file.");
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
            var bytes = extension switch
            {
                ".html" => HtmlExportWriter.Write(document, snapshot, warnings)
              , ".txt" => TextExportWriter.Write(document, warnings)
              , _ => WordExportWriter.Write(document, snapshot, warnings)
            };
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
