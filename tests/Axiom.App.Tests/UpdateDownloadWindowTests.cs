using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Xunit;

namespace Axiom.App.Tests;

public sealed class UpdateDownloadWindowTests
{
    [AvaloniaFact]
    public void CheckingIsVisibleBeforeWorkAndDownloadReportsPercentageAndCancel()
    {
        var canceled = false;
        var window = new UpdateDownloadWindow(() => canceled = true, "Axiom updates", "Checking for updates…");
        window.Show();
        Assert.True(window.IsVisible);
        Assert.Equal("Checking for updates…", window.MessageForTesting);
        Assert.True(window.IsIndeterminateForTesting);
        window.SetProgress(new("Downloading Axiom 1.7.3… 37%", 37));
        Assert.False(window.IsIndeterminateForTesting);
        Assert.Equal(37, window.PercentageForTesting);
        Assert.Contains("37%", window.MessageForTesting);
        var cancel = window.GetLogicalDescendants().OfType<Button>().Single();
        cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(canceled);
        Assert.False(cancel.IsEnabled);
        window.Close();
        Assert.True(window.WasClosed);
    }

    [AvaloniaFact]
    public void ClosingTheProgressWindowCancelsTheOperation()
    {
        var canceled = false;
        var window = new UpdateDownloadWindow(() => canceled = true, "Axiom updates", "Checking for updates…");
        window.Show();
        Assert.False(canceled);
        window.Close();
        Assert.True(canceled);
        Assert.True(window.WasClosed);
    }
}
