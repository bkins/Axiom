namespace Axiom.Core;

public sealed record RelationshipView(DocumentRelationship Relationship, string Direction, string Endpoint, string Status)
{
    public string Display => $"{Direction}: {Endpoint}\n{Relationship.Kind} · {Status}\n{Relationship.Note}";
}
