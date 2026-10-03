using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Axiom.Infrastructure;
using Axiom.Core;

namespace Axiom.App;

public partial class MainWindow
{
    private readonly AppDataPaths _operationalPaths = new();

    private async void Diagnostics_OnClick(object? sender, RoutedEventArgs eventArgs)
        => await ExecuteCommandAsync("application.diagnostics");

    private async Task<CommandResult> ShowDiagnosticsAsync()
    {
        var detailsVisible = false;
        Task<string> ReadDiagnosticsAsync() => Task.Run(() =>
        {
            var raw = new DiagnosticsReader(_operationalPaths).ReadTail();
            var summary = $"State: {_operationalPaths.Root}\nLogs: {_operationalPaths.LogDirectory}\n\n{DiagnosticsPresentation.Summarize(raw)}";
            return detailsVisible ? summary + "\n\nTechnical details (original records):\n" + raw : summary;
        });
        var window = new InformationWindow("Axiom diagnostics", ReadDiagnosticsAsync,
            () => { detailsVisible = !detailsVisible; return ReadDiagnosticsAsync(); }, "Show/hide technical details");
        await window.ShowDialog(this);
        return new(CommandOutcome.Completed, "Diagnostics closed.");
    }

    private async void Maintenance_OnClick(object? sender, RoutedEventArgs eventArgs)
        => await ExecuteCommandAsync("application.maintenance");

