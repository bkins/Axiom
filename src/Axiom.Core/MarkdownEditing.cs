using System.Text.RegularExpressions;

namespace Axiom.Core;

public enum MarkdownEditKind
{
    Bold,
    Italic,
    InlineCode,
    Link,
    Paragraph,
    Heading1,
    Heading2,
    Heading3,
    UnorderedList,
    OrderedList,
    TaskList,
    FencedCode,
    HardBreak,
    Table,
    HorizontalRule,
    Callout
}

public sealed record MarkdownEditResult(
    string Text,
    int SelectionStart,
    int SelectionLength,
    bool Changed);

public sealed partial class MarkdownEditService
{
    public MarkdownEditResult Apply(
        string text,
        int selectionStart,
        int selectionLength,
        MarkdownEditKind kind)
    {
        ArgumentNullException.ThrowIfNull(text);
        ValidateSelection(text, selectionStart, selectionLength);

        if (TouchesCanonicalIdentity(text, selectionStart, selectionLength))
            return Unchanged(text, selectionStart, selectionLength);

        return kind switch
        {
            MarkdownEditKind.Bold => ApplyInline(text, selectionStart, selectionLength, "**", "bold text"),
            MarkdownEditKind.Italic => ApplyInline(text, selectionStart, selectionLength, "*", "italic text"),
            MarkdownEditKind.InlineCode => ApplyInline(text, selectionStart, selectionLength, "`", "code"),
            MarkdownEditKind.Link => ApplyLink(text, selectionStart, selectionLength),
            MarkdownEditKind.FencedCode => ApplyFence(text, selectionStart, selectionLength),
            MarkdownEditKind.HardBreak => ApplyHardBreak(text, selectionStart, selectionLength),
            MarkdownEditKind.Table or MarkdownEditKind.HorizontalRule or MarkdownEditKind.Callout => ApplyTemplate(text, selectionStart, selectionLength, kind),
            _ => ApplyBlock(text, selectionStart, selectionLength, kind)
        };
    }

    private static MarkdownEditResult ApplyInline(
        string text,
        int start,
        int length,
        string delimiter,
        string placeholder)
    {
        if (length > 0)
        {
            var selected = text.Substring(start, length);
            if (selected.Length >= delimiter.Length * 2 &&
                selected.StartsWith(delimiter, StringComparison.Ordinal) &&
                selected.EndsWith(delimiter, StringComparison.Ordinal))
            {
                var inner = selected[delimiter.Length..^delimiter.Length];
                return Replace(text, start, length, inner, start, inner.Length);
            }

            if (start >= delimiter.Length && start + length + delimiter.Length <= text.Length &&
                text.AsSpan(start - delimiter.Length, delimiter.Length).SequenceEqual(delimiter) &&
                text.AsSpan(start + length, delimiter.Length).SequenceEqual(delimiter))
            {
                return Replace(
                    text,
                    start - delimiter.Length,
                    length + delimiter.Length * 2,
                    selected,
                    start - delimiter.Length,
                    selected.Length);
            }

            var replacement = delimiter + selected + delimiter;
            return Replace(text, start, length, replacement, start + delimiter.Length, selected.Length);
        }

        var inserted = delimiter + placeholder + delimiter;
        return Replace(text, start, 0, inserted, start + delimiter.Length, placeholder.Length);
    }

    private static MarkdownEditResult ApplyLink(string text, int start, int length)
    {
        const string labelPlaceholder = "link text";
        const string destinationPlaceholder = "https://";
        var label = length == 0 ? labelPlaceholder : text.Substring(start, length);
        var replacement = $"[{label}]({destinationPlaceholder})";
        var selectedStart = length == 0
            ? start + 1
            : start + label.Length + 3;
        var selectedLength = length == 0 ? label.Length : destinationPlaceholder.Length;
        return Replace(text, start, length, replacement, selectedStart, selectedLength);
    }

