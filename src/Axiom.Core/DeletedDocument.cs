namespace Axiom.Core;

public sealed record DeletedDocument(DocumentMutationIntent Intent
                                  , byte[] Content
                                  , DateTimeOffset CapturedUtc
                                  , string Status
                                  , DateTimeOffset? PinnedUtc
                                  , string MetadataSnapshot);
