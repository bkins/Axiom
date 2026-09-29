using System.Text;

namespace Axiom.Core;

public static class RelationshipPolicy
{
    public static DocumentRelationship Normalize(DocumentRelationship value)
    {
        if (value.RelationshipId == Guid.Empty || value.SourceId == Guid.Empty || value.TargetId == Guid.Empty)
            throw new ArgumentException("Relationship and endpoint identities are required.");
        if (value.SourceId == value.TargetId) throw new ArgumentException("A relationship cannot point to the same document.");
        var kind = ClassificationPolicy.NormalizeValue(value.Kind) ?? throw new ArgumentException("A relationship kind is required.");
        var note = string.IsNullOrWhiteSpace(value.Note) ? null : value.Note.Trim().Normalize(NormalizationForm.FormC);
        if (note is not null && (note.Length > 2000 || note.Any(character => char.IsControl(character) && character is not '\n' and not '\r' and not '\t')))
            throw new ArgumentException("Notes support at most 2,000 characters and no unsupported control characters.");
        return value with { Kind = kind, Note = note };
    }

    public static string Key(DocumentRelationship value) => $"{value.SourceId:D}/{value.TargetId:D}/{value.Kind.ToUpperInvariant()}";
}
