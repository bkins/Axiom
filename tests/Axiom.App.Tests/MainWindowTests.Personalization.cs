using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Axiom.Core;
using Xunit;

namespace Axiom.App.Tests;

public sealed partial class MainWindowTests
{
    [AvaloniaFact]
    public async Task ZenEnteredFromReadingModeRetainsPreviousSplitWhenEditingResumes()
    {
        var root = CreateTabWorkspace();
        try
        {
            var window = CreateWindow(root);
            var layout = window.FindControl<Grid>("EditorPreviewLayout")!;
            layout.Children.Remove(window.FindControl<NativeWebView>("Preview")!);
            window.Show(); await window.StartupCompleted;
            var previous = layout.ColumnDefinitions.Select(column => column.Width).ToArray();
            await window.ToggleReadingModeAsync();
            await window.ExecuteCommandAsync("view.mode.zen");
            Assert.Equal(WorkbenchViewMode.Zen, window.ViewModeForTesting);
            await window.ExecuteCommandAsync("view.mode.standard");
            Assert.True(window.ReadingModeForTesting);
            await window.ToggleReadingModeAsync();
            Assert.Equal(previous, layout.ColumnDefinitions.Select(column => column.Width).ToArray());
            window.Close();
        }
        finally { Directory.Delete(root,true); }
    }
}
