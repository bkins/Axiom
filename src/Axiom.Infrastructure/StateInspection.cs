namespace Axiom.Infrastructure;

public sealed record StateInspection(string Integrity, int SchemaVersion, long Workspaces, long Drafts, long Identities, long PendingOperations);
