using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Axiom.Core;
using Axiom.Infrastructure;
using Xunit;

namespace Axiom.App.Tests;

public sealed class CpAnalysisWindowTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Failed_Local_Helper_Leaves_Credential_Source_And_Connection_Untouched_And_Allows_Retry(bool start)
    {
        var changes = 0;
        var attempts = 0;
        var window = new CpAnalysisWindow(AnalysisSnapshot.Capture("one", "Reviewed source"), () => true, () => null, _ => changes++,
            runLocalHelper: (_, _) => ++attempts == 1
                ? Task.FromException(new InvalidOperationException("Local CP helper was not found at fixture path."))
                : Task.CompletedTask);
        window.Show();
        var controls = window.GetLogicalDescendants().ToArray();
        var helper = controls.OfType<Button>().Single(button => button.Name == (start ? "CpStartLocal" : "CpCopyLocalCredential"));
        var credential = controls.OfType<TextBox>().Single(control => control.Name == "CpCredential");
        var endpoint = controls.OfType<TextBox>().Single(control => control.Name == "CpEndpoint");
        var status = controls.OfType<TextBlock>().Single(control => control.Name == "CpConnectionStatus");
        credential.Text = "user-entered-credential";
        var originalEndpoint = endpoint.Text;
        helper.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Contains("fixture path", status.Text);
        Assert.Equal(originalEndpoint, endpoint.Text);
        Assert.True(helper.IsEffectivelyEnabled);
        helper.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.StartsWith(start ? "Local CP is running" : "Local credential copied", status.Text);
        Assert.Equal(2, attempts);
        Assert.Equal(0, changes);
        Assert.Equal(start ? "http://127.0.0.1:5277" : originalEndpoint, endpoint.Text);
        Assert.Equal("user-entered-credential", credential.Text);
        Assert.Equal("Reviewed source", controls.OfType<TextBox>().Single(control => control.Name == "CpSnapshotReview").Text);
        Assert.False(controls.OfType<Button>().Single(button => button.Name == "CpSubmit").IsEnabled);
        window.Close();
    }

    [AvaloniaFact]
    public void Local_Buttons_Do_Not_Connect_Or_Submit_And_Starting_Local_Disables_A_Different_Connected_Endpoint()
    {
        using var client = new CpDocumentAnalysisClient(new Uri("https://fixture.invalid/"), "fixture-only-analysis-credential-at-least-32");
        var calls = new List<bool>();
        var connectionChanges = 0;
        var window = new CpAnalysisWindow(AnalysisSnapshot.Capture("one", "Reviewed source"), () => true, () => client, _ => connectionChanges++,
            runLocalHelper: (start, _) => { calls.Add(start); return Task.CompletedTask; });
        window.Show();
        var controls = window.GetLogicalDescendants().ToArray();
        var copy = controls.OfType<Button>().Single(button => button.Name == "CpCopyLocalCredential");
        var start = controls.OfType<Button>().Single(button => button.Name == "CpStartLocal");
        var endpoint = controls.OfType<TextBox>().Single(control => control.Name == "CpEndpoint");
        copy.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(client.Endpoint.AbsoluteUri, endpoint.Text);
        start.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(new[] { false, true }, calls);
        Assert.Equal(0, connectionChanges);
        Assert.Equal("http://127.0.0.1:5277", endpoint.Text);
        Assert.False(controls.OfType<Button>().Single(button => button.Name == "CpSubmit").IsEnabled);
        Assert.Contains(controls.OfType<Button>(), button => button.Name == "ContextualHelp");
        window.Close();
    }

    [AvaloniaFact]
    public void Reopened_Connection_Displays_Actual_Endpoint_And_Disables_Submission_After_Edit()
    {
        using var client = new CpDocumentAnalysisClient(new Uri("https://fixture.invalid/analysis/"), "fixture-only-analysis-credential-at-least-32");
        var source = AnalysisSnapshot.Capture("one", "Reviewed source");
        var window = new CpAnalysisWindow(source, () => true, () => client, _ => { });
        window.Show();
        var endpoint = window.GetLogicalDescendants().OfType<TextBox>().Single(control => control.Name == "CpEndpoint");
        var submit = window.GetLogicalDescendants().OfType<Button>().Single(control => control.Name == "CpSubmit");

        Assert.Equal(client.Endpoint.AbsoluteUri, endpoint.Text);
        Assert.True(submit.IsEnabled);
        endpoint.Text = "https://different.invalid/";
        Assert.False(submit.IsEnabled);
        endpoint.Text = client.Endpoint.AbsoluteUri;
        Assert.True(submit.IsEnabled);
        window.Close();
    }
}
