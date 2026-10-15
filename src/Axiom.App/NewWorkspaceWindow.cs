using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Axiom.Core;

namespace Axiom.App;

internal sealed class NewWorkspaceWindow : Window
{
    internal NewWorkspaceWindow(string initialLocation, Func<string, string, Task<CommandResult>> create)
    {
        Title = "New Workspace"; Width = 620; SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var name = new TextBox { Name = "WorkspaceName", Text = "My Workspace", PlaceholderText = "Workspace name" };
        var location = new TextBox { Name = "WorkspaceLocation", Text = initialLocation };
        Avalonia.Automation.AutomationProperties.SetName(name, "Workspace name");
        Avalonia.Automation.AutomationProperties.SetName(location, "Workspace location");
        var status = new TextBlock { Name = "WorkspaceCreationStatus", Text = "Create a folder for your Markdown documents and open it as a workspace.", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var browse = new Button { Name = "WorkspaceBrowse", Content = "Browse…" };
        var confirm = new Button { Name = "WorkspaceCreate", Content = "Create Workspace", IsDefault = true };
        var cancel = new Button { Name = "WorkspaceCancel", Content = "Cancel", IsCancel = true };
        browse.Click += async (_, _) =>
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Location for the new workspace", AllowMultiple = false });
            if (folders.FirstOrDefault()?.TryGetLocalPath() is { } path) location.Text = path;
        };
        confirm.Click += async (_, _) =>
        {
            confirm.IsEnabled = browse.IsEnabled = cancel.IsEnabled = name.IsEnabled = location.IsEnabled = false;
            try
            {
                var result = await create(location.Text ?? "", name.Text ?? "");
                if (result.Outcome is CommandOutcome.Completed or CommandOutcome.Cancelled) Close(result);
                else status.Text = result.Message;
            }
            catch (Exception error) { status.Text = error.Message; }
            finally { confirm.IsEnabled = browse.IsEnabled = cancel.IsEnabled = name.IsEnabled = location.IsEnabled = true; }
        };
        cancel.Click += (_, _) => Close(null);
        Content = new StackPanel { Margin = new Thickness(20), Spacing = 12, Children =
        {
            status, new TextBlock { Text = "Workspace name" }, name,
            new TextBlock { Text = "Create inside this folder" }, location, browse,
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { confirm, cancel } }
        } };
        Opened += (_, _) => { name.Focus(); name.SelectAll(); };
    }
}
