using System.Text.Json;
using Axiom.Core;
using Axiom.Infrastructure;
using Velopack;
using Velopack.Locators;
using Velopack.Sources;

namespace Axiom.App;

internal static class AutomaticUpdateProbe
{
    internal static int CheckPublishedFeed(string receipt)
    {
        try
        {
            var client = new VelopackUpdateClient(new AutomaticUpdatePreferences(Channel: "preview"));
            if (!client.IsInstalled) throw new IOException("Run from the update-enabled package.");
            var version = client.DownloadLatestAsync(default).GetAwaiter().GetResult();
            File.WriteAllText(receipt, JsonSerializer.Serialize(new { Repository = VelopackUpdateClient.Repository, Channel = "preview", InstalledVersion = VelopackLocator.Current.CurrentlyInstalledVersion!.ToString(), AvailableVersion = version, CheckSucceeded = true }));
            return 0;
        }
        catch (Exception exception) { File.WriteAllText(receipt, exception.ToString()); return 1; }
    }

    internal static int Run(string[] args)
    {
        // Local fixtures have a different application ID. This cannot redirect a real install.
        if (VelopackLocator.Current.AppId != "AxiomUpdaterProbe" || args.Length != 3) return 2;
        var receipt = Path.GetFullPath(args[2]);
        try
        {
            var paths = new AppDataPaths();
            var state = new SqliteStateStore(paths);
            state.InitializeAsync().GetAwaiter().GetResult();
            if (args[0] == "--automatic-update-receipt")
            {
                var drafts = state.GetRecoverableDraftsAsync().GetAwaiter().GetResult();
                if (!drafts.Any(draft => draft.DraftKey == "updater-probe" && draft.Text == "unsaved update fixture")) throw new IOException("Recovery draft was lost.");
                File.WriteAllText(receipt, JsonSerializer.Serialize(new { Version = VelopackLocator.Current.CurrentlyInstalledVersion!.ToString(), DraftRetained = true, StateRoot = paths.Root }));
                return 0;
            }
            state.SaveDraftAsync(new RecoveryDraft("updater-probe", null, "unsaved update fixture", DateTimeOffset.UtcNow, null, true)).GetAwaiter().GetResult();
            var manager = new UpdateManager(new SimpleFileSource(new DirectoryInfo(args[1])), new UpdateOptions { ExplicitChannel = "win-x64-preview" });
            var client = new VelopackUpdateClient(manager);
            var version = client.DownloadLatestAsync(default).GetAwaiter().GetResult();
            if (version is null) throw new IOException("No fixture update found.");
            manager.ApplyUpdatesAndRestart(manager.UpdatePendingRestart!, new[] { "--automatic-update-receipt", args[1], receipt });
            return 0;
        }
        catch (Exception exception) { File.WriteAllText(receipt, exception.ToString()); return 1; }
    }
}
