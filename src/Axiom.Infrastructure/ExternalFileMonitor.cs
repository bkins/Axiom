namespace Axiom.Infrastructure;

public sealed class ExternalFileMonitor : IDisposable
{
    private FileSystemWatcher? _watcher;
    private Timer? _timer;
    private string? _path;
    private int _pending;
    private readonly Dictionary<string, ExternalFileMonitor> _children = new(StringComparer.OrdinalIgnoreCase);

    public event EventHandler<string>? Changed;
    public event EventHandler<(string OldPath, string NewPath)>? Renamed;

    public void Watch(string? path)
    {
        DisposeChildren();
        DisposeWatcher();
        _path = path is null ? null : Path.GetFullPath(path);
        if (_path is null || Path.GetDirectoryName(_path) is not { } directory || !Directory.Exists(directory)) return;

        _watcher = new FileSystemWatcher(directory)
        {
            Filter = Path.GetFileName(_path),
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            EnableRaisingEvents = true
        };
        _watcher.Changed += (_, _) => ScheduleChanged();
        _watcher.Deleted += (_, _) => ScheduleChanged();
        _watcher.Renamed += (_, eventArgs) => Renamed?.Invoke(this, (eventArgs.OldFullPath, eventArgs.FullPath));
    }

    public void WatchMany(IEnumerable<string> paths)
    {
        DisposeWatcher();
        _path = null;
        var normalized = paths.Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var removed in _children.Keys.Where(path => !normalized.Contains(path)).ToArray())
        {
            _children[removed].Dispose();
            _children.Remove(removed);
        }
        foreach (var path in normalized)
        {
            if (_children.ContainsKey(path)) continue;
            var child = new ExternalFileMonitor();
            child.Changed += (_, changed) => Changed?.Invoke(this, changed);
            child.Renamed += (_, renamed) => Renamed?.Invoke(this, renamed);
            child.Watch(path);
            _children.Add(path, child);
        }
    }

    private void DisposeChildren()
    {
        foreach (var child in _children.Values) child.Dispose();
        _children.Clear();
    }

    private void ScheduleChanged()
    {
        Interlocked.Exchange(ref _pending, 1);
        _timer?.Dispose();
        _timer = new Timer(_ =>
        {
            if (Interlocked.Exchange(ref _pending, 0) == 1 && _path is { } path)
                Changed?.Invoke(this, path);
        }, null, 250, Timeout.Infinite);
    }

    private void DisposeWatcher()
    {
        _timer?.Dispose();
        _timer = null;
        _watcher?.Dispose();
        _watcher = null;
    }

    public void Dispose()
    {
        DisposeChildren();
        DisposeWatcher();
        GC.SuppressFinalize(this);
    }
}

