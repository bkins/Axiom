namespace Axiom.Core;

public interface IClassificationStore
{
    Task<IReadOnlyList<DocumentClassification>> GetClassificationsAsync(CancellationToken cancellationToken = default);
    Task ApplyClassificationsAsync(IReadOnlyList<ClassificationChange> changes, CancellationToken cancellationToken = default);
}
