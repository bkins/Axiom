using System.Text;

namespace Axiom.Core;

public static class ClassificationPolicy
{
    public static DocumentClassification Normalize(DocumentClassification value)
    {
        if (value.DocumentId == Guid.Empty) throw new ArgumentException("A valid document ID is required.");
        return value with { Tags = NormalizeSet(value.Tags), Type = NormalizeValue(value.Type), Aliases = NormalizeSet(value.Aliases) };
    }

    public static string[] NormalizeSet(IEnumerable<string> values)
    {
        var normalized = values.Select(NormalizeValue).Where(value => value is not null).Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        if (normalized.Length > 100) throw new ArgumentException("At most 100 tags or aliases are supported.");
        return normalized;
    }

    public static string? NormalizeValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim().Normalize(NormalizationForm.FormC);
        if (value.Length > 200 || value.Any(char.IsControl)) throw new ArgumentException("Values must have at most 200 characters and no control characters.");
        return value;
    }

    public static bool Equivalent(DocumentClassification left, DocumentClassification right) =>
        left.DocumentId == right.DocumentId && string.Equals(left.Type, right.Type, StringComparison.Ordinal) &&
        left.Tags.SequenceEqual(right.Tags) && left.Aliases.SequenceEqual(right.Aliases);

    public static bool Matches(string relativePath, DocumentClassification value, IEnumerable<string> tags, string? type, string? text) =>
        tags.All(tag => value.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase)) &&
        (string.IsNullOrWhiteSpace(type) || string.Equals(value.Type, type.Trim(), StringComparison.OrdinalIgnoreCase)) &&
        (string.IsNullOrWhiteSpace(text) || relativePath.Contains(text, StringComparison.OrdinalIgnoreCase) ||
            value.Aliases.Any(alias => alias.Contains(text, StringComparison.OrdinalIgnoreCase)));
}