    private async Task<CommandResult> ShowMaintenanceAsync()
    {
        var service = new StateMaintenanceService(_operationalPaths);
        InformationWindow? window = null;
        window = new InformationWindow("Axiom state maintenance", InspectAsync, ExportAsync, "Export verified backup…");
        await window.ShowDialog(this);
        return new(CommandOutcome.Completed, "State maintenance viewer closed.");

        async Task<string> InspectAsync()
        {
            var state = await service.InspectAsync();
            return $"Database: {_operationalPaths.DatabasePath}\nIntegrity: {state.Integrity}\nSchema: {state.SchemaVersion}\nWorkspaces: {state.Workspaces}\nProtected drafts: {state.Drafts}\nIdentity paths: {state.Identities}\nPending file operations: {state.PendingOperations}\n\nInspection and backup do not change Markdown. Reset, repair, and live restore are not available.";
        }
        async Task<string> ExportAsync()
        {
            var file = await window!.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export Axiom state backup (choose a new file)"
                , SuggestedFileName = $"axiom-backup-{DateTime.Now:yyyyMMdd-HHmmss}.db"
                , DefaultExtension = "db"
            });
            if (file?.TryGetLocalPath() is not { } path) return "Backup cancelled; no files changed.";
            var result = await service.ExportBackupAsync(path);
            return $"Verified backup exported: {result}\nRestore requires closing all Axiom processes first.\n\n{await InspectAsync()}";
        }
    }

    private async void About_OnClick(object? sender, RoutedEventArgs eventArgs)
        => await ExecuteCommandAsync("application.about");

    private async Task<CommandResult> ShowAboutAsync()
    {
        await new InformationWindow("About Axiom", GetAboutInformationAsync).ShowDialog(this);
        return new(CommandOutcome.Completed, "About closed.");
    }

    internal async Task<string> GetAboutInformationAsync()
    {
        var result = $"Axiom {_buildIdentity.ProductVersion}\nBuild: {_buildIdentity.InformationalVersion}\nOS: {RuntimeInformation.OSDescription}\nRuntime: {RuntimeInformation.FrameworkDescription}\nArchitecture: {RuntimeInformation.ProcessArchitecture}\nState: {_operationalPaths.Root}\nSupported state schema: {SqliteStateStore.CurrentSchemaVersion}";
        var manifestPath = Path.Combine(AppContext.BaseDirectory, PortablePackageService.ManifestName);
        if (!File.Exists(manifestPath)) return result + "\nPublish metadata: unavailable (development build).";
        try
        {
            await using var stream = File.OpenRead(manifestPath);
            var manifest = await JsonSerializer.DeserializeAsync<PortablePackageManifest>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (manifest?.InformationalVersion != _buildIdentity.InformationalVersion) return result + "\nPublish metadata: contradicts the running assembly.";
            return result + $"\nSource: {manifest.CommitId}\nBranch: {manifest.Branch}\nBuilt UTC: {manifest.BuiltUtc:u}\nChannel: {manifest.Channel ?? "unspecified (legacy manifest)"}\nRelease notes: {manifest.ReleaseNotes ?? "not supplied"}\nRetained versions: {Path.Combine(_operationalPaths.Root, "Versions")}";
        }
        catch (Exception exception) { return result + "\nPublish metadata unavailable: " + exception.Message; }
    }

    private async void InstallUpdate_OnClick(object? sender, RoutedEventArgs eventArgs)
        => await ExecuteCommandAsync(sender is Control { Tag: "Switch" } ? "application.switchPortable" : "application.installPortable");

    private async Task<CommandResult> InstallPortableVersionAsync(bool rollback)
    {
        if (_mutationBlocked) return new(CommandOutcome.Unavailable, "Resolve pending file operations before switching versions.");
        try
        {
            var versions = Path.Combine(_operationalPaths.Root, "Versions");
            if (await ChoiceDialog.ShowAsync(this, rollback ? "Switch Axiom version" : "Install Axiom update",
                    "Choose payload-manifest.json inside a published Axiom application package. This is the folder containing Axiom.App.exe, not your Markdown workspace.\n\n" +
                    (rollback ? "You may choose an older compatible package or return to a newer retained package." : "Choose a package with a higher version number than the running app.") +
                    "\n\nAxiom checks the package before asking for confirmation. Cancel leaves this app running. Restart is offered after confirmation; Later keeps this app running.",
                    "Choose package…", "Cancel") != "Choose package…") return new(CommandOutcome.Cancelled, "Package selection cancelled.");
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title                    = "Choose payload-manifest.json in the Axiom application package"
              , AllowMultiple            = false
              , FileTypeFilter           = [new FilePickerFileType("Axiom package manifest") { Patterns = [PortablePackageService.ManifestName] }]
              , SuggestedStartLocation   = rollback && Directory.Exists(versions) ? await StorageProvider.TryGetFolderFromPathAsync(versions) : null
            });
            if (files.FirstOrDefault()?.TryGetLocalPath() is not { } selectedManifest) return new(CommandOutcome.Cancelled, "Package selection cancelled.");
            var source = PortablePackageService.GetPackageDirectory(selectedManifest);
            var service = new PortablePackageService(_operationalPaths);
            UpdateChrome("Verifying portable package…");
            var manifest = await Task.Run(() => service.ValidateAsync(source));
            var inspection = await new StateMaintenanceService(_operationalPaths).InspectAsync();
            if (inspection.Integrity != "ok") throw new IOException("The current state did not pass integrity verification. Export a backup and resolve it before switching versions.");
            PortablePackageService.EnsureSchemaCompatible(manifest.SchemaVersion, inspection.SchemaVersion);
            if (inspection.PendingOperations > 0) throw new IOException("Resolve pending file operations before switching versions.");
            if (!rollback && Version.Parse(manifest.Version) <= Version.Parse(_buildIdentity.ProductVersion))
                throw new IOException("An update must be newer. Use Switch Portable Version for a retained older package.");
            if (await ChoiceDialog.ShowAsync(this, "Verified package", $"Version: {manifest.InformationalVersion}\nBranch: {manifest.Branch}\nBuilt UTC: {manifest.BuiltUtc:u}\nSource: {source}\n\n{(rollback ? "Switch to this retained version" : "Install into a new version folder")}? Restart keeps state at {_operationalPaths.Root}. Existing binaries remain for rollback.", "Continue", "Cancel") != "Continue") return new(CommandOutcome.Cancelled, "Verified package declined.");
            if (!await ConfirmAllTabsAsync()) return new(CommandOutcome.Cancelled, "Tab review cancelled version switching.");
            await new StateMaintenanceService(_operationalPaths).ExportBackupAsync(Path.Combine(_operationalPaths.BackupDirectory, "pre-switch-" + Guid.NewGuid().ToString("N") + ".db"));
            var destination = rollback ? source : await Task.Run(() => service.InstallAsync(source, inspection.SchemaVersion));
            await service.ValidateAsync(destination);
            if (await ChoiceDialog.ShowAsync(this, "Version ready", $"Verified version is ready at {destination}. Restart now?", "Restart", "Later") != "Restart")
            {
                UpdateChrome($"This app is still running. Version ready at {destination}. Use Tools → Switch Portable Version and choose its payload-manifest.json when ready.");
                return new(CommandOutcome.Completed, "Verified version prepared; restart deferred.");
            }
            await RunSessionTransitionAsync(async () =>
            {
                if (_mutationBlocked || ManagementState is not null && (await ManagementState.GetPendingMutationsAsync()).Count > 0)
                    throw new IOException("Resolve pending file operations before switching versions.");
                await _markCleanExit();
                PortableUpdateLauncher.ScheduleRestart(destination, _operationalPaths.Root);
                _closingApproved = true;
                _recoveryTimer.Stop();
                _previewTimer.Stop();
                _outlineTimer.Stop();
                _monitor.Dispose();
                _workspaceRefreshTimer.Stop();
                _workspaceChanges.Dispose();
                Close();
            });
            return new(CommandOutcome.Completed, "Version restart scheduled.");
        }
        catch (Exception exception)
        {
            ReportError("portable.update.failed", exception, null);
            await ChoiceDialog.ShowAsync(this, "Version not switched", exception.Message, "OK");
            return new(CommandOutcome.Failed, exception.Message, exception);
        }
    }
}
