namespace Axiom.Core;

public interface ISemanticDocumentResultStore
{
    Task<SemanticDocumentResult?> ReadAsync(string documentKey, string providerId, string providerVersion, string contentHash, long revision, CancellationToken cancellationToken = default);
    Task WriteAsync(SemanticDocumentResult result, CancellationToken cancellationToken = default);
}
