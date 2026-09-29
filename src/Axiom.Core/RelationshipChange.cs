namespace Axiom.Core;

public sealed record RelationshipChange(DocumentRelationship Value, long ExpectedRevision, bool Delete = false);
