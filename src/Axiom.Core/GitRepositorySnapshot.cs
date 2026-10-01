namespace Axiom.Core;

public sealed record GitRepositorySnapshot(string WorkspaceRoot
                                        , string RepositoryRoot
                                        , string BranchReference
                                        , string? Head
                                        , string IndexPath
                                        , string IndexHash
                                        , IReadOnlyList<GitChangedFile> Files
                                        , bool CanMutate
                                        , string? Reason);
