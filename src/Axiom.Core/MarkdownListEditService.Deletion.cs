using System.Globalization;
using System.Text;

namespace Axiom.Core;

public sealed partial class MarkdownListEditService
{
    public MarkdownEditResult DeleteItems(string text, int selectionStart, int selectionLength)
    {
        ValidateSelection(text, selectionStart, selectionLength);
        var end = selectionStart + selectionLength;
        if (selectionLength == 0 || !IsLineStart(text, selectionStart) || end != text.Length && !IsLineStart(text, end) ||
            TouchesCanonicalIdentity(text, selectionStart, selectionLength)) return Unchanged(text, selectionStart, selectionLength);
        var firstEnd = FindLineEnd(text, selectionStart);
        var first = ListItemRegex().Match(text[selectionStart..firstEnd]);
        if (!first.Success || !first.Groups["ordered"].Success ||
            !long.TryParse(first.Groups["number"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var next))
            return Unchanged(text, selectionStart, selectionLength);
        var structure = new MarkdownStructureService().Parse(text);
        if (structure.Nodes.Any(node => node.Kind == DocumentStructureNodeKind.Code &&
            selectionStart < node.Source.Offset + node.Source.Length && end > node.Source.Offset))
            return Unchanged(text, selectionStart, selectionLength);
        var indent = first.Groups["indent"].Value;
        var delimiter = first.Groups["delimiter"].Value;
        // Only whole items (plus their indented continuation) can trigger automatic renumbering.
        foreach (var line in LineSplitRegex().Split(text[selectionStart..end]))
            if (!string.IsNullOrWhiteSpace(line) && !ListItemRegex().IsMatch(line) &&
                (!line.StartsWith(indent + " ", StringComparison.Ordinal) && !line.StartsWith(indent + "\t", StringComparison.Ordinal)))
                return Unchanged(text, selectionStart, selectionLength);
        var suffix = new StringBuilder();
        var position = end;
        while (position < text.Length)
        {
            var lineEnd = FindLineEnd(text, position);
            var afterLine = lineEnd;
            if (afterLine < text.Length && text[afterLine] == '\r') afterLine++;
            if (afterLine < text.Length && text[afterLine] == '\n') afterLine++;
            var line = text[position..lineEnd];
            var item = ListItemRegex().Match(line);
            if (!string.IsNullOrWhiteSpace(line))
            {
                if (item.Success && item.Groups["indent"].Value == indent && item.Groups["delimiter"].Value == delimiter)
                {
                    var number = item.Groups["number"];
                    suffix.Append(line[..number.Index]).Append(next.ToString(CultureInfo.InvariantCulture)).Append(line[(number.Index + number.Length)..]);
                    if (next == long.MaxValue) return Unchanged(text, selectionStart, selectionLength);
                    next++;
                }
                else if (line.StartsWith(indent + " ", StringComparison.Ordinal) || line.StartsWith(indent + "\t", StringComparison.Ordinal)) suffix.Append(line);
                else break;
            }
            else suffix.Append(line);
            suffix.Append(text[lineEnd..afterLine]);
            position = afterLine;
        }
        suffix.Append(text[position..]);
        return new(text[..selectionStart] + suffix, selectionStart, 0, true);
    }
}
