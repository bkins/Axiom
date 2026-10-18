using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Axiom.Core;

namespace Axiom.App;

public partial class MainWindow
{
    private void InitializeCompactWorkbench()
    {
        var menu = new MenuFlyout();
        foreach (var (button, title) in new[] { (FileCommandsButton, "File"), (EditCommandsButton, "Edit"), (FormatCommandsButton, "Format"), (ToolsCommandsButton, "Tools") })
        {
            var group = new MenuItem { Header = title };
            if (button.Flyout is MenuFlyout original)
                foreach (var item in original.Items.ToArray()) { original.Items.Remove(item); group.Items.Add(item); }
            menu.Items.Add(group);
            if (button != FileCommandsButton) button.IsVisible = false;
        }
        var helpMenu = new MenuItem { Header = "Help" };
        foreach (var (title, command) in new[]
        {
            ("Browse all feature topics", "help.home")
          , ("Workspace Help", "help.workspace")
          , ("Metadata Help", "help.metadata")
          , ("Relationships Help", "help.relationships")
          , ("CP connection Help", "help.cp")
          , ("Semantic search test walkthrough", "help.semantic")
          , ("Guided walkthrough", "help.walkthrough")
        })
        {
            var item = new MenuItem { Header = title };
            item.Click += async (_, _) => await ExecuteCommandAsync(command);
            helpMenu.Items.Add(item);
        }
        menu.Items.Add(helpMenu);
        FileCommandsButton.Flyout = menu;
        CommandPanel.Children.Remove(FileCommandsButton);
        CommandPanel.Children.Insert(0, FileCommandsButton);
        AutomationProperties.SetName(FileCommandsButton, "Main menu: File Edit Format Tools Help");
        ToolTip.SetTip(FileCommandsButton, "Main menu — Alt or F10; Alt+F/E/M opens a section");
        FileCommandsButton.Content = CreateWorkbenchIcon("menu");
        FileCommandsButton.Classes.Add("toolbar-icon");
        AppearanceButton.IsVisible = false;
        WindowDecorationProperties.SetElementRole(CommandPanel, WindowDecorationsElementRole.User);
        foreach (var button in CommandPanel.GetLogicalDescendants().OfType<Button>().ToArray())
        {
            var name = AutomationProperties.GetName(button) ?? "";
            var icon = name switch
            {
                "Navigate back" => "back"
              , "Navigate forward" => "forward"
              , "New document toolbar" => "new"
              , "Save document toolbar" => "save"
              , "Offline User Guide" => "help"
              , "Application settings" => "settings"
              , "Insert workspace image reference" => "image"
              , "Make Axiom Document" => "identity"
              , "Show preview at editor caret" => "target"
              , _ => null
            };
            if (icon is not null) button.Content = CreateWorkbenchIcon(icon);
            button.Classes.Add("toolbar-icon");
        }
        ReadingModeButton.Content = CreateWorkbenchIcon(_readingMode ? "edit" : "eye");
        PinButton.Content = CreateWorkbenchIcon("pin");
        ScrollSyncButton.Content = CreateWorkbenchIcon("sync");
        ExplorerFilterButton.Content = CreateWorkbenchIcon("search");
        WorkspaceActionsButton.Content = CreateWorkbenchIcon("more");
        ExplorerFilterButton.Classes.Add("toolbar-icon");
        WorkspaceActionsButton.Classes.Add("toolbar-icon");
        foreach (var button in ToolWindowRail.GetLogicalDescendants().OfType<Button>())
        {
            button.Classes.Add("toolbar-icon");
            button.Content = CreateWorkbenchIcon(AutomationProperties.GetName(button) switch
            {
                "Toggle metadata panel" => "identity"
              , "Toggle relationships panel" => "forward"
              , "Toggle classification panel" => "grid"
              , "Toggle workspace search panel" => "search"
              , "Toggle document outline panel" => "list"
              , "Toggle backlinks panel" => "back"
              , "Toggle Git changes panel" => "git"
              , "Offline User Guide" => "help"
              , _ => "grid"
            });
        }
        var actions = new MenuFlyout();
        foreach (var id in new[] { "document.new", "workspace.create", "workspace.open", "workspace.catalog", "workspace.refresh", "workspace.createDocument", "workspace.manage.createfolder", "workspace.bulkEnroll", "help.workspace" })
        {
            var command = _commands.Commands.Single(command => command.Id == id);
            var item = new MenuItem { Header = command.Title };
            item.Click += async (_, _) => await ExecuteCommandAsync(id);
            actions.Items.Add(item);
        }
        actions.Opening += (_, _) =>
        {
            foreach (var item in actions.Items.OfType<MenuItem>())
            {
                var command = _commands.Commands.Single(command => command.Title == item.Header?.ToString());
                var available = _commands.GetAvailability(command.Id, CaptureCommandContext());
                item.IsEnabled = available.Enabled;
                ToolTip.SetTip(item, available.Reason);
            }
        };
        WorkspaceActionsButton.Flyout = actions;
        AddDisabledCommandHost(BackButton, "navigation.back");
        AddDisabledCommandHost(ForwardButton, "navigation.forward");
        InsertImageCommandHost.Focusable = true;
        InsertImageCommandHost.GotFocus += (_, _) =>
        {
            if (InsertImageButton.IsEnabled) InsertImageButton.Focus();
            else ShowInformation(_commands.GetAvailability("editor.insertImage", CaptureCommandContext()).Reason ?? "Save inside the workspace before inserting images.");
        };
        AutomationProperties.SetName(InsertImageCommandHost, "Insert image command and availability");
    }

