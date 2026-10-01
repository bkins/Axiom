using System.Globalization;

namespace Axiom.Core;

public static class DocumentTemplateService
{
    public static string Expand(string template, string title, DateOnly date)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        if (title.Contains('\r') || title.Contains('\n'))
            throw new ArgumentException("The document title must fit on one line.", nameof(title));
        // A template may itself be an enrolled document. Its identity belongs to
        // that source, never to a newly created document.
        if (DocumentIdentityPolicy.FindCanonical(template) is not null)
        {
            var newline = template.IndexOf('\n');
            template = newline < 0 ? "" : template[(newline + 1)..];
        }
        // Substitute in one pass so placeholder-looking title text stays literal.
        var output = new System.Text.StringBuilder(template.Length);
        for (var position = 0; position < template.Length;)
        {
            if (template.AsSpan(position).StartsWith("{{title}}", StringComparison.Ordinal))
            {
                output.Append(title);
                position += 9;
            }
            else if (template.AsSpan(position).StartsWith("{{date}}", StringComparison.Ordinal))
            {
                output.Append(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                position += 8;
            }
            else output.Append(template[position++]);
        }
        return output.ToString();
    }
}
