namespace Axiom.Core;

public sealed record MetadataImportProposal(Guid DocumentId, string Description, ClassificationChange? Change, RelationshipChange? RelationshipChange = null)
{
    public bool Replace { get; set; }
    public bool CanReplace => Change is not null || RelationshipChange is not null;
}
