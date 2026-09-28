namespace Axiom.Infrastructure;

public sealed class DiagnosticsReader(AppDataPaths paths)
{
    public string ReadTail(int maximumLines = 500, int maximumCharacters = 100_000)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumLines, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumCharacters, 1);
        if (!Directory.Exists(paths.LogDirectory)) return "No diagnostic logs are available.";
        var entries = new Queue<string>();
        var length = 0;
        foreach (var file in Directory.EnumerateFiles(paths.LogDirectory, "*.log").OrderByDescending(File.GetLastWriteTimeUtc).Take(2).Reverse())
        {
            if (File.GetAttributes(file).HasFlag(FileAttributes.ReparsePoint)) continue;
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            // Read only a bounded byte tail, including for a malformed huge log.
            var start = Math.Max(0, stream.Length - maximumCharacters * 4L);
            stream.Seek(start, SeekOrigin.Begin);
            using var reader = new StreamReader(stream);
            if (start > 0) reader.ReadLine();
            while (reader.ReadLine() is { } line)
            {
                if (line.Length > maximumCharacters) line = line[^maximumCharacters..];
                entries.Enqueue(line);
                length += line.Length + Environment.NewLine.Length;
                while (entries.Count > maximumLines || length > maximumCharacters)
                    length -= entries.Dequeue().Length + Environment.NewLine.Length;
            }
        }
        return entries.Count == 0 ? "No diagnostic log entries are available." : string.Join(Environment.NewLine, entries);
    }
}
