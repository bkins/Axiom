namespace Axiom.Core;

public sealed class RevisionRestoreService
{
    public string Prepare(string current, string historical)
    {
        var currentIdentity = DocumentIdCodec.Find(current);
        var historicalIdentity = DocumentIdCodec.Find(historical);
        if (currentIdentity == historicalIdentity) return historical;
        var body = DocumentIdCodec.RemoveCanonicalMarkerForPreview(historical);
        if (currentIdentity is null) return body;
        var newline = current.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        return $"<!-- axiom:documentId={currentIdentity:D} -->{newline}{body}";
    }
}
