using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Axiom.App;
using Axiom.Core;
using Axiom.Infrastructure;
using Xunit;

namespace Axiom.App.Tests;

public sealed partial class MainWindowTests
{
    [AvaloniaFact]
    public void Workspace_Free_Start_Actions_Have_Visible_Clickable_Bounds()
    {
        var root = CreateTabWorkspace();
        try
        {
            var window = CreateWindow(root);
            window.Show(); window.UpdateLayout();
            var actions = window.FindControl<StackPanel>("WorkspaceStartActions")!;
            Assert.True(actions.IsEffectivelyVisible);
            Assert.True(actions.Bounds.Width > 100);
            Assert.Equal(3, actions.Children.Count);
            foreach (var button in actions.Children.OfType<Button>())
            {
                Assert.True(button.Bounds.Width > 0);
                Assert.True(button.Bounds.Height > 0);
            }
            window.Close();
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public async Task New_Document_Without_Workspace_Shows_Editor_And_Preserves_Existing_Draft()
    {
        var root = CreateTabWorkspace();
        try
        {
            var window = CreateWindow(root);
            window.SetEditorTextForTesting("Existing unsaved work");
            await window.ToggleReadingModeAsync();
            Assert.True(window.ReadingModeForTesting);

            var result = await window.ExecuteCommandAsync("document.new");

            Assert.Equal(CommandOutcome.Completed, result.Outcome);
            Assert.Null(window.ActiveWorkspaceRootForTesting);
            Assert.False(window.ReadingModeForTesting);
            Assert.Contains(window.TabsForTesting, tab => tab.Session.Text == "Existing unsaved work" && tab.Session.IsDirty);
            Assert.Null(window.TabsForTesting.Last().Session.Path);
            window.SetEditorTextForTesting("# Standalone draft\nReady for CP testing.");
            Assert.Contains("Standalone draft", window.EditorTextForTesting);
            window.Close();
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public async Task Create_Workspace_Opens_And_Remembers_New_Folder_While_Cancelled_Switch_Preserves_Dirty_Work()
    {
        var root = CreateTabWorkspace();
        try
        {
            var window = CreateWindow(root);
            Assert.Equal(CommandOutcome.Completed, (await window.CreateWorkspaceAsync(root, "My new workspace")).Outcome);
            Assert.Equal(Path.Combine(root, "My new workspace"), window.ActiveWorkspaceRootForTesting);
            var state = new SqliteStateStore(new AppDataPaths(Path.Combine(root, "state")));
            Assert.Contains(await state.ListWorkspacesAsync(), workspace => workspace.RootPath == Path.Combine(root, "My new workspace"));
            window.SetEditorTextForTesting("Protected unsaved draft");
            window.UnsavedChoiceForTesting = _ => Task.FromResult("Cancel");

            var result = await window.CreateWorkspaceAsync(root, "Another workspace");

            Assert.Equal(CommandOutcome.Cancelled, result.Outcome);
            Assert.Contains("Created folder", result.Message);
            Assert.Equal(Path.Combine(root, "My new workspace"), window.ActiveWorkspaceRootForTesting);
            Assert.Equal("Protected unsaved draft", window.EditorTextForTesting);
            Assert.True(Directory.Exists(Path.Combine(root, "Another workspace")));
            window.Close();
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public void Cancel_Workspace_Dialog_Does_Not_Call_Creation()
    {
        var called = false;
        var dialog = new NewWorkspaceWindow(Path.GetTempPath(), (_, _) => { called = true; return Task.FromResult(new CommandResult(CommandOutcome.Completed, "created")); });
        dialog.Show();
        dialog.GetLogicalDescendants().OfType<Button>().Single(button => button.Name == "WorkspaceCancel").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.False(called);
    }
}
