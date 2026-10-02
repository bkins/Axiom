using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Axiom.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = AppComposition.CreateMainWindow();
            if (WorkbenchProbe.Root is not null)
            {
                var window = (MainWindow)desktop.MainWindow;
                window.Opened += async (_, _) => await WorkbenchProbe.StartAsync(window);
            }
            if (CommandWorkflowProbe.Root is not null)
            {
                var window = (MainWindow)desktop.MainWindow;
                window.Opened += async (_, _) => await CommandWorkflowProbe.StartAsync(window);
            }
            if (InstallationWindowProbe.Root is not null)
            {
                var window = (MainWindow)desktop.MainWindow;
                window.Opened += async (_, _) => await InstallationWindowProbe.StartAsync(window);
            }
            if (WorkspaceGitProbe.Root is not null)
            {
                var window = (MainWindow)desktop.MainWindow;
                window.Opened += async (_, _) => await WorkspaceGitProbe.StartAsync(window);
            }
            if (StructuredWikiProbe.Root is not null)
            {
                var window = (MainWindow)desktop.MainWindow;
                window.Opened += async (_, _) => await StructuredWikiProbe.StartAsync(window);
            }
            if (DeletedDocumentProbe.Root is not null)
            {
                var window = (MainWindow)desktop.MainWindow;
                window.Opened += async (_, _) => await DeletedDocumentProbe.StartAsync(window);
            }
            if (DocumentHistoryProbe.Root is not null)
            {
                var window = (MainWindow)desktop.MainWindow;
                window.Opened += async (_, _) => await DocumentHistoryProbe.StartAsync(window);
            }
            if (ReadingFeedbackProbe.Root is not null)
            {
                var window = (MainWindow)desktop.MainWindow;
                window.Opened += async (_, _) => await ReadingFeedbackProbe.StartAsync(window);
            }
            if (TemplateAssetProbe.Root is not null)
            {
                var window = (MainWindow)desktop.MainWindow;
                window.Opened += async (_, _) => await TemplateAssetProbe.StartAsync(window);
            }
            if (PdfExportProbe.Root is not null)
            {
                var window = (MainWindow)desktop.MainWindow;
                window.Opened += async (_, _) => await PdfExportProbe.StartAsync(window);
            }
            if (EverydayUseProbe.Root is not null)
            {
                var window = (MainWindow)desktop.MainWindow;
                window.Opened += async (_, _) => await EverydayUseProbe.StartAsync(window);
            }
            if (MultiBufferRecoveryProbe.Root is not null)
            {
                var window = (MainWindow)desktop.MainWindow;
                window.Opened += async (_, _) => await MultiBufferRecoveryProbe.StartAsync(window);
            }
            if (ConnectedDocumentsProbe.Root is not null)
            {
                var window = (MainWindow)desktop.MainWindow;
                window.Opened += async (_, _) => await ConnectedDocumentsProbe.StartAsync(window);
            }
        }
        base.OnFrameworkInitializationCompleted();
    }
}
