namespace Axiom.Core;

public sealed record MetadataTransferRelationship(Guid SourceId, Guid TargetId, string Kind, string? Note);