    private void ApplyCompactTitleBar(bool compact)
    {
        ExtendClientAreaToDecorationsHint = compact && OperatingSystem.IsWindows();
        ExtendClientAreaTitleBarHeightHint = 36;
        WindowDecorations = ExtendClientAreaToDecorationsHint ? WindowDecorations.BorderOnly : WindowDecorations.Full;
        CompactCaptionButtons.IsVisible = ExtendClientAreaToDecorationsHint;
        TitleCommandGrid.Margin = new Thickness(0);
    }

    private void CaptionMinimize_OnClick(object? sender, RoutedEventArgs args) => WindowState = WindowState.Minimized;
    private void CaptionMaximize_OnClick(object? sender, RoutedEventArgs args) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void CaptionClose_OnClick(object? sender, RoutedEventArgs args) => Close();

    private void AddDisabledCommandHost(Button button, string id)
    {
        var index = CommandPanel.Children.IndexOf(button);
        if (index < 0) return;
        CommandPanel.Children.Remove(button);
        var host = new Border { Child = button, Background = Brushes.Transparent, Focusable = !button.IsEnabled };
        AutomationProperties.SetName(host, AutomationProperties.GetName(button) + " availability");
        void UpdateReason()
        {
            var availability = _commands.GetAvailability(id, CaptureCommandContext());
            ToolTip.SetTip(host, availability.Enabled ? ToolTip.GetTip(button) : availability.Reason);
            AutomationProperties.SetHelpText(host, availability.Reason ?? "Command available.");
            host.Focusable = !button.IsEnabled;
        }
        button.PropertyChanged += (_, args) => { if (args.Property == IsEnabledProperty) UpdateReason(); };
        host.PointerEntered += (_, _) => UpdateReason();
        host.GotFocus += (_, _) => { UpdateReason(); if (!button.IsEnabled) ShowInformation(_commands.GetAvailability(id, CaptureCommandContext()).Reason ?? "Command unavailable."); };
        UpdateReason();
        CommandPanel.Children.Insert(index, host);
    }

