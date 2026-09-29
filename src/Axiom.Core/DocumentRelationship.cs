namespace Axiom.Core;

public sealed record DocumentRelationship(Guid RelationshipId, Guid SourceId, Guid TargetId, string Kind, string? Note, long Revision = 0);
