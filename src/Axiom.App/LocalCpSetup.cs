using System.Diagnostics;

namespace Axiom.App;

internal static class LocalCpSetup
{
    private static string HelperPath(bool start) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Axiom", "CpConnection",
        start ? "Start-Axiom-Cp.cmd" : "Copy-Axiom-Cp-Credential.cmd");

    internal static bool Available(bool start) => OperatingSystem.IsWindows() && File.Exists(HelperPath(start));

    internal static async Task RunAsync(bool start, CancellationToken cancellationToken)
    {
        if (!Available(start)) throw new InvalidOperationException("Local CP setup is not installed. See CP connection Help; manual endpoint/credential entry remains available.");
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName        = HelperPath(start)
          , UseShellExecute = true
          , WindowStyle     = ProcessWindowStyle.Hidden
        }) ?? throw new InvalidOperationException("The local CP helper could not start.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        await process.WaitForExitAsync(timeout.Token);
        if (process.ExitCode != 0) throw new InvalidOperationException("The local CP helper failed. Check the local setup and try again.");
    }
}
