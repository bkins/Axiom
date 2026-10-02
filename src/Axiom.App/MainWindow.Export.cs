using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Axiom.Core;
using Axiom.Infrastructure;

namespace Axiom.App;

public partial class MainWindow
{
    private bool _exportRunning;

    private async void Export_OnClick(object? sender, RoutedEventArgs args)
    {
        var extension = (sender as MenuItem)?.Tag as string ?? "html";
        await ExecuteCommandAsync("document.export." + extension);
    }

    private async Task<CommandResult> ExportCurrentBufferAsync(string extension, CancellationToken cancellationToken)
    {
        if (_exportRunning) return new(CommandOutcome.Unavailable, "Another export is running.");
        _exportRunning = true;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var snapshot = new ExportSnapshot(Editor.Text ?? string.Empty, _session.Path);
        var pageSize = PdfPageSize.Letter;
        try
        {
            if (extension == "pdf")
            {
                var choice = await ChoiceDialog.ShowAsync(this, "PDF page size", "Export the current buffer as a paginated PDF. Choose portrait page size.", "Letter", "A4", "Cancel");
                if (choice is not "Letter" and not "A4") return new(CommandOutcome.Cancelled, "PDF page selection cancelled.");
                pageSize = choice == "A4" ? PdfPageSize.A4 : PdfPageSize.Letter;
            }
            var selected = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export current buffer (Markdown remains unsaved)"
              ,
                SuggestedFileName = Path.GetFileNameWithoutExtension(snapshot.SourcePath ?? "Untitled.md") + "." + extension
              ,
                DefaultExtension = extension
              ,
                ShowOverwritePrompt = false
              ,
                FileTypeChoices = [new FilePickerFileType(extension switch { "docx" => "Word document", "txt" => "Plain text", "pdf" => "PDF document", "epub" => "EPUB book", "odt" => "OpenDocument Text", _ => "Standalone HTML" }) { Patterns = ["*." + extension] }]
            });
            if (selected?.TryGetLocalPath() is not { } path) return new(CommandOutcome.Cancelled, "Export destination cancelled.");
            var replace = File.Exists(path);
            if (replace && await ChoiceDialog.ShowAsync(this, "Replace export", "Replace this output file?\n" + path, "Replace", "Cancel") != "Replace") return new(CommandOutcome.Cancelled, "Existing export retained.");
            ExportProgressPanel.IsVisible = true;
            ExportCancelRequested = () => cancellation.Cancel();
            var result = await PdfExportServiceFactory.Create(_operationalPaths.Root).WriteAsync(snapshot, path, replace,
                _tabs.Select(tab => tab.Session.Path).OfType<string>(), _operationalPaths.Root, cancellation.Token, pageSize);
            UpdateChrome("Exported " + result.Path);
            if (result.Warnings.Count > 0) await ChoiceDialog.ShowAsync(this, "Export completed with warnings", string.Join("\n", result.Warnings), "Close");
            return new(CommandOutcome.Completed, "Exported " + result.Path);
        }
        catch (OperationCanceledException) { UpdateChrome("Export cancelled; existing output preserved."); return new(CommandOutcome.Cancelled, "Export cancelled."); }
        catch (Exception exception) { ReportError("document.export.failed", exception, snapshot.SourcePath); return new(CommandOutcome.Failed, exception.Message, exception); }
        finally { ExportCancelRequested = null; ExportProgressPanel.IsVisible = false; _exportRunning = false; }
    }

    private Action? ExportCancelRequested { get; set; }
    private async void ExportCancel_OnClick(object? sender, RoutedEventArgs args) => await ExecuteCommandAsync("document.cancelExport");
}
