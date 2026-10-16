using Axiom.Core;

namespace Axiom.Infrastructure;

public sealed record BatchExportItem(string RelativeOutput, ExportSnapshot Snapshot);
public sealed record BatchExportReview(string Destination, IReadOnlyList<BatchExportItem> Items);

public sealed class WorkspaceBatchExport(DocumentExportService exporter, string stateRoot)
{
    public BatchExportReview Review(string workspaceRoot, IEnumerable<ExportSnapshot> snapshots, string destination, string extension)
    {
        if (extension is not ("html" or "docx" or "txt" or "pdf" or "epub" or "odt")) throw new ArgumentException("Choose a supported export format.");
        destination = Path.GetFullPath(destination);
        ReviewedWorkspaceMove.ValidateSource(destination);
        if (File.Exists(destination) || Directory.Exists(destination) || !Directory.Exists(Path.GetDirectoryName(destination))) throw new IOException("Choose a new output folder inside an existing parent. Existing output is never merged or overwritten.");
        if (WorkspacePathBoundary.Contains(stateRoot, destination, true)) throw new IOException("Export cannot write inside app state.");
        var outputs = new HashSet<string>(WorkspacePathBoundary.Comparer);
        var items = new List<BatchExportItem>(); long characters = 0;
        foreach (var snapshot in snapshots.DistinctBy(snapshot => snapshot.SourcePath, WorkspacePathBoundary.Comparer))
        {
            if (snapshot.SourcePath is not { } source || !WorkspacePathBoundary.Contains(workspaceRoot, source)) throw new IOException("Bulk export accepts selected workspace Markdown only.");
            var relative = Path.ChangeExtension(Path.GetRelativePath(workspaceRoot, source), "." + extension);
            WorkspaceMutationPolicy.Resolve(destination, relative, false);
            if (!outputs.Add(relative)) throw new IOException("Two documents map to the same export path. Select them in separate batches.");
            characters += snapshot.Markdown.Length;
            if (snapshot.Markdown.Length > 4 * 1024 * 1024 || characters > 16 * 1024 * 1024 || items.Count >= 1000) throw new IOException("Bulk export supports 1,000 documents, 4 million characters each and 16 million characters total.");
            items.Add(new(relative, snapshot));
        }
        if (items.Count == 0) throw new IOException("No Markdown documents selected.");
        return new(destination, items);
    }

    public async Task<IReadOnlyList<string>> ApplyAsync(BatchExportReview review, IProgress<string>? progress = null, CancellationToken token = default, PdfPageSize pageSize = PdfPageSize.Letter)
    {
        var staging = Path.Combine(Path.GetDirectoryName(review.Destination)!, ".axiom-export-" + Guid.NewGuid().ToString("N"));
        var warnings = new List<string>();
        ReviewedWorkspaceMove.ValidateSource(review.Destination);
        if (Directory.Exists(review.Destination) || File.Exists(review.Destination)) throw new IOException("Output folder appeared after review. Nothing was exported.");
        Directory.CreateDirectory(staging);
        var published = false;
        try
        {
            var index = 0;
            foreach (var item in review.Items)
            {
                token.ThrowIfCancellationRequested();
                var path = WorkspaceMutationPolicy.Resolve(staging, item.RelativeOutput, false);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                progress?.Report($"Exporting {++index}/{review.Items.Count}: {item.RelativeOutput}");
                var result = await exporter.WriteAsync(item.Snapshot, path, false, review.Items.Select(item => item.Snapshot.SourcePath!).ToArray(), stateRoot, token, pageSize);
                warnings.AddRange(result.Warnings.Select(warning => item.RelativeOutput + ": " + warning));
            }
            token.ThrowIfCancellationRequested();
            ReviewedWorkspaceMove.ValidateSource(review.Destination);
            Directory.Move(staging, review.Destination); published = true;
            return warnings;
        }
        finally { if (!published && Directory.Exists(staging)) Directory.Delete(staging, true); }
    }
}
