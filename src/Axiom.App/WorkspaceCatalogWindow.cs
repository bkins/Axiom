using Avalonia.Controls;
using Avalonia.Layout;
using Axiom.Core;

namespace Axiom.App;

internal sealed class WorkspaceCatalogWindow : Window
{
    public WorkspaceCatalogWindow(IReadOnlyList<WorkspaceRecord> workspaces)
    {
        Title = "Remembered workspaces";
        Width = 760;
        Height = 440;
        MinWidth = 520;
        MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var list = new ListBox { ItemsSource = workspaces.Select(workspace => $"{workspace.DisplayName} — {workspace.RootPath}{(Directory.Exists(workspace.RootPath) ? "" : " — unavailable")}").ToArray() };
        Avalonia.Automation.AutomationProperties.SetName(list, "Remembered workspace paths");
        var open = new Button { Content = "Open selected", IsDefault = true };
        var forget = new Button { Content = "Forget selected…" };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        open.Click += (_, _) => { if (list.SelectedIndex >= 0) Close((workspaces[list.SelectedIndex], false)); };
        forget.Click += (_, _) => { if (list.SelectedIndex >= 0) Close((workspaces[list.SelectedIndex], true)); };
        cancel.Click += (_, _) => Close(null);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { open, forget, cancel } };
        Content = new DockPanel
        {
            Margin = new Avalonia.Thickness(18)
                    ,
            Children =
                      {
                          new TextBlock { Text = workspaces.Count == 0 ? "No remembered workspaces. Use Open Folder to add one." : "Choose a collection by its full path.", Margin = new Avalonia.Thickness(0, 0, 0, 10) }
                        , buttons
                        , list
                      }
        };
        DockPanel.SetDock(((DockPanel)Content).Children[0], Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
    }
}
