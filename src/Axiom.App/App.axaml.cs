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
            if (PreviewResilienceProbe.Root is not null)
            {
                var window = (MainWindow)desktop.MainWindow;
                window.RecoveryChoiceForTesting = _ => Task.FromResult("Restore");
                window.Opened += async (_, _) => await PreviewResilienceProbe.StartAsync(window);
            }
            if (AuthoringPolishProbe.Root is not null)
            {
                var window = (MainWindow)desktop.MainWindow;
                window.Opened += async (_, _) => await AuthoringPolishProbe.StartAsync(window);
            }
            if (WindowsInteractionProbe.Root is not null)
            {
                var window = (MainWindow)desktop.MainWindow;
                window.Opened += async (_, _) => await WindowsInteractionProbe.StartAsync(window);
            }
            if (PersonalizationProbe.Root is not null)
            {
                var window = (MainWindow)desktop.MainWindow;
                window.Opened += async (_, _) => await PersonalizationProbe.StartAsync(window);
            }
            _activationWindow = (MainWindow)desktop.MainWindow;
            _activationWindow.Opened += (_, _) => DrainActivations();
            _activationWindow.Closed += (_, _) => _activationWindow = null;
            if (ExternalOpeningProbe.Root is not null)
            {
                var window = (MainWindow)desktop.MainWindow;
                window.Opened += async (_, _) => await ExternalOpeningProbe.StartAsync(window);
            }
            if (CompactWorkbenchProbe.Root is not null)
            {
                var window = (MainWindow)desktop.MainWindow;
                window.Opened += async (_, _) => await CompactWorkbenchProbe.StartAsync(window);
            }
            if (DailyAuthoringProbe.Root is not null)
            {
                var window = (MainWindow)desktop.MainWindow;
                window.Opened += async (_, _) => await DailyAuthoringProbe.StartAsync(window);
            }
            if (DiscoveryTerminalProbe.Root is not null)
            {
                var window = (MainWindow)desktop.MainWindow;
                window.Opened += async (_, _) => await DiscoveryTerminalProbe.StartAsync(window);
            }
            if (CpHelpProbe.Root is not null)
            {
                var window = (MainWindow)desktop.MainWindow;
                window.Opened += async (_, _) => await CpHelpProbe.StartAsync(window);
            }
            if (SemanticReviewProbe.Root is not null)
            {
                var window = (MainWindow)desktop.MainWindow;
                window.Opened += async (_, _) => await SemanticReviewProbe.StartAsync(window);
            }
            if (TurnkeyCpProbe.Root is not null)
            {
                var window = (MainWindow)desktop.MainWindow;
                window.Opened += async (_, _) => await TurnkeyCpProbe.StartAsync(window);
            }
            if (CreationProbe.Root is not null)
            {
                var window = (MainWindow)desktop.MainWindow;
                window.Opened += async (_, _) => await CreationProbe.StartAsync(window);
            }
            if (SemanticLayoutProbe.Root is not null)
            {
                var window = (MainWindow)desktop.MainWindow;
                window.Opened += async (_, _) => await SemanticLayoutProbe.StartAsync(window);
            }
            if (WorkspaceNavigationProbe.Root is not null)
            {
                var window = (MainWindow)desktop.MainWindow;
                window.Opened += async (_, _) => await WorkspaceNavigationProbe.StartAsync(window);
            }
            if (UsageReliabilityProbe.Root is not null)
            {
                var window = (MainWindow)desktop.MainWindow;
                window.Opened += async (_, _) => await UsageReliabilityProbe.StartAsync(window);
            }
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
        if (AuthoringAssistanceProbe.Root is not null && ApplicationLifetime is IClassicDesktopStyleApplicationLifetime authoringDesktop)
        {
            var window = (MainWindow)authoringDesktop.MainWindow!;
            window.Opened += async (_, _) => await AuthoringAssistanceProbe.StartAsync(window);
        }
    }
}
