using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Axiom.Core;
using Axiom.Infrastructure;

namespace Axiom.App;

public partial class MainWindow
{
    private async void WorkspaceRenameSelected_OnClick(object? sender, RoutedEventArgs args) => await ExecuteCommandAsync("workspace.batch.rename");
    private async void WorkspaceCopyFolder_OnClick(object? sender, RoutedEventArgs args) => await ExecuteCommandAsync("workspace.batch.copy");
    private async void WorkspaceExportSelected_OnClick(object? sender, RoutedEventArgs args) => await ExecuteCommandAsync("workspace.batch.export");

    private async Task<CommandResult> RunNextWorkspaceToolAsync(string action)
    {
        if (_activeWorkspace is not { } workspace || ManagementState is null || _workspaceBatchRunning || _sessionTransitionRunning || _navigationRunning || _mutationBlocked || _exportRunning)
            return new(CommandOutcome.Unavailable, "Finish the current operation and open a workspace first.");
        var nodes = SelectedWorkspaceItems();
        if (nodes.Length == 0) { UpdateChrome("Select workspace files or folders first."); return new(CommandOutcome.Unavailable, "Select workspace items first."); }
        _workspaceBatchRunning = true;
        try
        {
            CommandResult result;
            if (action == "Rename")
            {
                var template = await TextEntryDialog.ShowAsync(this, "Rename selected items", "Use {name} for the original name and {n} for a path-sorted sequence number. Markdown extensions are kept. Example: Notes-{n}-{name}. Destinations and link repairs are reviewed before changes.", "{name}-{n}");
                result = template is null ? new(CommandOutcome.Cancelled, "Rename cancelled.") : await RenameWorkspaceSelectionAsync(nodes, template);
            }
            else if (action == "Copy folder")
            {
                if (nodes.Length != 1 || nodes[0].IsDocument) throw new IOException("Select one workspace folder to copy, including its descendants.");
                var destination = await TextEntryDialog.ShowAsync(this, "Copy workspace folder", "Enter a new workspace-relative destination. No merge or overwrite. Copy uses reviewed saved files; unsaved editor changes are not included.", nodes[0].RelativePath + " copy");
                result = destination is null ? new(CommandOutcome.Cancelled, "Copy cancelled.") : await CopyWorkspaceFolderAsync(nodes[0].RelativePath, destination);
            }
            else
            {
                var format = await ChoiceDialog.ShowAsync(this, "Bulk export format", "Open documents use captured current buffers, including unsaved edits. Other selected documents use saved content. Source Markdown is never saved. Folder selections include Markdown descendants; paths are preserved in one new output folder.", "HTML", "DOCX", "TXT", "PDF", "EPUB", "ODT", "Cancel");
                if (format == "Cancel") return new(CommandOutcome.Cancelled, "Export cancelled.");
                var parent = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose parent for new export folder", AllowMultiple = false });
                if (parent.FirstOrDefault()?.TryGetLocalPath() is not { } folder) return new(CommandOutcome.Cancelled, "Export cancelled.");
                var name = await TextEntryDialog.ShowAsync(this, "New export folder", "Enter a new folder name. Existing folders are never merged or overwritten.", "Axiom exports");
                if (name is null) return new(CommandOutcome.Cancelled, "Export cancelled.");
                if (name != Path.GetFileName(name) || name is "." or "..") throw new IOException("Enter a folder name, not a path.");
                result = await ExportWorkspaceSelectionAsync(nodes, Path.Combine(folder, name), format.ToLowerInvariant());
            }
            UpdateChrome(result.Message); return result;
        }
        catch (Exception error)
        { await ChoiceDialog.ShowAsync(this, "Workspace tool stopped", error.Message, "OK"); return new(CommandOutcome.Failed, error.Message, error); }
        finally
        { _workspaceBatchRunning = false; _mutationBlocked = new ReviewedWorkspaceMove(ManagementState).Pending || (await ManagementState.GetPendingMutationsAsync()).Count > 0; }
    }

