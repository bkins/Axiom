using System.Text.Json;
using Axiom.Core;

namespace Axiom.Infrastructure;

public sealed class RollingFileLog : IAxiomLog
{
    private readonly string _directory;
    private readonly object _gate = new();

    public RollingFileLog(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(directory);
        Prune();
    }

    public void Information(string eventName, string? path = null) => Write("Information", eventName, null, path);
    public void Error(string eventName, Exception exception, string? path = null) => Write("Error", eventName, exception, path);

    private void Write(string level, string eventName, Exception? exception, string? path)
    {
        var entry = new
        {
            timestampUtc = DateTimeOffset.UtcNow,
            level,
            eventName,
            path,
            exceptionType = exception?.GetType().FullName,
            error = exception?.Message
        };
        lock (_gate)
        {
            File.AppendAllText(Path.Combine(_directory, $"axiom-{DateTime.UtcNow:yyyyMMdd}.log"), JsonSerializer.Serialize(entry) + Environment.NewLine);
        }
    }

    private void Prune()
    {
        foreach (var path in Directory.EnumerateFiles(_directory, "axiom-*.log")
                     .OrderByDescending(File.GetLastWriteTimeUtc)
                     .Skip(10))
            File.Delete(path);
    }
}

