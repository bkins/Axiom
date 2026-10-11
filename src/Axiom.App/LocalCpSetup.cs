using System.Diagnostics;
using System.ComponentModel;

namespace Axiom.App;

internal static class LocalCpSetup
{
    private static string HelperPath(bool start) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Axiom", "CpConnection",
        start ? "Start-Axiom-Cp.cmd" : "Copy-Axiom-Cp-Credential.cmd");

    internal static Task RunAsync(bool start, CancellationToken cancellationToken)
        => RunHelperAsync(HelperPath(start), cancellationToken);

    internal static async Task RunHelperAsync(string helperPath, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) throw new InvalidOperationException("Local CP helpers require Windows. Enter your endpoint and credential manually.");
        cancellationToken.ThrowIfCancellationRequested();
        Process? helper;
        try
        {
            // A CMD file is not an executable. Use the fixed Windows command host
            // instead of relying on ShellExecute's file association and caller context.
            helper = Process.Start(new ProcessStartInfo
            {
                FileName        = Path.Combine(Environment.SystemDirectory, "cmd.exe")
              , Arguments       = "/d /v:off /s /c \"\"" + helperPath + "\"\""
              , WorkingDirectory = Environment.SystemDirectory
              , UseShellExecute = false
              , CreateNoWindow  = true
              , RedirectStandardError = true
              , RedirectStandardOutput = true
              , WindowStyle     = ProcessWindowStyle.Hidden
            });
        }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException($"Windows could not launch the local CP helper at {helperPath}. Windows error {exception.NativeErrorCode}: {exception.Message}. Check the local CP setup or use its external launcher.", exception);
        }
        using var process = helper ?? throw new InvalidOperationException("The local CP helper could not start.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        // Drain both pipes concurrently so a failing helper cannot block on output.
        // Successful clipboard-helper output is deliberately discarded.
        var standardError = process.StandardError.ReadToEndAsync(timeout.Token);
        var standardOutput = process.StandardOutput.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token);
        var error = (await standardError).Trim();
        await standardOutput;
        if (process.ExitCode != 0)
        {
            // Only disclose known command-host diagnostics, never arbitrary helper
            // output that might contain credentials or other sensitive values.
            var commandFailure = error.Contains("is not recognized as an internal or external command", StringComparison.OrdinalIgnoreCase)
                || error.Equals("Access is denied.", StringComparison.OrdinalIgnoreCase)
                || error.Equals("The system cannot find the path specified.", StringComparison.OrdinalIgnoreCase)
                || error.Equals("The system cannot find the file specified.", StringComparison.OrdinalIgnoreCase);
            var detail = commandFailure ? " Command host: " + error[..Math.Min(error.Length, 1024)] : " No recognized command-host diagnostic was returned.";
            throw new InvalidOperationException($"The local CP helper at {helperPath} exited with code {process.ExitCode}.{detail} Check the local setup or use its external launcher.");
        }
    }
}
