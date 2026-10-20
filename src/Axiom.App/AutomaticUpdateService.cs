using Axiom.Core;
using Velopack;
using Velopack.Sources;

namespace Axiom.App;

internal sealed record AutomaticUpdateProgress(string Message, int? Percentage = null);

internal interface IAutomaticUpdateClient
{
    bool IsInstalled { get; }
    Task<string?> DownloadLatestAsync(CancellationToken cancellationToken, IProgress<AutomaticUpdateProgress>? progress = null);
    void ApplyAndRestart();
}

internal sealed class VelopackUpdateClient : IAutomaticUpdateClient
{
    // Public binary distribution only. No source code or credentials are published here.
    internal const string Repository = "https://github.com/bkins/Axiom-Releases";
    private readonly UpdateManager _manager;
    private VelopackAsset? _ready;

    public VelopackUpdateClient(AutomaticUpdatePreferences preferences)
    {
        _manager = new UpdateManager(new GithubSource(Repository, null, preferences.Channel == "preview"),
            new UpdateOptions { ExplicitChannel = preferences.FeedChannel, AllowVersionDowngrade = false });
    }

    internal VelopackUpdateClient(UpdateManager manager) => _manager = manager;

    public bool IsInstalled => OperatingSystem.IsWindows() && _manager.IsInstalled;

    public async Task<string?> DownloadLatestAsync(CancellationToken cancellationToken, IProgress<AutomaticUpdateProgress>? progress = null)
    {
        _ready = null;
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new("Checking for updates…"));
        var update = await _manager.CheckForUpdatesAsync();
        cancellationToken.ThrowIfCancellationRequested();
        if (update is null) return null;
        progress?.Report(new($"Downloading Axiom {update.TargetFullRelease.Version}…", 0));
        await _manager.DownloadUpdatesAsync(update, value => progress?.Report(new(
            value >= 100 ? "Verifying downloaded update…" : $"Downloading Axiom {update.TargetFullRelease.Version}… {value}%", value)), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        _ready = update.TargetFullRelease;
        return _ready.Version.ToString();
    }

    public void ApplyAndRestart() => _manager.ApplyUpdatesAndRestart(
        _ready ?? throw new InvalidOperationException("Check and download an update first."));
}

internal sealed class AutomaticUpdateService(Func<AutomaticUpdatePreferences, IAutomaticUpdateClient> createClient)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IAutomaticUpdateClient? _readyClient;
    public string? ReadyVersion { get; private set; }
    public AutomaticUpdatePreferences? ReadyPreferences { get; private set; }

    public async Task<string> CheckAsync(AutomaticUpdatePreferences preferences, CancellationToken cancellationToken, IProgress<AutomaticUpdateProgress>? progress = null)
    {
        _ = preferences.FeedChannel;
        if (!await _gate.WaitAsync(0, cancellationToken)) return "An update check is already running.";
        try
        {
            ReadyVersion = null;
            ReadyPreferences = null;
            _readyClient = null;
            var client = createClient(preferences);
            if (!client.IsInstalled) return "Automatic updates require the update-enabled Axiom installer. This portable review build is unchanged.";
            var version = await client.DownloadLatestAsync(cancellationToken, progress);
            if (version is null) return $"No newer update is available on {preferences.Channel}.";
            cancellationToken.ThrowIfCancellationRequested();
            _readyClient = client;
            ReadyPreferences = preferences;
            ReadyVersion = version;
            return $"Axiom {version} is downloaded. Choose Restart to update when convenient.";
        }
        finally { _gate.Release(); }
    }

    public void ApplyAndRestart(AutomaticUpdatePreferences current)
    {
        if (_readyClient is null || ReadyPreferences != current) throw new InvalidOperationException("Update settings changed. Check for updates again before restarting.");
        _readyClient.ApplyAndRestart();
    }
}
