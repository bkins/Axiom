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
        if (_exportRunning) return;
        _exportRunning = true;
        using var cancellation = new CancellationTokenSource();
        var snapshot = new ExportSnapshot(Editor.Text ?? string.Empty, _session.Path);
        var extension = (sender as MenuItem)?.Tag as string ?? "html";
        try
        {
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
                FileTypeChoices = [new FilePickerFileType(extension == "docx" ? "Word document" : "Standalone HTML") { Patterns = ["*." + extension] }]
            });
            if (selected?.TryGetLocalPath() is not { } path) return;
            var replace = File.Exists(path);
            if (replace && await ChoiceDialog.ShowAsync(this, "Replace export", "Replace this output file?\n" + path, "Replace", "Cancel") != "Replace") return;
            ExportProgressPanel.IsVisible = true;
            ExportCancelRequested = () => cancellation.Cancel();
            var result = await new DocumentExportService().WriteAsync(snapshot, path, replace,
                _tabs.Select(tab => tab.Session.Path).OfType<string>(), _operationalPaths.Root, cancellation.Token);
            UpdateChrome("Exported " + result.Path);
            if (result.Warnings.Count > 0) await ChoiceDialog.ShowAsync(this, "Export completed with warnings", string.Join("\n", result.Warnings), "Close");
        }
        catch (OperationCanceledException) { UpdateChrome("Export cancelled; existing output preserved."); }
        catch (Exception exception) { ReportError("document.export.failed", exception, snapshot.SourcePath); }
        finally { ExportCancelRequested = null; ExportProgressPanel.IsVisible = false; _exportRunning = false; }
    }

    private Action? ExportCancelRequested { get; set; }
    private void ExportCancel_OnClick(object? sender, RoutedEventArgs args) => ExportCancelRequested?.Invoke();
}
