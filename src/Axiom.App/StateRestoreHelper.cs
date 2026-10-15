using System.Diagnostics;
using Axiom.Infrastructure;

namespace Axiom.App;

internal static class StateRestoreHelper
{
    internal static int Run(string[] args)
    {
        if (args.Length != 5 || !int.TryParse(args[4], out var parent)) return 2;
        var paths = new AppDataPaths(args[1]);
        try
        {
            try
            {
                using var process = Process.GetProcessById(parent);
                if (!process.WaitForExit(60_000)) throw new IOException("Axiom did not close. Restore was cancelled; current state is unchanged.");
            }
            catch (ArgumentException) { }
            // Keep mutex acquisition/disposal on this synchronous STA thread.
            using (var ownership = new SingleInstanceCoordinator(paths.Root))
            {
                if (!ownership.IsPrimary) throw new IOException("Another Axiom process is using this state. Close it before retrying restore.");
                var rollback = new StateRestoreService(paths).RestoreOfflineAsync(args[2], args[3]).GetAwaiter().GetResult();
                File.WriteAllText(Path.Combine(paths.Root, "State", "last-restore.txt"), $"Restored {DateTimeOffset.Now:O}\nReviewed backup: {args[2]}\nPrevious state retained: {rollback}\nMarkdown files were not restored or changed.");
            }
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
            start.Environment["AXIOM_STATE_ROOT"] = paths.Root;
            if (Environment.GetEnvironmentVariable("AXIOM_NATIVE_RESTORE_PROBE") is { } probe)
            { start.ArgumentList.Add("--state-restore-probe"); start.ArgumentList.Add(probe); }
            Process.Start(start);
            return 0;
        }
        catch (Exception error)
        {
            StartupFailure.Show("State restore did not complete: " + error.Message + "\nYour reviewed backup and any pre-restore backup remain in " + paths.BackupDirectory + ". Reopen Axiom to inspect current state.");
            return 1;
        }
    }
}
