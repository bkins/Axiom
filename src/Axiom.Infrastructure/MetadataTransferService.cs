using System.Text.Json;
using Axiom.Core;

namespace Axiom.Infrastructure;

public static class MetadataTransferService
{
    public const int MaximumBytes = 16 * 1024 * 1024;
    public const int MaximumRecords = 10000;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static MetadataTransfer Export(ClassificationCatalog catalog) => new("axiom-document-metadata", 1,
        catalog.Documents.Where(document => document.IsEligible).OrderBy(document => document.DocumentId).Select(document =>
            new MetadataTransferRecord(document.DocumentId!.Value, document.Classification!.Tags, document.Classification.Type,
                document.Classification.Aliases, document.RelativePath.Replace('\\', '/'))).ToArray());

    public static async Task WriteAsync(string path, MetadataTransfer transfer, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Choose a .json file for metadata export.");
        Validate(transfer);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(transfer, Options), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static async Task<MetadataTransfer> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
        if (stream.Length > MaximumBytes) throw new InvalidDataException("Metadata file exceeds the 16 MiB limit.");
        var transfer = await JsonSerializer.DeserializeAsync<MetadataTransfer>(stream, cancellationToken: cancellationToken)
            ?? throw new InvalidDataException("The transfer file is empty.");
        Validate(transfer);
        return transfer;
    }

    public static void Validate(MetadataTransfer transfer)
    {
        if (transfer.Format != "axiom-document-metadata" || transfer.SchemaVersion != 1 || transfer.Records is null)
            throw new InvalidDataException("Unsupported metadata format or version.");
        if (transfer.Records.Length > MaximumRecords) throw new InvalidDataException("Too many metadata records.");
        if (transfer.Records.Select(record => record.DocumentId).Distinct().Count() != transfer.Records.Length)
            throw new InvalidDataException("Duplicate document IDs in transfer.");
        foreach (var record in transfer.Records)
        {
            if (record.Tags is null || record.Aliases is null) throw new InvalidDataException("Tags and aliases are required arrays.");
            ClassificationPolicy.Normalize(new(record.DocumentId, record.Tags, record.Type, record.Aliases));
            if (record.RelativePath is { } hint && (Path.IsPathRooted(hint) || hint.Contains(':') || hint.Contains('\\') || hint.Split('/').Contains("..")))
                throw new InvalidDataException("Path hints must be workspace-relative.");
        }
    }

    public static IReadOnlyList<MetadataImportProposal> Preview(MetadataTransfer transfer, ClassificationCatalog catalog)
    {
        Validate(transfer);
        return transfer.Records.Select(record =>
        {
            var document = catalog.Find(record.DocumentId);
            if (document is null) return new MetadataImportProposal(record.DocumentId, $"{record.DocumentId:D} · Missing, ambiguous, or unavailable identity; skip", null);
            var value = ClassificationPolicy.Normalize(new(record.DocumentId, record.Tags, record.Type, record.Aliases));
            if (ClassificationPolicy.Equivalent(value, document.Classification!))
                return new MetadataImportProposal(record.DocumentId, $"{document.RelativePath} · Identical; no change", null);
            return new MetadataImportProposal(record.DocumentId,
                $"{document.RelativePath}\nCurrent tags: {string.Join(", ", document.Classification!.Tags)}; type: {document.Classification.Type ?? "none"}; aliases: {string.Join(", ", document.Classification.Aliases)}\nReplace with tags: {string.Join(", ", value.Tags)}; type: {value.Type ?? "none"}; aliases: {string.Join(", ", value.Aliases)}",
                new ClassificationChange(value, document.Classification!.Revision));
        }).ToArray();
    }
}
