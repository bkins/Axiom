namespace Axiom.Core;

public sealed record WorkspaceBacklinkIndex(int FormatVersion
                                         , DateTimeOffset BuiltUtc
                                         , IReadOnlyList<WorkspaceIndexedDocument> Documents
                                         , IReadOnlyList<string> Errors);
