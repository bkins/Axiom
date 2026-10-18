using Markdig.Extensions.TaskLists;
using Markdig.Syntax;

namespace Axiom.Core;

public sealed record PreviewTaskMarker(int Offset, bool Checked);

public static class PreviewTaskToggle
{
    public static IReadOnlyList<PreviewTaskMarker> Read(DocumentStructure document) => document.Syntax.Descendants<TaskList>()
        .Select(task => task.Span.Start)
        .Where(start => start >= 0 && start + 2 < document.SourceText.Length && document.SourceText[start] == '[' &&
            document.SourceText[start + 2] == ']' && document.SourceText[start + 1] is ' ' or 'x' or 'X')
        .Select(start => new PreviewTaskMarker(start + 1, document.SourceText[start + 1] != ' ')).ToArray();

    public static bool TryToggle(DocumentStructure rendered, string currentSource, int offset, bool wasChecked, out char replacement)
    {
        replacement = default;
        if (!string.Equals(rendered.SourceText, currentSource, StringComparison.Ordinal) ||
            !Read(rendered).Any(marker => marker.Offset == offset && marker.Checked == wasChecked)) return false;
        replacement = wasChecked ? ' ' : 'x';
        return true;
    }
}
