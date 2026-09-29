namespace Axiom.Infrastructure;

public sealed class WorkspaceChangeMonitor : IDisposable
{
    private FileSystemWatcher? _watcher;
    private FileSystemWatcher? _directoryWatcher;
    private int _generation;

    public event EventHandler<string>? Changed;

    public void Watch(string? root)
    {
        var generation = Interlocked.Increment(ref _generation);
        _watcher?.Dispose();
        _directoryWatcher?.Dispose();
        _watcher = null;
        _directoryWatcher = null;
        if (root is null || !Directory.Exists(root)) return;
        var normalizedRoot = Path.GetFullPath(root);
        var watcher = new FileSystemWatcher(normalizedRoot)
        {
            IncludeSubdirectories = true
          , NotifyFilter          = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
        };
        watcher.Created += (_, eventArgs) => NotifyDocument(eventArgs.FullPath);
        watcher.Deleted += (_, eventArgs) => NotifyDocument(eventArgs.FullPath);
        watcher.Renamed += (_, eventArgs) => { NotifyDocument(eventArgs.OldFullPath); NotifyDocument(eventArgs.FullPath); };
        watcher.Changed += (_, eventArgs) => NotifyDocument(eventArgs.FullPath);
        void NotifyDocument(string path)
        {
            if (path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase)) Notify();
        }
        // Overflow requires a complete rescan, just like an ordinary structural change.
        watcher.Error += (_, _) => Notify();
        _watcher = watcher;
        watcher.EnableRaisingEvents = true;
        var directories = new FileSystemWatcher(normalizedRoot);
        directories.IncludeSubdirectories = true;
        directories.NotifyFilter = NotifyFilters.DirectoryName;
        directories.Created += (_, _) => Notify();
        directories.Deleted += (_, _) => Notify();
        directories.Renamed += (_, _) => Notify();
        directories.Error += (_, _) => Notify();
        _directoryWatcher = directories;
        directories.EnableRaisingEvents = true;

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
