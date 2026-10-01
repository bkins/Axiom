using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Axiom.Core;
using Xunit;

namespace Axiom.App.Tests;

public sealed class RelationshipEditorWindowTests
{
    [AvaloniaFact]
    public async Task ClosingDuringApplyWaitsForCommitAndDoesNotReportCancellation()
    {
        var owner = new Window();
        owner.Show();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var target = new ClassifiedDocument("target.md", Guid.NewGuid(), "hash", "Unique saved identity", null);
        var dialog = new RelationshipEditorWindow(Guid.NewGuid(), [target], null, async change =>
        {
            Assert.Equal(target.DocumentId, change.Value.TargetId);
            entered.SetResult();
            await release.Task;
        });
        try
        {
            var closed = dialog.ShowDialog(owner);
            dialog.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, "Apply"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await entered.Task;
            dialog.Close();
            Assert.False(closed.IsCompleted);
            Assert.False(dialog.Committed);
            release.SetResult();
            await closed;
            Assert.True(dialog.Committed);
        }
        finally { release.TrySetResult(); dialog.Close(); owner.Close(); }
    }
}
