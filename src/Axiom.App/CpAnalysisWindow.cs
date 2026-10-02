using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Axiom.Core;
using Axiom.Infrastructure;

namespace Axiom.App;

internal sealed class CpAnalysisWindow : Window
{
    internal CpAnalysisWindow( AnalysisSnapshot snapshot
                             , Func<bool> sourceStillCurrent
                             , Func<CpDocumentAnalysisClient?> getClient
                             , Action<CpDocumentAnalysisClient?> setClient )
    {
        Title = "CP connection and reviewed document analysis";
        Width = 820;
        Height = 760;
        MinWidth = 540;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var endpoint = new TextBox { Name = "CpEndpoint", Text = getClient()?.Endpoint.AbsoluteUri ?? "http://localhost:5273", PlaceholderText = "CP Development endpoint" };
        var credential = new TextBox { Name = "CpCredential", PasswordChar = '●', PlaceholderText = "Dedicated analysis credential (session only)" };
        var connect = new Button { Name = "CpConnect", Content = "Connect / check capabilities" };
        var disconnect = new Button { Content = "Disconnect" };
        var submit = new Button { Name = "CpSubmit", Content = "Submit reviewed snapshot for insights", IsEnabled = getClient() is not null };
        var cancel = new Button { Content = "Cancel request", IsEnabled = false };
        var close = new Button { Content = "Close", IsCancel = true };
        var question = new TextBox { Text = "Summarize this document and identify uncertainties.", PlaceholderText = "Analysis question (up to 4,000 characters)" };
        var review = new TextBox { Name = "CpSnapshotReview", Text = snapshot.Text, IsReadOnly = true, AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MinHeight = 140 };
        var output = new TextBox { Name = "CpDerivedOutput", IsReadOnly = true, AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MinHeight = 140 };
        var status = new TextBlock { Text = "Optional connection. No content is sent until you submit the exact snapshot shown below.", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        CancellationTokenSource? requestCancellation = null;
        bool ConnectedEndpointMatches() => getClient() is { } client && Uri.TryCreate(endpoint.Text, UriKind.Absolute, out var proposed)
            && new Uri(proposed.AbsoluteUri.TrimEnd('/') + "/").Equals(client.Endpoint);
        void Busy(bool busy)
        {
            connect.IsEnabled = disconnect.IsEnabled = !busy;
            endpoint.IsEnabled = credential.IsEnabled = question.IsEnabled = !busy;
            submit.IsEnabled = !busy && ConnectedEndpointMatches();
            cancel.IsEnabled = busy;
        }
        endpoint.PropertyChanged += (_, change) =>
        {
            if (change.Property == TextBox.TextProperty) submit.IsEnabled = requestCancellation is null && ConnectedEndpointMatches();
        };
        async Task RunAsync(Func<CancellationToken, Task> operation)
        {
            requestCancellation = new CancellationTokenSource();
            Busy(true);
            try { await operation(requestCancellation.Token); }
            catch (OperationCanceledException) { status.Text = "Request cancelled. Source and offline work are unchanged."; }
            catch (Exception exception) { status.Text = exception is InvalidOperationException or ArgumentException ? exception.Message : "CP returned an invalid or unavailable response."; }
            finally { requestCancellation.Dispose(); requestCancellation = null; Busy(false); }
        }
        connect.Click += async (_, _) => await RunAsync(async cancellationToken =>
        {
            var candidate = new CpDocumentAnalysisClient(new Uri(endpoint.Text ?? ""), credential.Text ?? "");
            try
            {
                var capabilities = await candidate.GetCapabilitiesAsync(cancellationToken);
                if (capabilities.Simulated) throw new InvalidOperationException("CP reports a simulated provider; live analysis is unavailable.");
                setClient(candidate);
                credential.Text = "";
                status.Text = $"Authenticated endpoint: {candidate.Endpoint}. Insights configured: {capabilities.InsightsConfigured}; embeddings configured: {capabilities.EmbeddingsConfigured}. Availability: {capabilities.Availability}. No document was submitted.";
            }
            catch { candidate.Dispose(); throw; }
        });
        disconnect.Click += (_, _) => { setClient(null); credential.Text = ""; output.Text = ""; submit.IsEnabled = false; status.Text = "Disconnected; session credential discarded."; };
        submit.Click += async (_, _) => await RunAsync(async cancellationToken =>
        {
            if (!sourceStillCurrent()) throw new InvalidOperationException("Source changed. Close and reopen this screen to review a fresh snapshot before submitting.");
            if (!ConnectedEndpointMatches()) throw new InvalidOperationException("Endpoint changed. Connect and check capabilities before submitting.");
            var client = getClient() ?? throw new InvalidOperationException("Connect to CP first.");
            status.Text = "Submitting only the reviewed snapshot…";
            var result = await client.GetInsightsAsync(new[] { snapshot }, question.Text, cancellationToken);
            output.Text = AnalysisCitationPolicy.Report(result);
            status.Text = $"Derived output — {(sourceStillCurrent() ? "source hash current" : "STALE: source changed")}; {result.Provider}; model {result.Model ?? "not attested"}; {result.Provenance}; {result.CreatedAt:u}. Citations and factual claims require review.";
        });
        cancel.Click += (_, _) => requestCancellation?.Cancel();
        close.Click += (_, _) => Close();
        Closed += (_, _) => { requestCancellation?.Cancel(); credential.Text = ""; };
        var layout = new StackPanel
        {
            Margin = new Thickness(18)
          , Spacing = 10
          , Children =
            {
                new TextBlock { Text = "Connection (credential retained in memory only)", FontWeight = Avalonia.Media.FontWeight.Bold }
              , endpoint, credential
              , new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { connect, disconnect } }
              , status
              , new TextBlock { Text = $"Exact reviewed snapshot: {snapshot.ReferenceKey}; SHA-256 {snapshot.SourceHash}", TextWrapping = Avalonia.Media.TextWrapping.Wrap }
              , review, question
              , new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { submit, cancel } }
              , new TextBlock { Text = "Derived output — separate from Markdown; select text to copy explicitly." }
              , output, close
            }
        };
        Content = new ScrollViewer { Content = layout };
    }
}
