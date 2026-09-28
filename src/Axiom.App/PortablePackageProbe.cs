using System.Text.Json;
using Axiom.Infrastructure;

namespace Axiom.App;

internal static class PortablePackageProbe
{
    public static int Run(string[] args)
    {
        if (args.Length != 4) return 2;
        try
        {
            var paths = new AppDataPaths(args[2]);
            var state = new SqliteStateStore(paths);
            state.InitializeAsync().GetAwaiter().GetResult();
            var service = new PortablePackageService(paths);
            var manifest = service.ValidateAsync(args[1]).GetAwaiter().GetResult();
            var installed = service.InstallAsync(args[1], SqliteStateStore.CurrentSchemaVersion).GetAwaiter().GetResult();
            service.ValidateAsync(installed).GetAwaiter().GetResult();
            var maintenance = new StateMaintenanceService(paths);
            var backup = maintenance.ExportBackupAsync(Path.Combine(paths.Root, "verified-backup.db")).GetAwaiter().GetResult();
            File.WriteAllText(args[3], JsonSerializer.Serialize(new { manifest.InformationalVersion, PayloadFiles = manifest.Files.Count, Installed = installed, Backup = backup, Inspection = maintenance.InspectAsync().GetAwaiter().GetResult() }));
            return 0;
        }
        catch (Exception exception)
        {
            File.WriteAllText(args[3], exception.ToString());
            return 1;
        }
    }
}
