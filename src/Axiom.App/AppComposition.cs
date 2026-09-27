using Axiom.Core;
using Axiom.Infrastructure;
using Avalonia;
using Avalonia.Styling;

namespace Axiom.App;

internal static class AppComposition
{
    private static AppDataPaths? _paths;
    private static RollingFileLog? _log;
    private static SqliteStateStore? _state;
    private static string? _sessionId;

    public static MainWindow CreateMainWindow()
    {
        _paths = new AppDataPaths();
        _paths.EnsureCreated();
        _log = new RollingFileLog(_paths.LogDirectory);
        _state = new SqliteStateStore(_paths, _log);
        _state.InitializeAsync().GetAwaiter().GetResult();
        var appearance = _state.GetAppearanceAsync().GetAwaiter().GetResult();
        if (Application.Current is not null)
            Application.Current.RequestedThemeVariant = appearance switch
            {
                AppearancePreference.Light => ThemeVariant.Light,
                AppearancePreference.Dark => ThemeVariant.Dark,
                _ => ThemeVariant.Default
            };
        _sessionId = _state.BeginSessionAsync().GetAwaiter().GetResult();
        _log.Information("application.started");

        var files = new FileDocumentStore(_log);
        var documents = new DocumentService(files, _state, _state, _log);
        var workspaces = new WorkspaceService(_state);
        return new MainWindow(documents, workspaces, files, _state, _state, appearance, new MarkdownPreviewService(), new ExternalFileMonitor(), _log, MarkCleanExitAsync);
    }

    private static async Task MarkCleanExitAsync()
    {
        if (_state is not null && _sessionId is not null)
            await _state.EndSessionAsync(_sessionId, true);
        _log?.Information("application.stopped");
    }
}

