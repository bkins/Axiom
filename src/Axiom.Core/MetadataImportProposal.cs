namespace Axiom.Core;

public sealed record MetadataImportProposal(Guid DocumentId, string Description, ClassificationChange? Change)
{
    public bool Replace { get; set; }
    public bool CanReplace => Change is not null;
}
