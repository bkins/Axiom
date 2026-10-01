namespace Axiom.Core;

public sealed record GitSelectedCommitReview(GitRepositorySnapshot Repository
                                         , IReadOnlyList<string> RelativePaths
                                         , IReadOnlyDictionary<string, string?> WorkingHashes
                                         , IReadOnlyDictionary<string, string?> IndexEntries);
