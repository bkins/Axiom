using System.Diagnostics;

namespace Axiom.Infrastructure;

public static class PortableUpdateLauncher
{
    public static string BuildRestartScript(string directory, string stateRoot, int processId)
    {
        var executable = Path.Combine(Path.GetFullPath(directory), "Axiom.App.exe");
        if (processId < 1) throw new ArgumentOutOfRangeException(nameof(processId));
        return $$"""
            $ErrorActionPreference = 'Stop'
            Wait-Process -Id {{processId}} -Timeout 30 -ErrorAction SilentlyContinue
            if (Get-Process -Id {{processId}} -ErrorAction SilentlyContinue) { exit 2 }
            $env:AXIOM_STATE_ROOT = '{{Quote(stateRoot)}}'
            Start-Process -FilePath '{{Quote(executable)}}' -WorkingDirectory '{{Quote(directory)}}'
            """;
    }

    public static void ScheduleRestart(string directory, string stateRoot)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Portable restart currently requires Windows.");
        var script = Path.Combine(Path.GetTempPath(), "AxiomRestart-" + Guid.NewGuid().ToString("N") + ".ps1");
        File.WriteAllText(script, BuildRestartScript(directory, stateRoot, Environment.ProcessId));
        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false
                      ,
            CreateNoWindow = true
                      ,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("The restart helper could not be started.");
    }

    private static string Quote(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}
