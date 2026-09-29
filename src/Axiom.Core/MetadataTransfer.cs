namespace Axiom.Core;

public sealed record MetadataTransfer(string Format, int SchemaVersion, MetadataTransferRecord[] Records, MetadataTransferRelationship[]? Relationships = null);
