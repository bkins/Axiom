using Axiom.Infrastructure;

namespace Axiom.App;

public partial class MainWindow
{
    private async Task<DocumentTab?> FindPhysicalTabAsync(string path, DocumentTab? excluded = null)
    {
        if (OperatingSystem.IsWindows()) return FindTab(path) is { } existing && existing != excluded ? existing : null;
        var observedTabs = _tabs.Where(tab => tab != excluded && tab.Session.Path is { } savedPath && File.Exists(savedPath)).ToArray();
        if (observedTabs.Length == 0) return null;
        var paths = new[] { path }.Concat(observedTabs.Select(tab => tab.Session.Path!)).ToArray();
        var identities = await new UnixFileIdentityReader().ReadAsync(paths);
        var aliasIndex = Enumerable.Range(0, observedTabs.Length).FirstOrDefault(index => identities[index + 1] == identities[0], -1);
        return aliasIndex >= 0 && _tabs.Contains(observedTabs[aliasIndex]) ? observedTabs[aliasIndex] : null;
    }
}
