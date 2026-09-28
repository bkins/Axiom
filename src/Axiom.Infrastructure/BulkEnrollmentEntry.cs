namespace Axiom.Infrastructure;

public sealed record BulkEnrollmentEntry(string RelativePath, string Status, string? ReviewedHash, Guid? Identity, Guid WorkspaceId)
{
    public bool IsEligible => Status == "Ordinary — ready to enroll";
    public override string ToString() => $"{RelativePath} — {Status}";
}
