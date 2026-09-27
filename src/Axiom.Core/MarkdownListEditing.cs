using System.Globalization;
using System.Text.RegularExpressions;

namespace Axiom.Core;

public enum MarkdownListEditKind
{
    Continue,
    Indent,
    Outdent
}

public sealed partial class MarkdownListEditService
{
    private const string IndentUnit = "  ";

    public MarkdownEditResult Apply(
        string text,
        int selectionStart,
        int selectionLength,
        MarkdownListEditKind kind)
    {
        ArgumentNullException.ThrowIfNull(text);
        ValidateSelection(text, selectionStart, selectionLength);

        if (TouchesCanonicalIdentity(text, selectionStart, selectionLength))
            return Unchanged(text, selectionStart, selectionLength);

        return kind switch
        {
            MarkdownListEditKind.Continue => ContinueList(text, selectionStart, selectionLength),
            MarkdownListEditKind.Indent => ChangeIndent(text, selectionStart, selectionLength, outdent: false),
            MarkdownListEditKind.Outdent => ChangeIndent(text, selectionStart, selectionLength, outdent: true),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown list editing action.")
        };
    }

    private static MarkdownEditResult ContinueList(string text, int start, int length)
    {
        if (length != 0) return Unchanged(text, start, length);

        var lineStart = FindLineStart(text, start);
        var lineEnd = FindLineEnd(text, start);
        if (start != lineEnd) return Unchanged(text, start, length);

        var line = text[lineStart..lineEnd];
        var match = ListItemRegex().Match(line);
        if (!match.Success) return Unchanged(text, start, length);

        var indent = match.Groups["indent"].Value;
        var content = match.Groups["content"].Value;
        if (string.IsNullOrWhiteSpace(content))
            return Replace(text, lineStart, line.Length, indent, lineStart + indent.Length, 0);

        var marker = GetContinuationMarker(match);
        if (marker is null) return Unchanged(text, start, length);

        var inserted = DetectNewline(text) + indent + marker;
        return Replace(text, start, 0, inserted, start + inserted.Length, 0);
    }

    private static string? GetContinuationMarker(Match match)
    {
        if (match.Groups["task"].Success)
            return match.Groups["bullet"].Value + match.Groups["bulletSpace"].Value + "[ ]" + match.Groups["taskSpace"].Value;

        if (match.Groups["ordered"].Success)
        {
            if (!long.TryParse(match.Groups["number"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ||
                number == long.MaxValue)
                return null;
            return (number + 1).ToString(CultureInfo.InvariantCulture) +
                match.Groups["delimiter"].Value +
                match.Groups["orderedSpace"].Value;
        }

        return match.Groups["unordered"].Success
            ? match.Groups["unorderedBullet"].Value + match.Groups["unorderedSpace"].Value
            : null;
    }

    private static MarkdownEditResult ChangeIndent(string text, int start, int length, bool outdent)
    {
        var blockStart = FindLineStart(text, start);
        var selectionEnd = start + length;
        var probe = length > 0 && selectionEnd > blockStart && IsLineStart(text, selectionEnd)
            ? selectionEnd - 1
            : selectionEnd;
        var blockEnd = FindLineEnd(text, probe);
        var block = text[blockStart..blockEnd];
        var newline = DetectNewline(block.Length == 0 ? text : block);
        var lines = LineSplitRegex().Split(block);

        if (!ContainsOnlyListContent(lines)) return Unchanged(text, start, length);

        var changed = false;
        for (var index = 0; index < lines.Length; index++)
        {
            var original = lines[index];
            var updated = outdent ? RemoveIndentUnit(original) : IndentUnit + original;
            changed |= !updated.Equals(original, StringComparison.Ordinal);
            lines[index] = updated;
        }

        if (!changed) return Unchanged(text, start, length);

        var replacement = string.Join(newline, lines);
        if (length > 0)
            return Replace(text, blockStart, block.Length, replacement, blockStart, replacement.Length);

        var removed = outdent ? block.Length - replacement.Length : 0;
        var caret = outdent
            ? Math.Max(blockStart, start - removed)
            : start + IndentUnit.Length;
        return Replace(text, blockStart, block.Length, replacement, caret, 0);
    }

    private static bool ContainsOnlyListContent(IReadOnlyList<string> lines)
    {
        var hasListItem = false;
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (!ListItemRegex().IsMatch(line)) return false;
            hasListItem = true;
        }
        return hasListItem;
    }

    private static string RemoveIndentUnit(string line)
    {
        if (line.StartsWith('\t')) return line[1..];
        var spaces = 0;
        while (spaces < IndentUnit.Length && spaces < line.Length && line[spaces] == ' ') spaces++;
        return line[spaces..];
    }

    private static bool TouchesCanonicalIdentity(string text, int start, int length)
    {
        if (DocumentIdCodec.Find(text) is null) return false;
        var newlineIndex = text.IndexOf('\n');
        var protectedLength = newlineIndex < 0 ? text.Length : newlineIndex + 1;
        return length == 0 ? start < protectedLength : start < protectedLength && start + length > 0;
    }

    private static int FindLineStart(string text, int offset)
    {
        if (offset == 0) return 0;
        var index = text.LastIndexOf('\n', Math.Min(offset - 1, text.Length - 1));
        return index < 0 ? 0 : index + 1;
    }

    private static int FindLineEnd(string text, int offset)
    {
        var index = text.IndexOf('\n', Math.Min(offset, text.Length));
        if (index < 0) return text.Length;
        return index > 0 && text[index - 1] == '\r' ? index - 1 : index;
    }

    private static bool IsLineStart(string text, int offset) => offset == 0 || offset <= text.Length && text[offset - 1] == '\n';

    private static string DetectNewline(string text) => text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

    private static MarkdownEditResult Replace(
        string text,
        int start,
        int length,
        string replacement,
        int selectionStart,
        int selectionLength)
    {
        var updated = text[..start] + replacement + text[(start + length)..];
        return new MarkdownEditResult(updated, selectionStart, selectionLength, !updated.Equals(text, StringComparison.Ordinal));
    }

    private static MarkdownEditResult Unchanged(string text, int start, int length) => new(text, start, length, false);

    private static void ValidateSelection(string text, int start, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        if (start > text.Length || length > text.Length - start)
            throw new ArgumentOutOfRangeException(nameof(length), "The selection must be inside the source text.");
    }

    [GeneratedRegex(@"^(?<indent>[ \t]*)(?:(?<task>(?<bullet>[-+*])(?<bulletSpace>[ \t]+)\[[ xX]\](?<taskSpace>[ \t]+))|(?<ordered>(?<number>\d+)(?<delimiter>[.)])(?<orderedSpace>[ \t]+))|(?<unordered>(?<unorderedBullet>[-+*])(?<unorderedSpace>[ \t]+)))(?<content>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex ListItemRegex();

    [GeneratedRegex("\\r\\n|\\n", RegexOptions.CultureInvariant)]
    private static partial Regex LineSplitRegex();
}
