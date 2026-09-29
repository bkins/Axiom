namespace Axiom.Core;

public sealed record WorkspaceIndexedDocument(string RelativePath, string ContentHash, IReadOnlyList<WorkspaceLinkReference> References);
