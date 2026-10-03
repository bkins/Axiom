using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Axiom.Core;
using Axiom.Infrastructure;

namespace Axiom.App;

internal sealed class WorkspaceGitView : UserControl
{
    internal Action RefreshAvailability { get; private set; } = () => { };
    internal Action CancelOperation { get; private set; } = () => { };
    internal ListBox ChangedFiles { get; }
    internal TextBox DiffText { get; }
    internal TextBox CommitMessage { get; }
    internal bool IsBusy { get; private set; }
    internal WorkspaceGitView(Window owner, GitWorkspaceService service, GitRepositorySnapshot initial, Func<string, bool> dirty, Func<bool> contextValid, Action closePanel)
    {
        var current = initial;
        var busy = false;
        CancellationTokenSource? operation = null;
        var list = new ListBox { ItemsSource = current.Files, SelectionMode = SelectionMode.Multiple };
        ChangedFiles = list;
        list.MinHeight = 100;
        list.MaxHeight = 200;
        list.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<GitChangedFile>((file, _) => new TextBlock
        {
            Text = file is null ? "" : $"Index {file.IndexStatus} / Saved {file.WorkingStatus}  {file.RelativePath}\n{file.Reason ?? (dirty(file.RelativePath) ? "Unsaved/conflicted buffer — save or resolve it before Git changes." : "") }"
          , TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });
        Avalonia.Automation.AutomationProperties.SetName(list, "Changed repository files; select documents explicitly for Git operations");
        var header = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var status = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var refresh = new Button { Content = "Refresh" };
        var workingDiff = new Button { Content = "Saved diff" };
        var stagedDiff = new Button { Content = "Staged diff" };
        var stage = new Button { Content = "Stage selected…" };
        var unstage = new Button { Content = "Unstage selected…" };
        var commit = new Button { Content = "Commit selected staged…" };
        var cancel = new Button { Content = "Cancel operation", IsEnabled = false };
        var close = new Button { Content = "Close" };
        DiffText = new TextBox { IsReadOnly = true, AcceptsReturn = true, FontFamily = new Avalonia.Media.FontFamily("Consolas"), FontSize = 11, MinHeight = 100, MaxHeight = 240, PlaceholderText = "Select a saved file, then choose Saved diff or Staged diff.", TextWrapping = Avalonia.Media.TextWrapping.NoWrap };
        Avalonia.Automation.AutomationProperties.SetName(DiffText, "Embedded read-only Git line comparison with HEAD and current line numbers");
        CommitMessage = new TextBox { AcceptsReturn = true, MinHeight = 50, MaxHeight = 90, PlaceholderText = "Commit message for selected staged documents" };
        Avalonia.Automation.AutomationProperties.SetName(CommitMessage, "Local Git commit message");
        GitChangedFile[] Selected() => list.SelectedItems?.OfType<GitChangedFile>().ToArray() ?? [];
        void Update()
        {
            IsBusy = busy;
            header.Text = $"Branch: {current.BranchReference}\nHEAD: {current.Head ?? "unborn"}";
            ToolTip.SetTip(header, "Repository: " + current.RepositoryRoot + "\n" + current.Reason);
            var selected = Selected();
            var manageable = !busy && contextValid() && current.CanMutate && selected.Length > 0 && selected.All(file => file.CanManage && !dirty(file.RelativePath));
            stage.IsEnabled = manageable;
            unstage.IsEnabled = manageable && selected.All(file => file.IndexStatus is not " " and not "?");
            commit.IsEnabled = unstage.IsEnabled && !string.IsNullOrWhiteSpace(CommitMessage.Text);
            workingDiff.IsEnabled = stagedDiff.IsEnabled = !busy && contextValid() && selected.Length == 1 && selected[0].CanManage;
            refresh.IsEnabled = !busy && contextValid();
            close.IsEnabled = list.IsEnabled = CommitMessage.IsEnabled = !busy;
            cancel.IsEnabled = busy;
            var reason = busy ? "A Git operation is running; cancel it or wait." : !contextValid() ? "The workspace changed or a document operation is running." : !current.CanMutate ? current.Reason ?? "Repository requires external Git." : selected.Length == 0 ? "Select saved Markdown files first." : selected.Any(file => dirty(file.RelativePath)) ? "A selected buffer is unsaved or conflicted; save or resolve it first." : "The selected files are unsupported for this Git operation.";
            foreach (var button in new[] { stage, unstage, workingDiff, stagedDiff, commit })
            {
                var message = button == commit && unstage.IsEnabled && string.IsNullOrWhiteSpace(CommitMessage.Text) ? "Enter a commit message first." : reason;
                ToolTip.SetTip(button, button.IsEnabled ? null : message);
                Avalonia.Automation.AutomationProperties.SetHelpText(button, button.IsEnabled ? null : message);
            }
        }
        async Task Refresh(CancellationToken cancellationToken)
        {
            current = await service.InspectAsync(initial.WorkspaceRoot, cancellationToken) ?? throw new IOException("The workspace no longer has a Git repository.");
            list.ItemsSource = current.Files;
            DiffText.Text = string.Empty;
            Update();
        }
        async Task Run(Func<CancellationToken, Task> action)
        {
            if (busy || !contextValid()) return;
            busy = true;
            operation = new CancellationTokenSource();
            Update();
            try { await action(operation.Token); }
            catch (OperationCanceledException) { status.Text = "Cancelled. Completed Git changes remain; refresh before continuing."; }
            catch (Exception exception) { status.Text = exception.Message + " Refresh before retrying."; }
            finally
            {
                operation.Dispose();
                operation = null;
                busy = false;
                Update();
            }
        }
        list.SelectionChanged += (_, _) => Update();
        refresh.Click += async (_, _) => await Run(Refresh);
        cancel.Click += (_, _) => operation?.Cancel();
        close.Click += (_, _) => closePanel();
        RefreshAvailability = Update;
        CancelOperation = () => operation?.Cancel();
        DetachedFromVisualTree += (_, _) => operation?.Cancel();
        CommitMessage.TextChanged += (_, _) => Update();
        KeyDown += (_, args) =>
        {
            if (args.Key == Avalonia.Input.Key.Escape) { if (busy) operation?.Cancel(); else closePanel(); args.Handled = true; }
            else if (args.Key == Avalonia.Input.Key.Enter && args.KeyModifiers == Avalonia.Input.KeyModifiers.Control && commit.IsEnabled)
            { commit.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); args.Handled = true; }
        };
        async Task Diff(bool staged)
        {
            var selected = Selected().Single();
            await Run(async cancellationToken =>
            {
                var diff = await service.ReadDiffAsync(current, selected.RelativePath, cancellationToken);
                var lines = new LineDiffService().Compare(diff.HeadText, staged ? diff.IndexText : diff.WorkingText);
                DiffText.Text = $"HEAD → {(staged ? "Staged index" : "Saved working file")} · {selected.RelativePath}\n" + string.Join('\n', lines.Select(line =>
                    $"{(line.Kind == LineDiffKind.Added ? '+' : line.Kind == LineDiffKind.Removed ? '−' : '·')} {line.PreviousLine?.ToString() ?? "-",6} {line.CurrentLine?.ToString() ?? "-",6}  " +
                    line.Text.Replace("\r\n", "↵", StringComparison.Ordinal).Replace("\r", "␍", StringComparison.Ordinal).Replace("\n", "␊", StringComparison.Ordinal)));
            });
        }
        workingDiff.Click += async (_, _) => await Diff(false);
        stagedDiff.Click += async (_, _) => await Diff(true);
        async Task ChangeStaging(bool add)
        {
            var selected = Selected();
            if (await ChoiceDialog.ShowAsync(owner, add ? "Stage selected saved documents" : "Unstage selected documents",
                string.Join("\n", selected.Select(file => file.RelativePath)) + "\nOnly these index entries change. Saved files and editor buffers remain unchanged. Git filters/hooks are not run; filtered/LFS files require external Git.", "Continue", "Cancel") != "Continue") return;
            await Run(async cancellationToken =>
            {
                var results = new List<string>();
                foreach (var file in selected)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!contextValid() || dirty(file.RelativePath)) throw new IOException("The workspace changed, or a selected editor buffer has unsaved changes or a conflict.");
                    var reviewed = await service.InspectAsync(initial.WorkspaceRoot, cancellationToken) ?? throw new IOException("Repository unavailable.");
                    if (add) await service.StageAsync(reviewed, file.RelativePath, await service.ReviewWorkingHashAsync(reviewed, file.RelativePath, cancellationToken), cancellationToken);
                    else await service.UnstageAsync(reviewed, file.RelativePath, cancellationToken);
                    results.Add((add ? "Staged: " : "Unstaged: ") + file.RelativePath);
                    status.Text = string.Join("\n", results);
                }
                await Refresh(cancellationToken);
            });
        }
        stage.Click += async (_, _) => await ChangeStaging(true);
        unstage.Click += async (_, _) => await ChangeStaging(false);
        commit.Click += async (_, _) =>
        {
            var selected = Selected();
            var message = CommitMessage.Text?.Trim();
            if (string.IsNullOrWhiteSpace(message)) { status.Text = "Enter a commit message first."; return; }
            await Run(async cancellationToken =>
            {
                var review = await service.ReviewCommitAsync(current, selected.Select(file => file.RelativePath).ToArray(), cancellationToken);
                if (await ChoiceDialog.ShowAsync(owner, "Confirm selected Git commit", $"Branch: {current.BranchReference}\n{string.Join("\n", selected.Select(file => file.RelativePath))}\nMessage: {message}\nThis is an unsigned local Git commit; repository hooks are disabled. No Save or network action occurs.", "Commit", "Cancel") != "Commit") { status.Text = "Commit cancelled."; return; }
                if (!contextValid() || selected.Any(file => dirty(file.RelativePath))) throw new IOException("The workspace or a selected buffer changed while reviewing the commit.");
                var hash = await service.CommitAsync(review, message, cancellationToken);
                status.Text = "Committed selected staged documents: " + hash;
                CommitMessage.Text = string.Empty;
                await Refresh(cancellationToken);
            });
        };
        var notice = new TextBlock { Text = "Index / Saved status codes: M modified, A added, D deleted, R renamed, ? untracked, U conflict. Renames, submodules, sparse checkout, filtered/LFS files and pending Git operations require external Git. Axiom document-history checkpoints are separate refs; these commits update the reviewed branch.", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var buttons = new WrapPanel { Orientation = Orientation.Horizontal, Children = { refresh, workingDiff, stagedDiff, stage, unstage, commit, cancel, close } };
        foreach (var button in buttons.Children) button.Margin = new Thickness(0, 0, 8, 6);
        var details = new Expander { Header = "Repository details and limits", Content = new TextBlock { Text = current.RepositoryRoot + "\n" + current.Reason + "\n" + notice.Text, TextWrapping = Avalonia.Media.TextWrapping.Wrap } };
        var grid = new Grid { Margin = new Thickness(8), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,Auto,Auto"), RowSpacing = 8, Children = { header, details, list, DiffText, CommitMessage, new ScrollViewer { Content = status, MaxHeight = 70 }, buttons } };
        for (var index = 0; index < grid.Children.Count; index++) Grid.SetRow(grid.Children[index], index);
        Content = new ScrollViewer { Content = grid };
        Update();
    }
}
