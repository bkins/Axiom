using Avalonia.Controls;
using Avalonia.Layout;
using Axiom.Core;
using Axiom.Infrastructure;

namespace Axiom.App;

internal sealed class BulkEnrollmentWindow : Window
{
    private readonly CancellationTokenSource _cancellation = new();
    private bool _running;
    private bool _applied;

    public BulkEnrollmentWindow(WorkspaceRecord workspace, BulkEnrollmentService service)
    {
        Title = "Make Axiom Documents — review selected files";
        Width = 800;
        Height = 500;
        MinWidth = 600;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var status = new TextBlock { Text = "Reviewing workspace…", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var list = new ListBox { Name = "EnrollmentFiles", SelectionMode = SelectionMode.Multiple | SelectionMode.Toggle };
        Avalonia.Automation.AutomationProperties.SetName(list, "Enrollment preview; click or press Space to toggle files");
        var instructions = new TextBlock
        {
            Text         = "Bulk enrollment selects files here, separately from the explorer. Click each row to add/remove it; use Space on a focused row or Shift for a range. Review the selected count, then Apply."
          , TextWrapping = Avalonia.Media.TextWrapping.Wrap
          , Margin       = new Avalonia.Thickness(0, 0, 0, 8)
        };
        var selectionStatus = new TextBlock { Name = "EnrollmentSelectionStatus", Text = "0 files selected", Margin = new Avalonia.Thickness(0, 5) };
        list.SelectionChanged += (_, _) => selectionStatus.Text = $"{list.SelectedItems!.Count} files selected";
        var selectAll = new Button { Name = "SelectAllEligible", Content = "Select all eligible", IsEnabled = false };
        var clear = new Button { Name = "ClearEnrollmentSelection", Content = "Clear selection" };
        var apply = new Button { Content = "Apply — save selected files…", IsEnabled = false };
        var cancel = new Button { Content = "Cancel / Close" };
        var progress = new Progress<string>(message => status.Text = message);
        selectAll.Click += (_, _) =>
        {
            list.UnselectAll();
            foreach (var item in list.Items.OfType<BulkEnrollmentEntry>().Where(entry => entry.IsEligible)) list.SelectedItems!.Add(item);
        };
        clear.Click += (_, _) => list.UnselectAll();
        cancel.Click += (_, _) => { if (_running) _cancellation.Cancel(); else Close(_applied); };
        apply.Click += async (_, _) =>
        {
            var selected = list.SelectedItems!.OfType<BulkEnrollmentEntry>().ToArray();
            if (selected.Length == 0) { status.Text = "Select files to enroll. No files are selected initially."; return; }
            var summary = string.Join(Environment.NewLine, selected.Select(entry => entry.RelativePath));
            if (await ChoiceDialog.ShowAsync(this, "Save selected enrollment", $"Apply will write {selected.Length} selected files in {workspace.RootPath}. Each ordinary file receives an ID; already enrolled files are skipped. Completed writes remain if cancelled.\n\n{summary}", "Apply", "Cancel") != "Apply") return;
            SetRunning(true);
            try
            {
                var results = await Task.Run(() => service.ApplyAsync(workspace, selected, progress, _cancellation.Token));
                _applied = results.Any(result => result.Status == "Enrolled");
                list.ItemsSource = results.Select(result => $"{result.RelativePath} — {result.Status}").ToArray();
                status.Text = $"Enrolled {CountApplied(results)} of {selected.Length}. Results below show each outcome. Close and reopen to refresh/retry.";
            }
            catch (Exception exception) { status.Text = exception.Message; }
            finally { SetRunning(false); apply.IsEnabled = false; selectAll.IsEnabled = false; }
        };
        Opened += async (_, _) =>
        {
            SetRunning(true);
            try
            {
                var entries = await Task.Run(() => service.PreviewAsync(workspace, progress, _cancellation.Token));
                list.ItemsSource = entries;
                status.Text = $"{workspace.RootPath}\n{entries.Count} reviewed; {entries.Count(entry => entry.IsEligible)} eligible; {entries.Count(entry => !entry.IsEligible)} skipped/blocked. Select files to enroll; selection starts empty.";
                apply.IsEnabled = true;
                selectAll.IsEnabled = true;
            }
            catch (OperationCanceledException) { status.Text = "Review cancelled. No files changed."; }
            catch (Exception exception) { status.Text = "Review failed: " + exception.Message; }
            finally { SetRunning(false); }
        };
        Closing += (_, eventArgs) => { if (_running) { _cancellation.Cancel(); eventArgs.Cancel = true; } };
        var buttons = new WrapPanel { Orientation = Orientation.Horizontal, Children = { selectAll, clear, apply, cancel } };
        foreach (var button in buttons.Children.OfType<Button>()) button.Margin = new Avalonia.Thickness(0, 5, 8, 5);
        var heading = new StackPanel { Children = { instructions, status, selectionStatus } };
        Content = new DockPanel { Margin = new Avalonia.Thickness(18), Children = { heading, buttons, list } };
        DockPanel.SetDock(heading, Dock.Top);
        DockPanel.SetDock(status, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);

        void SetRunning(bool running)
        {
            _running = running;
            list.IsEnabled = !running;
            if (running) { selectAll.IsEnabled = false; apply.IsEnabled = false; }
            clear.IsEnabled = !running;
            cancel.Content = running ? "Cancel remaining work" : "Close";
        }
    }

    private static int CountApplied(IReadOnlyList<BulkEnrollmentResult> results) => results.Count(result => result.Status == "Enrolled");
}
