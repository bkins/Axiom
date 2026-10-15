using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Axiom.Core;
using Axiom.Infrastructure;

namespace Axiom.App;

internal sealed class CpDocumentActionWindow : Window
{
    internal CpDocumentActionWindow(Func<AnalysisSnapshot> capture, Func<AnalysisSnapshot, bool> current,
        Func<Action<string>, CancellationToken, Task<CpDocumentAnalysisClient>> connect,
        Action<AnalysisSnapshot, string> apply, bool analyzeOnOpen = false)
    {
        Title = "CP actions for this document";
        Width = 900; Height = 760; MinWidth = 580; MinHeight = 440;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var status = new TextBlock { Name = "CpActionStatus", Text = "Analyze or suggest edits sends only this document's current text to configured CP. Nothing is saved or applied automatically.", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var question = new TextBox { Name = "CpActionQuestion", Text = "Summarize this document and identify uncertainties.", PlaceholderText = "Analysis question" };
        var editRequest = new TextBox { Name = "CpEditRequest", Text = "Improve clarity and grammar while preserving the meaning and document structure.", PlaceholderText = "Describe the Markdown edits you want" };
        var analyze = new Button { Name = "CpActionAnalyze", Content = "Analyze this document" };
        var suggest = new Button { Name = "CpActionSuggest", Content = "Suggest edits" };
        var preview = new Button { Name = "CpActionPreview", Content = "Preview changes", IsEnabled = false };
        var accept = new Button { Name = "CpActionApply", Content = "Apply to unsaved buffer", IsEnabled = false };
        var cancel = new Button { Name = "CpActionCancel", Content = "Cancel", IsEnabled = false };
        var output = new TextBox { Name = "CpActionOutput", IsReadOnly = true, AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MinHeight = 240 };
        var close = new Button { Content = "Close" };
        CancellationTokenSource? running = null;
        AnalysisSnapshot? proposedFrom = null;
        string? proposal = null;
        bool reviewed = false;
        void Busy(bool busy)
        {
            analyze.IsEnabled = suggest.IsEnabled = question.IsEnabled = editRequest.IsEnabled = !busy;
            preview.IsEnabled = !busy && proposal is not null;
            accept.IsEnabled = !busy && reviewed && proposal is not null;
            cancel.IsEnabled = busy;
        }
        async Task RunAsync(bool editing)
        {
            running = new(); Busy(true);
            proposal = null; proposedFrom = null; reviewed = false; output.Text = "";
            try
            {
                var snapshot = capture();
                var request = editing ? editRequest.Text : question.Text;
                if (string.IsNullOrWhiteSpace(snapshot.Text)) throw new InvalidOperationException("This document is empty.");
                var client = await connect(message => status.Text = message, running.Token);
                if (!current(snapshot)) throw new InvalidOperationException("The document changed during setup. Click the action again to use its current text.");
                status.Text = editing ? "Requesting a proposed Markdown revision…" : "Analyzing this document…";
                var prompt = editing ? "Propose a complete replacement Markdown document for this request: " + request + "\nPreserve facts and any axiom:documentId marker exactly. Return ONLY the complete replacement inside one markdown code fence. Use a fence longer than any fence inside the document. Do not include commentary or invented citations." : request;
                var result = await client.GetInsightsAsync([snapshot], prompt, running.Token);
                if (editing)
                {
                    proposal = MarkdownEditProposal.Parse(snapshot.Text, result.Text);
                    proposedFrom = snapshot; output.Text = proposal;
                    status.Text = current(snapshot) ? "Proposal ready. Preview changes, then explicitly Apply. Source is unchanged." : "Proposal is stale; source changed. Request a fresh proposal before applying.";
                }
                else { output.Text = AnalysisCitationPolicy.Report(result); status.Text = current(snapshot) ? "Analysis ready. Source is unchanged; review claim support." : "Analysis is stale: source changed. Source is unchanged by CP."; }
            }
            catch (OperationCanceledException) { status.Text = "Cancelled. Source is unchanged."; }
            catch (Exception error) { status.Text = error is InvalidOperationException or ArgumentException ? error.Message : "CP is unavailable. Source and offline work are unchanged; retry or use Advanced connection."; }
            finally { running.Dispose(); running = null; Busy(false); }
        }
        analyze.Click += async (_, _) => await RunAsync(false);
        if (analyzeOnOpen) Opened += async (_, _) => await RunAsync(false);
        suggest.Click += async (_, _) => await RunAsync(true);
        preview.Click += async (_, _) =>
        {
            if (proposal is null || proposedFrom is null) return;
            if (!current(proposedFrom)) { status.Text = "Source changed. Request a fresh proposal."; accept.IsEnabled = false; return; }
            try { await new LineDiffWindow("Current unsaved text", proposedFrom.Text, "Proposed text", proposal).ShowDialog(this); reviewed = true; accept.IsEnabled = current(proposedFrom); }
            catch (Exception) { status.Text = "This proposal could not be compared. Request a smaller edit; source is unchanged."; }
        };
        accept.Click += (_, _) =>
        {
            if (!reviewed || proposal is null || proposedFrom is null) return;
            if (!current(proposedFrom)) { status.Text = "Source changed. Proposal was not applied."; accept.IsEnabled = false; return; }
            apply(proposedFrom, proposal); proposal = null; reviewed = false; Busy(false);
            status.Text = "Applied to the unsaved buffer. Undo reverses this change; Save remains your choice.";
        };
        cancel.Click += (_, _) => running?.Cancel();
        close.Click += (_, _) => Close();
        Closed += (_, _) => running?.Cancel();
        Content = new ScrollViewer { Content = new StackPanel { Margin = new Thickness(18), Spacing = 10, Children = { status, question, new WrapPanel { Children = { analyze } }, editRequest, new WrapPanel { Children = { suggest, preview, accept, cancel } }, output, close } } };
    }
}
