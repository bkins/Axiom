using System.Diagnostics;

namespace Axiom.App;

internal static class RepositoryTerminal
{
    internal static ProcessStartInfo CreateStartInfo(string repositoryRoot)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("External terminal access is currently available on Windows.");
        var root = Path.GetFullPath(repositoryRoot);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("The Git repository no longer exists.");
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"))
        {
            WorkingDirectory = root
          , UseShellExecute = false
          , CreateNoWindow = false
          , WindowStyle = ProcessWindowStyle.Normal
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NoExit");
        foreach (var key in start.Environment.Keys.Where(key => key.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase)).ToArray()) start.Environment.Remove(key);
        return start;
    }

    internal static void Open(string repositoryRoot)
    {
        using var process = Process.Start(CreateStartInfo(repositoryRoot)) ?? throw new IOException("The terminal could not be opened.");
    }
}
