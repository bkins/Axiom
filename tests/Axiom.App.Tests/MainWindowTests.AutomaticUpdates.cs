using Avalonia.Headless.XUnit;
using Avalonia.Controls;
using Avalonia.Threading;
using Xunit;

namespace Axiom.App.Tests;

public sealed partial class MainWindowTests
{
    [AvaloniaFact]
    public async Task CancelingDirtyTabCloseDisarmsUpdateAndRetainsTheDocument()
    {
        var root = CreateTabWorkspace();
        try
        {
            var path = Path.Combine(root, "update-cancel.md");
            await File.WriteAllTextAsync(path, "saved");
            var window = CreateWindow(root);
            var preview = window.FindControl<Control>("Preview")!;
            ((Panel)preview.Parent!).Children.Remove(preview); // Native WebView2 needs a real STA desktop, not the headless runner.
            window.Show();
            await window.StartupCompleted;
            await window.OpenDocumentAsync(path);
            window.SetEditorTextForTesting("unsaved changes");
            window.UnsavedChoiceForTesting = _ => Task.FromResult("Cancel");
            window.RequestAutomaticUpdateRestartForTesting();
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            Assert.False(window.AutomaticUpdateRestartRequestedForTesting);
            Assert.True(window.IsVisible);
            Assert.Equal("unsaved changes", window.EditorTextForTesting);
            Assert.Equal("saved", await File.ReadAllTextAsync(path));
            window.UnsavedChoiceForTesting = _ => Task.FromResult("Discard");
            await window.ConfirmAllTabsForTestingAsync();
            window.Close();
        }
        finally { Directory.Delete(root, true); }
    }
}
