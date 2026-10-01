using Avalonia.Controls;

namespace Axiom.App;

internal sealed class UpdateDownloadWindow : Window
{
    public UpdateDownloadWindow(Action cancel)
    {
        Title = "Download verified update";
        Width = 480;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var button = new Button { Content = "Cancel download", IsCancel = true };
        button.Click += (_, _) => { cancel(); button.IsEnabled = false; };
        Closing += (_, _) => cancel();
        Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20)
          , Spacing = 12
          , Children = { new TextBlock { Text = "Downloading and verifying the package. Nothing installs automatically." }, new ProgressBar { IsIndeterminate = true }, button }
        };
    }
}
