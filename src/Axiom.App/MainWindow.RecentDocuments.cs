using Avalonia.Interactivity;
using Axiom.Core;

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
        => await ExecuteCommandAsync("document.recent");

    private async Task<CommandResult> ShowRecentDocumentsAsync()
    {
        if (ManagementState is null) return new(CommandOutcome.Unavailable, "Recent document storage unavailable.");
        try
        {
            var choice = await new RecentDocumentsWindow(ManagementState, await ManagementState.GetRecentDocumentsAsync(),
                () => _ = ShowHelpAsync("Recent documents")).ShowDialog<Axiom.Core.RecentDocument?>(this);
            if (choice is null) return new(CommandOutcome.Cancelled, "Recent document selection cancelled.");
            if (!File.Exists(choice.Path)) { UpdateChrome("Recent document is missing or unavailable: " + choice.Path + ". Remove it from history or restore the file."); return new(CommandOutcome.Unavailable, "Recent document unavailable."); }
            if (choice.WorkspaceRoot is { } root)
            {
                if (!Directory.Exists(root)) { UpdateChrome("Recent document workspace is unavailable: " + root); return new(CommandOutcome.Unavailable, "Recent workspace unavailable."); }
                if (!await SwitchWorkspaceAsync(root)) return new(CommandOutcome.Cancelled, "Workspace switch cancelled.");
            }
            await OpenDocumentAsync(choice.Path);
            return new(CommandOutcome.Completed, "Recent document selection reviewed.");
        }
        catch (Exception exception) { ReportError("recent.open.failed", exception, null); return new(CommandOutcome.Failed, exception.Message, exception); }
    }
}
