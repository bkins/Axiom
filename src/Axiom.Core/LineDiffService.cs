namespace Axiom.Core;

public sealed class LineDiffService
{
    public IReadOnlyList<LineDiffEntry> Compare(string previous, string current)
    {
        if (previous.Length > 4 * 1024 * 1024 || current.Length > 4 * 1024 * 1024)
            throw new IOException("Line comparison supports buffers up to 4 Mi characters.");
        var previousLines = Split(previous);
        var currentLines = Split(current);
        if (previousLines.Length > 100000 || currentLines.Length > 100000)
            throw new IOException("Line comparison supports at most 100,000 lines per buffer.");
        var prefix = 0;
        while (prefix < previousLines.Length && prefix < currentLines.Length && previousLines[prefix] == currentLines[prefix]) prefix++;
        var suffix = 0;
        while (suffix < previousLines.Length - prefix && suffix < currentLines.Length - prefix &&
               previousLines[^(suffix + 1)] == currentLines[^(suffix + 1)]) suffix++;
        var previousCount = previousLines.Length - prefix - suffix;
        var currentCount = currentLines.Length - prefix - suffix;
        if ((long)(previousCount + 1) * (currentCount + 1) > 2_000_000)
            throw new IOException("This comparison exceeds the line-diff limit. Compare smaller revisions.");
        var lengths = new int[previousCount + 1, currentCount + 1];
        for (var previousIndex = previousCount - 1; previousIndex >= 0; previousIndex--)
            for (var currentIndex = currentCount - 1; currentIndex >= 0; currentIndex--)
                lengths[previousIndex, currentIndex] = previousLines[prefix + previousIndex] == currentLines[prefix + currentIndex]
                    ? 1 + lengths[previousIndex + 1, currentIndex + 1]
                    : Math.Max(lengths[previousIndex + 1, currentIndex], lengths[previousIndex, currentIndex + 1]);
        var result = new List<LineDiffEntry>();
        for (var index = 0; index < prefix; index++) result.Add(new(LineDiffKind.Unchanged, index + 1, index + 1, previousLines[index]));
        var previousOffset = 0;
        var currentOffset = 0;
        while (previousOffset < previousCount || currentOffset < currentCount)
        {
            if (previousOffset < previousCount && currentOffset < currentCount &&
                previousLines[prefix + previousOffset] == currentLines[prefix + currentOffset])
            {
                result.Add(new(LineDiffKind.Unchanged, prefix + previousOffset + 1, prefix + currentOffset + 1, previousLines[prefix + previousOffset]));
                previousOffset++;
                currentOffset++;
            }
            else if (previousOffset < previousCount && (currentOffset == currentCount || lengths[previousOffset + 1, currentOffset] >= lengths[previousOffset, currentOffset + 1]))
            {
                result.Add(new(LineDiffKind.Removed, prefix + previousOffset + 1, null, previousLines[prefix + previousOffset++]));
            }
            else result.Add(new(LineDiffKind.Added, null, prefix + currentOffset + 1, currentLines[prefix + currentOffset++]));
        }
        for (var index = suffix; index > 0; index--)
            result.Add(new(LineDiffKind.Unchanged, previousLines.Length - index + 1, currentLines.Length - index + 1, previousLines[^index]));
        return result;
    }

    private static string[] Split(string text)
    {
        var lines = new List<string>();
        var start = 0;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] is not ('\r' or '\n')) continue;
            if (text[index] == '\r' && index + 1 < text.Length && text[index + 1] == '\n') index++;
            lines.Add(text[start..(index + 1)]);
            start = index + 1;
        }
        if (start < text.Length) lines.Add(text[start..]);
        return lines.ToArray();
    }
}
