namespace Axiom.Core;

public sealed record ClassificationCatalog(string Root, IReadOnlyList<ClassifiedDocument> Documents, IReadOnlyList<string> Errors)
{
    public ClassifiedDocument? Find(Guid id) => Documents.SingleOrDefault(document => document.DocumentId == id && document.IsEligible);
}
