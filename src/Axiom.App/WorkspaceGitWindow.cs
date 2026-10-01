using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Axiom.Core;
using Axiom.Infrastructure;

namespace Axiom.App;

internal sealed class WorkspaceGitWindow : Window
{
    internal WorkspaceGitWindow(GitWorkspaceService service, GitRepositorySnapshot initial, Func<string, bool> dirty)
    {
        Title = "Workspace Git";
        Width = 1000;
        Height = 700;
        MinWidth = 580;
        MinHeight = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var current = initial;
        var busy = false;
        CancellationTokenSource? operation = null;
        var list = new ListBox { ItemsSource = current.Files, SelectionMode = SelectionMode.Multiple };
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
        var close = new Button { Content = "Close", IsCancel = true };
        GitChangedFile[] Selected() => list.SelectedItems?.OfType<GitChangedFile>().ToArray() ?? [];
        void Update()
        {
            header.Text = $"Repository: {current.RepositoryRoot}\nBranch: {current.BranchReference}; HEAD: {current.Head ?? "unborn"}\n{current.Reason ?? "Inspect and deliberately stage/unstage/commit selected saved Markdown files. Git is optional; no push, reset, branch switching or automatic Save."}";
            var selected = Selected();
            var manageable = !busy && current.CanMutate && selected.Length > 0 && selected.All(file => file.CanManage && !dirty(file.RelativePath));
            stage.IsEnabled = manageable;
            unstage.IsEnabled = commit.IsEnabled = manageable && selected.All(file => file.IndexStatus is not " " and not "?");
            workingDiff.IsEnabled = stagedDiff.IsEnabled = !busy && selected.Length == 1 && selected[0].CanManage;
            refresh.IsEnabled = close.IsEnabled = list.IsEnabled = !busy;
            cancel.IsEnabled = busy;
        }
        async Task Refresh(CancellationToken cancellationToken)
        {
            current = await service.InspectAsync(initial.WorkspaceRoot, cancellationToken) ?? throw new IOException("The workspace no longer has a Git repository.");
            list.ItemsSource = current.Files;
            Update();
        }
        async Task Run(Func<CancellationToken, Task> action)
        {
            if (busy) return;
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
        close.Click += (_, _) => Close();
        Closing += (_, eventArgs) => { if (busy) { eventArgs.Cancel = true; operation?.Cancel(); status.Text = "Cancelling the current operation; close after it finishes."; } };
        async Task Diff(bool staged)
        {
            var selected = Selected().Single();
            await Run(async cancellationToken =>
            {
                var diff = await service.ReadDiffAsync(current, selected.RelativePath, cancellationToken);
                await new LineDiffWindow("HEAD · " + selected.RelativePath, diff.HeadText, staged ? "Staged index" : "Saved working file", staged ? diff.IndexText : diff.WorkingText).ShowDialog(this);
            });
        }
        workingDiff.Click += async (_, _) => await Diff(false);
        stagedDiff.Click += async (_, _) => await Diff(true);
        async Task ChangeStaging(bool add)
        {
            var selected = Selected();
            if (await ChoiceDialog.ShowAsync(this, add ? "Stage selected saved documents" : "Unstage selected documents",
                string.Join("\n", selected.Select(file => file.RelativePath)) + "\nOnly these index entries change. Saved files and editor buffers remain unchanged. Git filters/hooks are not run; filtered/LFS files require external Git.", "Continue", "Cancel") != "Continue") return;
            await Run(async cancellationToken =>
            {
                var results = new List<string>();
                foreach (var file in selected)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (dirty(file.RelativePath)) throw new IOException("A selected editor buffer has unsaved changes or a conflict.");
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
            await Run(async cancellationToken =>
            {
                var review = await service.ReviewCommitAsync(current, selected.Select(file => file.RelativePath).ToArray(), cancellationToken);
                var message = await TextEntryDialog.ShowAsync(this, "Commit selected staged documents", "Enter a commit message. Only reviewed staged bytes are committed; other staged files and all working buffers are retained.", "");
                if (message is null) { status.Text = "Commit cancelled."; return; }
                if (await ChoiceDialog.ShowAsync(this, "Confirm selected Git commit", $"Branch: {current.BranchReference}\n{string.Join("\n", selected.Select(file => file.RelativePath))}\nMessage: {message}\nThis is an unsigned local Git commit; repository hooks are disabled. No Save or network action occurs.", "Commit", "Cancel") != "Commit") { status.Text = "Commit cancelled."; return; }
                if (selected.Any(file => dirty(file.RelativePath))) throw new IOException("A selected buffer changed while reviewing the commit.");
                var hash = await service.CommitAsync(review, message, cancellationToken);
                status.Text = "Committed selected staged documents: " + hash;
                await Refresh(cancellationToken);
            });
        };
        var notice = new TextBlock { Text = "Index / Saved status codes: M modified, A added, D deleted, R renamed, ? untracked, U conflict. Renames, submodules, sparse checkout, filtered/LFS files and pending Git operations require external Git. Axiom document-history checkpoints are separate refs; these commits update the reviewed branch.", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var buttons = new WrapPanel { Orientation = Orientation.Horizontal, Children = { refresh, workingDiff, stagedDiff, stage, unstage, commit, cancel, close } };
        foreach (var button in buttons.Children) button.Margin = new Thickness(0, 0, 8, 6);
        var grid = new Grid { Margin = new Thickness(20), RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto,Auto"), RowSpacing = 12, Children = { header, notice, list, new ScrollViewer { Content = status, MaxHeight = 100 }, buttons } };
        Grid.SetRow(notice, 1);
        Grid.SetRow(list, 2);
        Grid.SetRow(grid.Children[3], 3);
        Grid.SetRow(buttons, 4);
        Content = grid;
        Update();
    }
}
