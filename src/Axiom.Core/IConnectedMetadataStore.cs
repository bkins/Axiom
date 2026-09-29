namespace Axiom.Core;

public interface IConnectedMetadataStore : IClassificationStore
{
    Task<IReadOnlyList<DocumentRelationship>> GetRelationshipsAsync(CancellationToken cancellationToken = default);
    Task ApplyConnectedMetadataAsync(IReadOnlyList<ClassificationChange> classifications, IReadOnlyList<RelationshipChange> relationships, CancellationToken cancellationToken = default);
}
