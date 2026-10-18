using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Xunit;

namespace Axiom.App.Tests;

public sealed partial class MainWindowTests
{
    [AvaloniaFact]
    public async Task CompactFileActionsAndHelpRetainUnsavedTextAndTargetLabels()
    {
        var root = CreateTabWorkspace();
        try
        {
            var path = Path.Combine(root, "sample.md");
            await File.WriteAllTextAsync(path, "# Saved");
            var window = CreateWindow(root);
            await window.LoadFolderAsync(root);
            await window.OpenDocumentAsync(path);
            window.SetEditorTextForTesting("# Unsaved UI review");
            var menu = window.WorkspaceContextMenuForTesting("sample.md", false);
            var labels = menu.Items.OfType<MenuItem>().Select(item => item.Header?.ToString()).ToArray();
            Assert.Contains("New folder…", labels);
            Assert.Contains("Pin", labels);
            Assert.Contains("Copy", labels);
            await window.ExecuteExplorerCommandForTestingAsync("workspace.pinItem", "sample.md");
            Assert.Contains(window.WorkspaceContextMenuForTesting("sample.md", false).Items.OfType<MenuItem>(), item => Equals(item.Header, "Unpin"));
            Assert.Single(labels, label => label == "Show in Explorer");
            Assert.DoesNotContain(labels, label => label?.Contains("selected") == true || label?.Contains("CreateFolder") == true);
            var contextBar = window.FindControl<Border>("WorkspaceContextBar")!;
            var mainBar = window.FindControl<Border>("TopCommandBar")!;
            var dock = Assert.IsType<DockPanel>(contextBar.Parent);
            Assert.Same(mainBar.Parent, contextBar.Parent);
            Assert.Equal(dock.Children.IndexOf(mainBar) + 1, dock.Children.IndexOf(contextBar));
            Assert.Equal(Dock.Top, DockPanel.GetDock(contextBar));
            var format = window.EditorContextMenuForTesting().Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Format"));
            Assert.Contains(format.Items.OfType<MenuItem>(), item => Equals(item.Header, "Bold"));
            Assert.Contains(format.Items.OfType<MenuItem>(), item => Equals(item.Header, "Strikethrough"));
            Assert.DoesNotContain(format.Items.OfType<MenuItem>(), item => item.Header?.ToString()?.StartsWith("Format ") == true);
            var actions = window.FindControl<WrapPanel>("WorkspaceContextActions")!;
            Assert.False(actions.IsVisible);
            window.FindControl<Button>("WorkspaceToolbarToggle")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(actions.IsVisible);
            await window.ShowPersistentHelpForTestingAsync(null);
            var help = window.FindControl<Border>("HelpPanelHost")!;
            var rail = window.FindControl<Border>("ToolWindowRail")!;
            Assert.Same(help.Parent, rail.Parent);
            Assert.Equal(4, Grid.GetColumn(help));
            Assert.Equal(5, Grid.GetColumn(rail));
            Assert.True(help.IsVisible);
            Assert.Equal("# Unsaved UI review", window.EditorTextForTesting);
            Assert.Equal("# Saved", await File.ReadAllTextAsync(path));
            window.Close();
        }
        finally { Directory.Delete(root, true); }
    }
}