    private static WorkbenchIcon CreateWorkbenchIcon(string name) => new(name switch
    {
        "menu" => "M4,6 L20,6 M4,12 L20,12 M4,18 L20,18"
      , "back" => "M10,5 L3,12 L10,19 M3,12 L21,12"
      , "forward" => "M14,5 L21,12 L14,19 M3,12 L21,12"
      , "new" => "M12,3 L12,21 M3,12 L21,12"
      , "open" => "M3,8 L3,4 L10,4 L13,7 L21,7 L21,11 M3,10 L21,10 L18,20 L3,20 Z"
      , "folder-new" => "M3,7 L3,4 L10,4 L13,7 L21,7 L21,20 L3,20 Z M12,10 L12,17 M8,14 L16,14"
      , "move" => "M3,4 L10,4 L10,20 L3,20 Z M13,12 L23,12 M19,8 L23,12 L19,16"
      , "recycle" => "M3,6 L21,6 M8,6 L8,3 L16,3 L16,6 M5,6 L6,21 L18,21 L19,6 M10,10 L10,17 M14,10 L14,17"
      , "copy" => "M8,8 L21,8 L21,21 L8,21 Z M16,8 L16,3 L3,3 L3,16 L8,16"
      , "paste" => "M8,5 L4,5 L4,21 L20,21 L20,5 L16,5 M8,3 L16,3 L16,7 L8,7 Z M8,12 L16,12 M8,16 L16,16"
      , "export" => "M4,13 L4,21 L20,21 L20,13 M12,17 L12,2 M7,7 L12,2 L17,7"
      , "unpin" => "M8,3 L16,3 L15,10 L19,14 L5,14 L9,10 Z M12,14 L12,22 M3,3 L22,22"
      , "save" => "M4,3 L18,3 L21,6 L21,21 L3,21 L3,3 Z M7,3 L7,9 L17,9 L17,3 M7,21 L7,14 L17,14 L17,21"
      , "eye" => "M2,12 Q12,-1 22,12 Q12,25 2,12 Z M15,12 A3,3 0 1 1 9,12 A3,3 0 1 1 15,12"
      , "edit" => "M4,17 L4,21 L8,21 L21,8 L17,4 Z M15,6 L19,10"
      , "help" => "M22,12 A10,10 0 1 1 2,12 A10,10 0 1 1 22,12 M8,8 Q8,4 12,5 Q18,6 14,10 L12,12 L12,14 M12,18 L12,19"
      , "settings" => "M12,2 L14,6 L18,5 L19,9 L22,12 L19,15 L18,19 L14,18 L12,22 L10,18 L6,19 L5,15 L2,12 L5,9 L6,5 L10,6 Z M15,12 A3,3 0 1 1 9,12 A3,3 0 1 1 15,12"
      , "image" => "M3,3 L21,3 L21,21 L3,21 Z M3,17 L9,11 L14,16 L17,13 L21,17 M17,7 A1,1 0 1 1 15,7 A1,1 0 1 1 17,7"
      , "pin" => "M8,3 L16,3 L15,10 L19,14 L5,14 L9,10 Z M12,14 L12,22"
      , "sync" => "M7,3 L3,7 L7,11 M3,7 L17,7 M17,13 L21,17 L17,21 M21,17 L7,17"
      , "identity" => "M12,2 L22,12 L12,22 L2,12 Z M8,12 L11,15 L17,8"
      , "target" => "M19,12 A7,7 0 1 1 5,12 A7,7 0 1 1 19,12 M12,1 L12,7 M12,17 L12,23 M1,12 L7,12 M17,12 L23,12"
      , "search" => "M17,10 A7,7 0 1 1 3,10 A7,7 0 1 1 17,10 M15,15 L22,22"
      , "grid" => "M3,3 L21,3 L21,21 L3,21 Z M3,9 L21,9 M9,3 L9,21"
      , "git" => "M6,6 A2,2 0 1 1 6,2 A2,2 0 1 1 6,6 M6,6 L6,18 M6,22 A2,2 0 1 1 6,18 A2,2 0 1 1 6,22 M18,10 A2,2 0 1 1 18,6 A2,2 0 1 1 18,10 M18,10 C18,16 6,12 6,18"
      , "list" => "M3,5 L5,5 M9,5 L21,5 M3,12 L5,12 M9,12 L21,12 M3,19 L5,19 M9,19 L21,19"
      , _ => "M4,10 A2,2 0 1 1 4,14 A2,2 0 1 1 4,10 M12,10 A2,2 0 1 1 12,14 A2,2 0 1 1 12,10 M20,10 A2,2 0 1 1 20,14 A2,2 0 1 1 20,10"
    });

    private void ExplorerFilterToggle_OnClick(object? sender, RoutedEventArgs args)
    {
        ExplorerFilterPanel.IsVisible = !ExplorerFilterPanel.IsVisible;
        if (ExplorerFilterPanel.IsVisible) ExplorerFilter.Focus();
    }

    private void ExplorerFilterClear_OnClick(object? sender, RoutedEventArgs args) => ExplorerFilter.Text = "";

    private void MetadataInfo_OnClick(object? sender, RoutedEventArgs args)
    {
        if (sender is not Button target || target.Tag is not string field) return;
        _fieldHelpFlyout?.Hide();
        _fieldHelpFlyout = FieldHelp.Show(target, field);
    }

    private Flyout? _fieldHelpFlyout;
    internal Flyout? FieldHelpFlyoutForTesting => _fieldHelpFlyout;

}