    internal async Task<CommandResult> RenameWorkspaceSelectionAsync(WorkspaceExplorerNode[] nodes, string template)
    {
        var workspace = _activeWorkspace!;
        var plan = WorkspaceRenamePlan.Create(workspace.RootPath, nodes.Select(node => (node.RelativePath, !node.IsDocument)), template, path => File.Exists(path) || Directory.Exists(path));
        if (await ChoiceDialog.ShowAsync(this, "Review batch rename", string.Join("\n", plan.Select(item => Path.GetRelativePath(workspace.RootPath, item.Source) + " → " + Path.GetFileName(item.Destination))) + "\n\nEach item has a separate move/link-repair review. Cancellation or failure stops later items; earlier successful renames remain completed. Source order is stable and extensions are retained.", "Continue", "Cancel") != "Continue") return new(CommandOutcome.Cancelled, "Rename cancelled.");
        var completed = 0;
        foreach (var item in plan)
        {
            var result = await ReviewMoveAsync(item.Source, item.Destination, true, CancellationToken.None);
            if (result.Outcome != CommandOutcome.Completed) return new(result.Outcome, $"Renamed {completed}/{plan.Count}; remaining items not attempted. {result.Message}");
            completed++;
        }
        return new(CommandOutcome.Completed, $"Renamed {completed} items with reviewed references.");
    }

    internal async Task<CommandResult> CopyWorkspaceFolderAsync(string source, string destination)
    {
        var service = new ReviewedFolderCopy(ManagementState!);
        var review = await service.ReviewAsync(_activeWorkspace!, source, destination);
        if (await ChoiceDialog.ShowAsync(this, "Review folder copy", $"Source: {review.Source}\nDestination: {review.Destination}\n{review.Files.Count} files; {review.Directories.Count} descendant folders.\nEnrolled Markdown copies get fresh identities. Internal links stay within the copy; outgoing relative links keep original external targets. Saved files are copied; unsaved buffers and originals stay untouched.\n\n" + string.Join("\n", review.Warnings.Distinct()), "Copy", "Cancel") != "Copy") return new(CommandOutcome.Cancelled, "Folder copy cancelled.");
        await service.ApplyAsync(review);
        await RefreshWorkspaceTreeAsync();
        return new(CommandOutcome.Completed, "Folder copied and verified: " + destination);
    }

    internal async Task<CommandResult> ExportWorkspaceSelectionAsync(WorkspaceExplorerNode[] nodes, string destination, string extension)
    {
        var snapshots = new List<ExportSnapshot>(); var workspace = _activeWorkspace!;
        var included = new HashSet<string>(WorkspacePathBoundary.Comparer); long characters = 0;
        foreach (var node in nodes)
        {
            var path = WorkspaceMutationPolicy.Resolve(workspace.RootPath, node.RelativePath, node.IsDocument);
            var paths = node.IsDocument ? new[] { path } : (await _files.ListMarkdownFilesAsync(path)) is var discovered && discovered.SkippedDirectories.Count == 0 ? discovered.Files : throw new IOException("Selected folder scan is incomplete.");
            foreach (var source in paths)
            {
                if (!included.Add(source)) continue;
                if (snapshots.Count >= 1000 || new FileInfo(source).Length > 20 * 1024 * 1024) throw new IOException("Selection exceeds bulk export document/count limits.");
                ReviewedWorkspaceMove.ValidateSource(source);
                var tab = FindTab(source);
                var content = tab?.Document.Text ?? (await _files.ReadAsync(source)).Text;
                characters += content.Length;
                if (content.Length > 4 * 1024 * 1024 || characters > 16 * 1024 * 1024) throw new IOException("Selection exceeds bulk export character limits.");
                snapshots.Add(new(content, source));
            }
        }
        var service = new WorkspaceBatchExport(PdfExportServiceFactory.Create(_operationalPaths.Root), _operationalPaths.Root);
        var review = service.Review(workspace.RootPath, snapshots, destination, extension);
        if (await ChoiceDialog.ShowAsync(this, "Review selected export", "Output: " + destination + "\n" + string.Join("\n", review.Items.Select(item => Path.GetRelativePath(workspace.RootPath, item.Snapshot.SourcePath!) + " → " + item.RelativeOutput)) + "\n\nCaptured open buffers are included without saving. Publish the new folder only after all exports succeed.", "Export", "Cancel") != "Export") return new(CommandOutcome.Cancelled, "Export cancelled.");
        using var cancel = new CancellationTokenSource();
        _exportRunning = true; ExportProgressPanel.IsVisible = true; ExportCancelRequested = () => cancel.Cancel();
        try
        {
            var warnings = await service.ApplyAsync(review, new Progress<string>(message => UpdateChrome(message)), cancel.Token);
            if (warnings.Count > 0) await ChoiceDialog.ShowAsync(this, "Bulk export warnings", string.Join("\n", warnings), "Close");
            return new(CommandOutcome.Completed, $"Exported {review.Items.Count} documents to {destination}; Markdown unchanged.");
        }
        catch (OperationCanceledException) { return new(CommandOutcome.Cancelled, "Export cancelled; no output folder published and Markdown retained."); }
        finally { ExportCancelRequested = null; ExportProgressPanel.IsVisible = false; _exportRunning = false; }
    }
}