    private static MarkdownEditResult ApplyFence(string text, int start, int length)
    {
        var newline = DetectNewline(text);
        if (length > 0)
        {
            var selected = text.Substring(start, length);
            var prefix = "```" + newline;
            var suffix = newline + "```";

            if (start >= prefix.Length && start + length + suffix.Length <= text.Length &&
                text.AsSpan(start - prefix.Length, prefix.Length).SequenceEqual(prefix) &&
                text.AsSpan(start + length, suffix.Length).SequenceEqual(suffix))
            {
                return Replace(text, start - prefix.Length, prefix.Length + length + suffix.Length, selected, start - prefix.Length, length);
            }

            var trailingNewline = selected.EndsWith(newline, StringComparison.Ordinal) ? string.Empty : newline;
            var replacement = prefix + selected + trailingNewline + "```";
            return Replace(text, start, length, replacement, start + prefix.Length, selected.Length);
        }

        const string placeholder = "code";
        var inserted = "```" + newline + placeholder + newline + "```";
        return Replace(text, start, 0, inserted, start + 3 + newline.Length, placeholder.Length);
    }

    private static MarkdownEditResult ApplyBlock(
        string text,
        int selectionStart,
        int selectionLength,
        MarkdownEditKind kind)
    {
        var blockStart = FindLineStart(text, selectionStart);
        var probe = selectionLength == 0 ? selectionStart : Math.Max(selectionStart, selectionStart + selectionLength - 1);
        var blockEnd = FindLineEnd(text, probe);
        var block = text[blockStart..blockEnd];
        var newline = DetectNewline(block.Length == 0 ? text : block);
        var lines = LineSplitRegex().Split(block);
        var transformed = new string[lines.Length];
        var orderedNumber = 1;

        for (var index = 0; index < lines.Length; index++)
        {
            var (indent, content) = StripBlockPrefix(lines[index]);
            if (string.IsNullOrWhiteSpace(content))
            {
                transformed[index] = lines[index];
                continue;
            }

            var prefix = kind switch
            {
                MarkdownEditKind.Paragraph => string.Empty,
                MarkdownEditKind.Heading1 => "# ",
                MarkdownEditKind.Heading2 => "## ",
                MarkdownEditKind.Heading3 => "### ",
                MarkdownEditKind.UnorderedList => "- ",
                MarkdownEditKind.OrderedList => $"{orderedNumber++}. ",
                MarkdownEditKind.TaskList => "- [ ] ",
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "The edit is not a block operation.")
            };
            transformed[index] = indent + prefix + content;
        }

        var replacement = string.Join(newline, transformed);
        if (replacement.Equals(block, StringComparison.Ordinal))
            return Unchanged(text, selectionStart, selectionLength);

        if (selectionLength > 0)
            return Replace(text, blockStart, block.Length, replacement, blockStart, replacement.Length);

        var originalLine = lines[0];
        var (_, originalContent) = StripBlockPrefix(originalLine);
        var originalContentStart = originalLine.Length - originalContent.Length;
        var newContentStart = transformed[0].Length - originalContent.Length;
        var contentOffset = Math.Clamp(selectionStart - blockStart - originalContentStart, 0, originalContent.Length);
        return Replace(text, blockStart, block.Length, replacement, blockStart + newContentStart + contentOffset, 0);
    }

    private static (string Indent, string Content) StripBlockPrefix(string line)
    {
        var match = BlockPrefixRegex().Match(line);
        return match.Success
            ? (match.Groups["indent"].Value, match.Groups["content"].Value)
            : (string.Empty, line);
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

    [GeneratedRegex(@"^(?<indent>[ \t]*)(?:(?:#{1,6})[ \t]+|(?:[-+*])[ \t]+\[[ xX]\][ \t]+|(?:[-+*]|\d+[.)])[ \t]+)?(?<content>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex BlockPrefixRegex();

    [GeneratedRegex("\\r\\n|\\n", RegexOptions.CultureInvariant)]
    private static partial Regex LineSplitRegex();
}
