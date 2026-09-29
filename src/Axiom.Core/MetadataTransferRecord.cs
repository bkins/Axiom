namespace Axiom.Core;

public sealed record MetadataTransferRecord(Guid DocumentId, string[] Tags, string? Type, string[] Aliases, string? RelativePath = null);
