using Avalonia.Controls;

namespace Axiom.App;

internal sealed class UpdateDownloadWindow : Window
{
    private readonly TextBlock _message = new();
    private readonly ProgressBar _progress = new() { IsIndeterminate = true };

    public UpdateDownloadWindow(Action cancel, string title = "Download verified update", string message = "Downloading and verifying the package. Nothing installs automatically.")
    {
        Title = title;
        _message.Text = message;
        _message.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
        Width = 480;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var button = new Button { Content = "Cancel", IsCancel = true };
        button.Click += (_, _) => { cancel(); button.IsEnabled = false; };
        Closing += (_, _) => cancel();
        Closed += (_, _) => WasClosed = true;
        Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20)
          , Spacing = 12
          , Children = { _message, _progress, button }
        };
    }

    internal void SetProgress(AutomaticUpdateProgress progress)
    {
        _message.Text = progress.Message;
        _progress.IsIndeterminate = progress.Percentage is null;
        _progress.Value = Math.Clamp(progress.Percentage ?? 0, 0, 100);
    }
    internal string MessageForTesting => _message.Text ?? "";
    internal bool WasClosed { get; private set; }
    internal bool IsIndeterminateForTesting => _progress.IsIndeterminate;
    internal double PercentageForTesting => _progress.Value;
}
