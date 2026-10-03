namespace Axiom.Core;

public sealed partial class MarkdownEditService
{
    private static MarkdownEditResult ApplyTemplate(string text, int start, int length, MarkdownEditKind kind)
    {
        var newline = DetectNewline(text);
        var selected = length == 0 ? "Text" : text.Substring(start, length);
        var prefix = start == 0 || text[start - 1] == '\n' ? "" : newline + newline;
        var body = kind switch
        {
            MarkdownEditKind.Table => "| Column 1 | Column 2 |" + newline + "| --- | --- |" + newline + "| " + selected.Replace("|", "\\|").Replace("\r", "").Replace("\n", " ") + " | Text |",
            MarkdownEditKind.HorizontalRule => "---",
            MarkdownEditKind.Callout => "> [!note] Note" + newline + "> " + selected.Replace("\r\n", "\n").Replace("\n", newline + "> "),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        var inserted = prefix + body + newline + newline;
        var position = kind == MarkdownEditKind.Callout ? prefix.Length + "> [!note] Note".Length + newline.Length + 2 : prefix.Length;
        return Replace(text, start, length, inserted, start + position, kind == MarkdownEditKind.Callout ? selected.Length : 0);
    }
}
