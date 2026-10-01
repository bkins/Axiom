namespace Axiom.Core;

public interface ISemanticDocumentProvider
{
    string ProviderId { get; }
    string ProviderVersion { get; }
    Task<SemanticDocumentResult> AnalyzeAsync(SemanticDocumentSnapshot snapshot, CancellationToken cancellationToken = default);
}
