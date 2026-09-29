using Axiom.Core;

namespace Axiom.App;

internal sealed record WorkspaceLoadSnapshot(string Root
                                          , MarkdownDiscoveryResult Discovery
                                          , IReadOnlyDictionary<Guid, IReadOnlyList<string>> Duplicates
                                          , IReadOnlyList<WorkspaceTreeNode> Tree
                                          , ClassificationCatalog? Classifications
                                          , IReadOnlyList<DocumentRelationship> Relationships);
