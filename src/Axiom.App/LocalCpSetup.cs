using System.Diagnostics;
using System.ComponentModel;
using System.Text;

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
        using var drainCancellation = new CancellationTokenSource();
        var standardError = DrainAsync(process.StandardError, drainCancellation.Token, capture: true);
        var standardOutput = DrainAsync(process.StandardOutput, drainCancellation.Token, capture: false);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException("The local CP helper did not finish within three minutes. The API may already be running. Try Connect / check capabilities, or retry Start local CP. No document was submitted.");
        }
        finally
        {
            // A server started by the helper can inherit these handles. Its lifetime
            // must not hold the completed helper operation open waiting for EOF.
            drainCancellation.Cancel();
            await Task.WhenAll(standardError, standardOutput);
        }
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

    private static async Task<string> DrainAsync(StreamReader reader, CancellationToken cancellationToken, bool capture)
    {
        var text = new StringBuilder();
        var buffer = new char[1024];
        try
        {
            int count;
            while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken)) != 0)
                if (capture && text.Length < 4096) text.Append(buffer, 0, Math.Min(count, 4096 - text.Length));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        return text.ToString();
    }
}
