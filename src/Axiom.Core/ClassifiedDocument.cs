namespace Axiom.Core;

public sealed record ClassifiedDocument(string RelativePath, Guid? DocumentId, string ContentHash, string Status, DocumentClassification? Classification)
{
    public bool IsEligible => DocumentId is not null && Status == "Unique saved identity";
    public string Display => $"{RelativePath} · {Status}";
}
