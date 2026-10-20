using Avalonia.Interactivity;
using Avalonia.Threading;
using Axiom.Core;
using Axiom.Infrastructure;

namespace Axiom.App;

public partial class MainWindow
{
    private readonly AutomaticUpdateService _automaticUpdates = new(value => new VelopackUpdateClient(value));
    private readonly CancellationTokenSource _automaticUpdateCancellation = new();
    private readonly DispatcherTimer _automaticUpdateTimer = new() { Interval = TimeSpan.FromHours(6) };
    private bool _restartForAutomaticUpdate;
    private CancellationTokenSource? _automaticDownloadCancellation;
    private UpdateDownloadWindow? _automaticUpdateProgressWindow;
    internal bool AutomaticUpdateRestartRequestedForTesting => _restartForAutomaticUpdate;
    internal void RequestAutomaticUpdateRestartForTesting() { _restartForAutomaticUpdate = true; Close(); }

    private AutomaticUpdatePreferencesStore UpdatePreferencesStore => new(_operationalPaths,
        Program.UpdateRuntimeInitialized && Velopack.Locators.VelopackLocator.Current.Channel == "win-x64-preview" ? "preview" : "stable");

    private void InitializeAutomaticUpdateCommands()
    {
        RegisterCommand("application.automaticUpdates", "Check for updates / restart to update", null, false,
            (_, _) => CheckAutomaticUpdatesAsync(true));
        RegisterCommand("application.updateSettings", "Configure automatic updates and release channel", null, false,
            (_, _) => ConfigureAutomaticUpdatesAsync());
        RegisterCommand("application.cancelAutomaticUpdate", "Cancel update download", null, false, (_, _) =>
        {
            _automaticDownloadCancellation?.Cancel();
            return Task.FromResult(new CommandResult(CommandOutcome.Completed, "Update download cancellation requested."));
        }, isCancellationControl: true);
        _automaticUpdateTimer.Tick += async (_, _) => await CheckAutomaticUpdatesAsync(false);
        Closed += (_, _) =>
        {
            if (!_restartForAutomaticUpdate) return;
            try { _automaticUpdates.ApplyAndRestart(UpdatePreferencesStore.Load()); }
            catch (Exception exception) { _log.Error("updates.apply.failed", exception, null); }
        };
    }

    private void StartAutomaticUpdates()
    {
        // Review/portable payloads do not poll the network, even when preferences enable checks.
        if (!Program.UpdateRuntimeInitialized || !OperatingSystem.IsWindows() || !new VelopackUpdateClient(new AutomaticUpdatePreferences()).IsInstalled) return;
        _automaticUpdateTimer.Start();
        _ = CheckAutomaticUpdatesAsync(false);
    }

    private async Task PrepareAutomaticUpdateRestartAsync()
    {
        if (!_restartForAutomaticUpdate) return;
        if (_installationRunning || _mutationBlocked || ExportCancelRequested is not null)
            throw new IOException("Finish active operations before updating.");
        var maintenance = new StateMaintenanceService(_operationalPaths);
        var inspection = await maintenance.InspectAsync();
        if (inspection.Integrity != "ok" || inspection.PendingOperations > 0)
            throw new IOException("Resolve state integrity or pending file operations before updating.");
        Directory.CreateDirectory(_operationalPaths.BackupDirectory);
        await maintenance.ExportBackupAsync(Path.Combine(_operationalPaths.BackupDirectory,
            $"before-update-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.db"));
        if (_automaticUpdates.ReadyPreferences != UpdatePreferencesStore.Load())
            throw new IOException("Update settings changed. Check again before restarting.");
    }

    private async void AutomaticUpdates_OnClick(object? sender, RoutedEventArgs args) => await ExecuteCommandAsync("application.automaticUpdates");
    private async void AutomaticUpdateSettings_OnClick(object? sender, RoutedEventArgs args) => await ExecuteCommandAsync("application.updateSettings");
    private async void CancelAutomaticUpdate_OnClick(object? sender, RoutedEventArgs args) => await ExecuteCommandAsync("application.cancelAutomaticUpdate");

