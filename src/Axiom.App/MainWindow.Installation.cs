using Avalonia.Interactivity;
using Axiom.Infrastructure;

namespace Axiom.App;

public partial class MainWindow
{
    private bool _installationRunning;
    private async void InstallCurrentUser_OnClick(object? sender, RoutedEventArgs eventArgs) => await InstallCurrentUserAsync();
    private async void CheckTrustedUpdate_OnClick(object? sender, RoutedEventArgs eventArgs) => await CheckTrustedUpdateAsync();

    private async Task<Axiom.Core.CommandResult> InstallCurrentUserAsync()
    {
        if (_installationRunning || _mutationBlocked) return new(Axiom.Core.CommandOutcome.Unavailable, "Finish the active installation or pending file operation first.");
        _installationRunning = true;
        try
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Per-user installation is available on Windows.");
            var packages = new PortablePackageService(_operationalPaths);
            var manifest = await packages.ValidateAsync(AppContext.BaseDirectory);
            if (await ChoiceDialog.ShowAsync(this, "Install Axiom for current user",
                $"Install {manifest.InformationalVersion} in {_operationalPaths.Root}/Versions?\n\nA verified state backup is created first. Documents and drafts are retained. Existing versions remain for rollback. Windows uninstall removes this payload and integration while retaining state/backups and other versions. No administrator access is required.", "Continue", "Cancel") != "Continue")
                return new(Axiom.Core.CommandOutcome.Cancelled, "Installation cancelled.");
            var shortcut = await ChoiceDialog.ShowAsync(this, "Start menu shortcut", "Create a Start menu shortcut for this installation?", "Create shortcut", "Skip") == "Create shortcut";
            var association = await ChoiceDialog.ShowAsync(this, "Markdown Open with registration", "Offer Axiom in Open with for .md and .markdown files? Your default editor is retained.", "Register", "Skip") == "Register";
            var destination = await new WindowsInstallationService(_operationalPaths).InstallAsync(AppContext.BaseDirectory, shortcut, association);
            await ChoiceDialog.ShowAsync(this, "Axiom installed", $"Verified installation: {destination}\n\nUse Windows Installed apps to uninstall after closing this version. This app remains running. Portable version switching and retained versions remain available.", "OK");
            return new(Axiom.Core.CommandOutcome.Completed, "Per-user installation completed.");
        }
        catch (Exception exception)
        {
            await ChoiceDialog.ShowAsync(this, "Installation incomplete", exception.Message + "\nAny verified version or backup already created is retained.", "OK");
            return new(Axiom.Core.CommandOutcome.Failed, "Installation incomplete.", exception);
        }
        finally { _installationRunning = false; }
    }

    private async Task<Axiom.Core.CommandResult> CheckTrustedUpdateAsync()
    {
        if (_installationRunning || _mutationBlocked) return new(Axiom.Core.CommandOutcome.Unavailable, "Finish the active installation or pending file operation first.");
        _installationRunning = true;
        try
        {
            var url = await TextEntryDialog.ShowAsync(this, "Trusted update offer URL", "This explicit check contacts the HTTPS URL you provide. Obtain the offer URL and its SHA-256 from an independently trusted release publisher. No automatic background checks run.", "");
            if (url is null) return new(Axiom.Core.CommandOutcome.Cancelled, "Update check cancelled.");
            var hash = await TextEntryDialog.ShowAsync(this, "Trusted offer SHA-256", "Enter the independently trusted 64-character SHA-256 for the offer JSON. A hash downloaded from the same untrusted URL does not establish publisher trust.", "");
            if (hash is null) return new(Axiom.Core.CommandOutcome.Cancelled, "Update check cancelled.");
            using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(5) };
            var service = new VerifiedUpdateService(client);
            var inspection = await new StateMaintenanceService(_operationalPaths).InspectAsync();
            if (inspection.Integrity != "ok" || inspection.PendingOperations > 0) throw new IOException("Resolve state integrity or pending file operations before updating.");
            var offer = await service.CheckAsync(new Uri(url.Trim()), hash.Trim(), _buildIdentity.ProductVersion, inspection.SchemaVersion);
            if (await ChoiceDialog.ShowAsync(this, "Verified update offer", $"Version: {offer.Version}+{offer.CommitId}\nChannel: {offer.Channel}\nSchema: {offer.SchemaVersion}\n\n{offer.ReleaseNotes}\n\nDownload this verified package? Existing files and this running version remain unchanged.", "Download", "Cancel") != "Download")
                return new(Axiom.Core.CommandOutcome.Cancelled, "Update download cancelled.");
            using var cancellation = new CancellationTokenSource();
            var progress = new UpdateDownloadWindow(() => cancellation.Cancel());
            progress.Show(this);
            string package;
            try { package = await service.DownloadPackageAsync(offer, Path.Combine(_operationalPaths.Root, "Downloads"), cancellation.Token); }
            finally { progress.Close(); }
            await ChoiceDialog.ShowAsync(this, "Verified download ready", $"Verified package: {package}\n\nChoose Tools → Install Portable Update, then its payload-manifest.json, for the existing review, schema check, explicit installation and restart workflow. No files were installed automatically.", "OK");
            return new(Axiom.Core.CommandOutcome.Completed, "Verified update downloaded for explicit installation.");
        }
        catch (OperationCanceledException) { return new(Axiom.Core.CommandOutcome.Cancelled, "Download cancelled; nothing installed."); }
        catch (Exception exception)
        {
            await ChoiceDialog.ShowAsync(this, "Update not installed", exception.Message, "OK");
            return new(Axiom.Core.CommandOutcome.Failed, "Update not installed.", exception);
        }
        finally { _installationRunning = false; }
    }
}
