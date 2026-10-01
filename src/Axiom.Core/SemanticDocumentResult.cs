namespace Axiom.Core;

public sealed record SemanticDocumentResult(string ProviderId
                                         , string ProviderVersion
                                         , string DocumentKey
                                         , string ContentHash
                                         , long Revision
                                         , DateTimeOffset CreatedUtc
                                         , string ResultJson)
{
    public bool AppliesTo(SemanticDocumentSnapshot snapshot, string providerId, string providerVersion) =>
        DocumentKey == snapshot.DocumentKey && ContentHash == snapshot.ContentHash && Revision == snapshot.Revision
        && ProviderId == providerId && ProviderVersion == providerVersion;
}
