namespace Axiom.Core;

public sealed record DocumentRevision(string CommitHash, DateTimeOffset CommittedUtc, string Subject, string Path);
