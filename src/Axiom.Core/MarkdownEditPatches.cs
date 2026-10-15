using System.Text.Json;
using System.Text.RegularExpressions;

namespace Axiom.Core;

/// <summary>Builds a preview from bounded, unique replacements against the original snapshot.</summary>
public static class MarkdownEditPatches
{
    public static string Parse(string source, string response)
    {
        if (response.Length > 40_000) throw new InvalidOperationException("The edit response is too large. Source is unchanged.");
        var text = response.Trim();
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var fence = Regex.Match(text, @"\A```(?:json)?[ \t]*\r?\n(?<json>[\s\S]*?)\r?\n```\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
            if (!fence.Success) throw new InvalidOperationException("CP returned an incomplete edit response. Source is unchanged; retry a smaller request.");
            text = fence.Groups["json"].Value;
        }
        try
        {
            using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 8 });
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException();
            var edits = document.RootElement.GetProperty("edits");
            if (edits.ValueKind != JsonValueKind.Array || edits.GetArrayLength() > 8) throw new JsonException();
            var replacements = new List<(int Offset, string Old, string New)>();
            foreach (var edit in edits.EnumerateArray())
            {
                if (edit.ValueKind != JsonValueKind.Object || edit.GetProperty("oldText").ValueKind != JsonValueKind.String || edit.GetProperty("newText").ValueKind != JsonValueKind.String) throw new JsonException();
                var old = edit.GetProperty("oldText").GetString();
                var revised = edit.GetProperty("newText").GetString();
                if (string.IsNullOrEmpty(old) || old.Length > 2000 || revised is null || revised.Length > 4000) throw new JsonException();
                var offset = source.IndexOf(old, StringComparison.Ordinal);
                if (offset < 0 || source.IndexOf(old, offset + 1, StringComparison.Ordinal) >= 0)
                    throw new InvalidOperationException("A proposed edit does not uniquely match the document. Source is unchanged; retry a more specific request.");
                if (replacements.Any(change => offset < change.Offset + change.Old.Length && change.Offset < offset + old.Length))
                    throw new InvalidOperationException("Proposed edits overlap. Source is unchanged; retry a smaller request.");
                replacements.Add((offset, old, revised));
            }
            var proposal = source;
            foreach (var change in replacements.OrderByDescending(change => change.Offset))
                proposal = proposal.Remove(change.Offset, change.Old.Length).Insert(change.Offset, change.New);
            if (proposal.Length > 200_000 || string.IsNullOrWhiteSpace(proposal) || DocumentIdentityPolicy.FindCanonical(source) != DocumentIdentityPolicy.FindCanonical(proposal))
                throw new InvalidOperationException("The proposal changes the document identity or exceeds document limits. Source is unchanged.");
            return proposal;
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException)
        {
            throw new InvalidOperationException("CP returned an invalid edit response. Source is unchanged; retry a smaller request.", error);
        }
    }
}
