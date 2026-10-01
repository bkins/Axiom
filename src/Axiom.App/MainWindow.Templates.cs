using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Axiom.Core;
using Axiom.Infrastructure;

namespace Axiom.App;

public partial class MainWindow
{
    private async void CreateFromTemplate_OnClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (_activeWorkspace is not { } workspace || ManagementState is null || _mutationBlocked ||
            _sessionTransitionRunning || _navigationRunning)
        {
            UpdateChrome("Open a workspace and resolve pending operations before creating a document.");
            return;
        }
        _sessionTransitionRunning = true;
        SetSessionCommandsEnabled(false);
        try
        {
            var choice = await ChoiceDialog.ShowAsync(this, "New workspace document", "Start with a blank document or select a reusable Markdown template. Templates stay in a folder you own; Axiom does not modify them.", "Blank", "Choose template", "Cancel");
            if (choice == "Cancel") return;
            var template = "";
            if (choice == "Choose template")
            {
                var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Choose a Markdown template — {{title}} and {{date}} placeholders"
                  , AllowMultiple = false
                  , FileTypeFilter = [new FilePickerFileType("Markdown templates") { Patterns = ["*.md", "*.markdown"] }]
                });
                if (files.FirstOrDefault()?.TryGetLocalPath() is not { } templatePath) return;
                var bytes = await new WorkspaceImportService(new WorkspaceFileOperations(ManagementState)).ReviewAsync(templatePath, false);
                using var stream = new MemoryStream(bytes);
                using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                template = await reader.ReadToEndAsync();
            }
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "New document — choose an existing folder inside this workspace"
              , AllowMultiple = false
              , SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(workspace.RootPath)
            });
            if (folders.FirstOrDefault()?.TryGetLocalPath() is not { } folder) return;
            var name = await TextEntryDialog.ShowAsync(this, "Document name", "Enter a Markdown filename. Existing files are never overwritten.", "Untitled.md");
            if (name is null) return;
            if (name.Contains('/') || name.Contains('\\')) throw new ArgumentException("Enter a filename without folders.");
            var relative = Path.GetRelativePath(workspace.RootPath, Path.Combine(folder, name));
            var destination = WorkspaceMutationPolicy.Resolve(workspace.RootPath, relative);
            WorkspaceFileOperations.ValidateAncestors(workspace.RootPath, destination);
            var text = DocumentTemplateService.Expand(template, Path.GetFileNameWithoutExtension(name), DateOnly.FromDateTime(DateTime.Today));
            var preview = new DocumentCreationPreviewWindow(destination, text);
            if (!await preview.ShowDialog<bool>(this)) return;
            await CreateReviewedDocumentAsync(workspace, relative, text);
            UpdateChrome($"Created {relative} from the reviewed text.");
        }
        catch (Exception exception)
        {
            _mutationBlocked = (await ManagementState.GetPendingMutationsAsync()).Count > 0;
            ReportError("template.create.failed", exception, workspace.RootPath);
            await ChoiceDialog.ShowAsync(this, "Document not created", exception.Message, "OK");
        }
        finally { _sessionTransitionRunning = false; SetSessionCommandsEnabled(true); }
    }

    internal async Task CreateReviewedDocumentAsync(WorkspaceRecord workspace, string relative, string text)
    {
        if (ManagementState is null || _mutationBlocked) throw new IOException("Resolve pending file operations before creating a document.");
        var path = await new WorkspaceFileOperations(ManagementState, log: _log).ExecuteAsync(
            workspace, DocumentMutationKind.Create, null, relative, creationText: text);
        await LoadFolderAsync(workspace.RootPath);
        if (path is not null) await OpenDocumentAsync(path);
    }
}