    private async Task<CommandResult> ConfigureAutomaticUpdatesAsync()
    {
        try
        {
            var current = UpdatePreferencesStore.Load();
            var channel = await ChoiceDialog.ShowAsync(this, "Update channel",
                $"Current channel: {current.Channel}.\n\nStable delivers accepted releases. Preview delivers test builds. Switching to Stable waits for a newer Stable release; it never downgrades your app or state.",
                "Stable", "Preview", "Cancel");
            if (channel == "Cancel") return new(CommandOutcome.Cancelled, "Update settings unchanged.");
            var automatic = await ChoiceDialog.ShowAsync(this, "Automatic downloads",
                "Check at startup and every six hours, then download updates in the background? Installing always requires choosing Restart to update and completing the normal unsaved-document prompts.",
                "Enable", "Manual checks only", "Cancel");
            if (automatic == "Cancel") return new(CommandOutcome.Cancelled, "Update settings unchanged.");
            UpdatePreferencesStore.Save(new(automatic == "Enable", channel.ToLowerInvariant()));
            UpdateReadyButton.IsVisible = false;
            return new(CommandOutcome.Completed, "Update settings saved. Use Check for Updates to check now.");
        }
        catch (Exception exception) { return new(CommandOutcome.Failed, "Update settings could not be saved.", exception); }
    }

    private async Task<CommandResult> CheckAutomaticUpdatesAsync(bool interactive)
    {
        try
        {
            if (!Program.UpdateRuntimeInitialized) return new(CommandOutcome.Unavailable, "Updates require an installed Axiom runtime.");
            var preferences = UpdatePreferencesStore.Load();
            if (!interactive && (!preferences.Enabled || _automaticUpdateCancellation.IsCancellationRequested || (_automaticUpdates.ReadyVersion is not null && _automaticUpdates.ReadyPreferences == preferences)))
                return new(CommandOutcome.Cancelled, "Automatic update check skipped.");
            if (_automaticDownloadCancellation is not null)
            {
                if (interactive && _automaticUpdateProgressWindow is { } running)
                {
                    if (running.WasClosed) return new(CommandOutcome.Unavailable, "Update cancellation is finishing. Check again in a moment.");
                    if (!running.IsVisible) running.Show(this);
                    running.Activate();
                }
                return new(CommandOutcome.Unavailable, "An update check is already running. Its progress window shows the current status.");
            }
            string message;
            if (_automaticUpdates.ReadyVersion is not null && _automaticUpdates.ReadyPreferences == preferences)
                message = $"Axiom {_automaticUpdates.ReadyVersion} is downloaded and ready.";
            else
            {
                UpdateReadyButton.IsVisible = false;
                using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_automaticUpdateCancellation.Token);
                _automaticDownloadCancellation = cancellation;
                var window = new UpdateDownloadWindow(cancellation.Cancel, "Axiom updates", "Checking for updates…");
                _automaticUpdateProgressWindow = window;
                if (interactive) window.Show(this);
                var progress = new Progress<AutomaticUpdateProgress>(value =>
                {
                    if (_automaticDownloadCancellation == cancellation && !cancellation.IsCancellationRequested && !window.WasClosed) window.SetProgress(value);
                });
                try { message = await _automaticUpdates.CheckAsync(preferences, cancellation.Token, progress); }
                finally
                {
                    _automaticDownloadCancellation = null;
                    _automaticUpdateProgressWindow = null;
                    window.Close();
                }
            }
            UpdateReadyButton.IsVisible = _automaticUpdates.ReadyVersion is not null;
            if (!interactive)
            {
                if (_automaticUpdates.ReadyVersion is not null) ShowInformation(message + " Open Tools → Check for Updates to restart.");
                return new(CommandOutcome.Completed, message);
            }
            if (_automaticUpdates.ReadyVersion is null)
            {
                await ChoiceDialog.ShowAsync(this, "Axiom updates", message, "Close");
                return new(CommandOutcome.Completed, message);
            }
            if (await ChoiceDialog.ShowAsync(this, "Update ready", message + "\n\nAxiom will ask you to save or discard unsaved changes. Cancel keeps this version running. Your state is backed up before installation.",
                "Restart to update", "Later") != "Restart to update") return new(CommandOutcome.Cancelled, "Update downloaded; restart deferred.");
            if (_installationRunning || _mutationBlocked || ExportCancelRequested is not null)
                return new(CommandOutcome.Unavailable, "Finish the active installation, export or pending file operation before restarting.");

            _restartForAutomaticUpdate = true;
            Close();
            return new(CommandOutcome.Completed, "Restart requested; complete the unsaved-document prompts.");
        }
        catch (OperationCanceledException) { return new(CommandOutcome.Cancelled, "Update download cancelled; current version retained."); }
        catch (Exception exception)
        {
            _log.Error("updates.check.failed", exception, null);
            const string message = "Updates could not be checked or downloaded. Axiom remains usable; try Check for Updates again later.";
            if (interactive) await ChoiceDialog.ShowAsync(this, "Update unavailable", message, "Close");
            return new(CommandOutcome.Failed, message, exception);
        }
    }
}

