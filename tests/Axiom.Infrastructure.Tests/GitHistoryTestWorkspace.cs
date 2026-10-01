using System.Diagnostics;

namespace Axiom.Infrastructure.Tests;

internal sealed class GitHistoryTestWorkspace : IDisposable
{
    internal string Root { get; } = Path.Combine(Path.GetTempPath(), "AxiomGitHistory-" + Guid.NewGuid().ToString("N"));

    internal GitHistoryTestWorkspace()
    {
        Directory.CreateDirectory(Root);
        Run("init", "-q");
        Run("config", "user.name", "Axiom test");
        Run("config", "user.email", "test@axiom.local");
        Run("config", "core.autocrlf", "false");
    }

    internal string Run(params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory       = Root
          , UseShellExecute        = false
          , CreateNoWindow         = true
          , RedirectStandardOutput = true
          , RedirectStandardError  = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd();
        var errors = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(30000)) { process.Kill(true); throw new IOException("Test Git command timed out."); }
        if (process.ExitCode != 0) throw new IOException(errors);
        return output.TrimEnd('\r', '\n');
    }

    public void Dispose()
    {
        foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(Root, true);
    }
}
