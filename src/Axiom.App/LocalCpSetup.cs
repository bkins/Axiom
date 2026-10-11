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
            // File.Exists collapses access/detection failures into false. Let Windows
            // resolve the fixed launcher and report its actual launch error instead.
            helper = Process.Start(new ProcessStartInfo
            {
                FileName        = helperPath
              , UseShellExecute = true
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
        await process.WaitForExitAsync(timeout.Token);
        if (process.ExitCode != 0) throw new InvalidOperationException("The local CP helper failed. Check the local setup and try again.");
    }
}
