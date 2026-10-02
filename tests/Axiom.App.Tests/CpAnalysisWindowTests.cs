using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Axiom.Core;
using Axiom.Infrastructure;
using Xunit;

namespace Axiom.App.Tests;

public sealed class CpAnalysisWindowTests
{
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
