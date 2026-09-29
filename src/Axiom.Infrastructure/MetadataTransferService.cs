using System.Text.Json;
using Axiom.Core;

namespace Axiom.Infrastructure;

public static class MetadataTransferService
{
    public const int MaximumBytes = 16 * 1024 * 1024;
    public const int MaximumRecords = 10000;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static MetadataTransfer Export(ClassificationCatalog catalog, IReadOnlyList<DocumentRelationship>? relationships = null) => new("axiom-document-metadata", relationships is null ? 1 : 2,
        catalog.Documents.Where(document => document.IsEligible).OrderBy(document => document.DocumentId).Select(document =>
            new MetadataTransferRecord(document.DocumentId!.Value, document.Classification!.Tags, document.Classification.Type,
                document.Classification.Aliases, document.RelativePath.Replace('\\', '/'))).ToArray(),
        relationships?.Where(relationship => catalog.Find(relationship.SourceId) is not null || catalog.Find(relationship.TargetId) is not null)
            .OrderBy(RelationshipPolicy.Key, StringComparer.Ordinal).Select(relationship => new MetadataTransferRelationship(relationship.SourceId, relationship.TargetId, relationship.Kind, relationship.Note)).ToArray());

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
        if (transfer.Format != "axiom-document-metadata" || transfer.SchemaVersion is not 1 and not 2 || transfer.Records is null)
            throw new InvalidDataException("Unsupported metadata format or version.");
        if (transfer.Records.Length + (transfer.Relationships?.Length ?? 0) > MaximumRecords) throw new InvalidDataException("Too many metadata records.");
        if (transfer.SchemaVersion == 1 && transfer.Relationships is { Length: > 0 }) throw new InvalidDataException("Version 1 cannot contain relationships.");
        if (transfer.Records.Select(record => record.DocumentId).Distinct().Count() != transfer.Records.Length)
            throw new InvalidDataException("Duplicate document IDs in transfer.");
        foreach (var record in transfer.Records)
        {
            if (record.Tags is null || record.Aliases is null) throw new InvalidDataException("Tags and aliases are required arrays.");
            ClassificationPolicy.Normalize(new(record.DocumentId, record.Tags, record.Type, record.Aliases));
            if (record.RelativePath is { } hint && (Path.IsPathRooted(hint) || hint.Contains(':') || hint.Contains('\\') || hint.Split('/').Contains("..")))
                throw new InvalidDataException("Path hints must be workspace-relative.");
        }
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var relation in transfer.Relationships ?? [])
        {
            var normalized = RelationshipPolicy.Normalize(new(relation.SourceId, relation.SourceId, relation.TargetId, relation.Kind, relation.Note));
            if (!keys.Add(RelationshipPolicy.Key(normalized))) throw new InvalidDataException("Duplicate directed relationships in transfer.");
        }
    }

    public static IReadOnlyList<MetadataImportProposal> Preview(MetadataTransfer transfer, ClassificationCatalog catalog, IReadOnlyList<DocumentRelationship>? relationships = null)
    {
        Validate(transfer);
        var proposals = transfer.Records.Select(record =>
        {
            var document = catalog.Find(record.DocumentId);
            if (document is null) return new MetadataImportProposal(record.DocumentId, $"{record.DocumentId:D} · Missing, ambiguous, or unavailable identity; skip", null);
            var value = ClassificationPolicy.Normalize(new(record.DocumentId, record.Tags, record.Type, record.Aliases));
            if (ClassificationPolicy.Equivalent(value, document.Classification!))
                return new MetadataImportProposal(record.DocumentId, $"{document.RelativePath} · Identical; no change", null);
            return new MetadataImportProposal(record.DocumentId,
                $"{document.RelativePath}\nCurrent tags: {string.Join(", ", document.Classification!.Tags)}; type: {document.Classification.Type ?? "none"}; aliases: {string.Join(", ", document.Classification.Aliases)}\nReplace with tags: {string.Join(", ", value.Tags)}; type: {value.Type ?? "none"}; aliases: {string.Join(", ", value.Aliases)}",
                new ClassificationChange(value, document.Classification!.Revision));
        }).ToList();
        var existing = (relationships ?? []).ToDictionary(RelationshipPolicy.Key, StringComparer.Ordinal);
        foreach (var relation in transfer.Relationships ?? [])
        {
            var value = RelationshipPolicy.Normalize(new(Guid.NewGuid(), relation.SourceId, relation.TargetId, relation.Kind, relation.Note));
            var source = catalog.Find(value.SourceId);
            var target = catalog.Find(value.TargetId);
            if (source is null || target is null)
            {
                proposals.Add(new(value.RelationshipId, $"{value.SourceId:D} → {value.TargetId:D} · Unresolved or ambiguous endpoint; skip", null));
                continue;
            }
            var old = existing.GetValueOrDefault(RelationshipPolicy.Key(value));
            if (old is not null) value = value with { RelationshipId = old.RelationshipId };
            if (old is not null && old.Kind == value.Kind && old.Note == value.Note)
            {
                proposals.Add(new(value.RelationshipId, $"{source.RelativePath} → {target.RelativePath} · Identical relationship; no change", null));
                continue;
            }
            proposals.Add(new(value.RelationshipId, $"{source.RelativePath} → {target.RelativePath}\nKind: {value.Kind}; current note: {old?.Note ?? "none"}; proposed note: {value.Note ?? "none"}", null,
                new(value, old?.Revision ?? 0)));
        }
        return proposals;
    }
}
