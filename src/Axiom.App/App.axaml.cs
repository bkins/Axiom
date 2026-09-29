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
