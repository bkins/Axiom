using Avalonia.Interactivity;

namespace Axiom.App;

public partial class MainWindow
{
    private async Task RecordRecentOpenAsync(string path)
    {
        if (ManagementState is null || _session.Path is null || !Path.GetFullPath(_session.Path).Equals(Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)) return;
        try { await ManagementState.RecordRecentDocumentAsync(path, _activeWorkspace?.RootPath); }
        catch (Exception exception) { ReportError("recent.record.failed", exception, path); }
    }

    private async void RecentDocuments_OnClick(object? sender, RoutedEventArgs args)
    {
        if (ManagementState is null) return;
        try
        {
            var choice = await new RecentDocumentsWindow(ManagementState, await ManagementState.GetRecentDocumentsAsync(),
                () => _ = ShowHelpAsync("Recent documents")).ShowDialog<Axiom.Core.RecentDocument?>(this);
            if (choice is null) return;
            if (!File.Exists(choice.Path)) { UpdateChrome("Recent document is missing or unavailable: " + choice.Path + ". Remove it from history or restore the file."); return; }
            if (choice.WorkspaceRoot is { } root)
            {
                if (!Directory.Exists(root)) { UpdateChrome("Recent document workspace is unavailable: " + root); return; }
                if (!await SwitchWorkspaceAsync(root)) return;
            }
            await OpenDocumentAsync(choice.Path);
        }
        catch (Exception exception) { ReportError("recent.open.failed", exception, null); }
    }
}
