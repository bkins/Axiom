namespace Axiom.Core;

public sealed record WorkspaceDocumentSession(int FormatVersion
                                           , string? ActiveRelativePath
                                           , IReadOnlyList<WorkspaceDocumentLocation> Documents);
