namespace Axiom.Infrastructure;

public sealed class WorkspaceChangeMonitor : IDisposable
{
    private FileSystemWatcher? _watcher;
    private int _generation;

    public event EventHandler<string>? Changed;

    public void Watch(string? root)
    {
        var generation = Interlocked.Increment(ref _generation);
        _watcher?.Dispose();
        _watcher = null;
        if (root is null || !Directory.Exists(root)) return;
        var normalizedRoot = Path.GetFullPath(root);
        var watcher = new FileSystemWatcher(normalizedRoot)
        {
            IncludeSubdirectories = true
          , NotifyFilter          = NotifyFilters.FileName | NotifyFilters.DirectoryName
        };
        watcher.Created += (_, _) => Notify();
        watcher.Deleted += (_, _) => Notify();
        watcher.Renamed += (_, _) => Notify();
        // Overflow requires a complete rescan, just like an ordinary structural change.
        watcher.Error += (_, _) => Notify();
        _watcher = watcher;
        watcher.EnableRaisingEvents = true;

        void Notify()
        {
            if (generation == Volatile.Read(ref _generation)) Changed?.Invoke(this, normalizedRoot);
        }
    }

    public void Dispose()
    {
        Watch(null);
        GC.SuppressFinalize(this);
    }
}
