using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Axiom.Core;
using Axiom.Infrastructure;

namespace Axiom.App;

internal sealed class CpWorkspaceAnalysisWindow : Window
{
    internal CpWorkspaceAnalysisWindow(CpDocumentAnalysisClient client, IReadOnlyList<AnalysisSnapshot> candidates, Func<IReadOnlyList<AnalysisSnapshot>, bool> fresh, ISemanticDocumentResultStore? cache = null, IReadOnlyDictionary<string, DocumentClassification?>? classifications = null)
    {
        Title = "Reviewed semantic search and cross-document analysis";
        Width = 880;
        Height = 800;
        MinWidth = 600;
        MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var selectors = candidates.Select(source => new CheckBox { Content = source.ReferenceKey, Tag = source }).ToArray();
        var review = new TextBox { Name = "CpWorkspaceSnapshotReview", IsReadOnly = true, AcceptsReturn = true, MinHeight = 120, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var question = new TextBox { Name = "CpSemanticQuery", PlaceholderText = "Explicit query or cross-document question" };
        var typeFilter = new TextBox { PlaceholderText = "Local saved Type filter (exact match, optional)" };
        var tagFilter = new TextBox { PlaceholderText = "Local saved Tag filter (exact match, optional)" };
        var status = new TextBlock { Text = "Select open document buffers; review the exact text before submitting. No workspace scanning or background upload.", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var build = new Button { Name = "CpSemanticBuild", Content = "Build reviewed semantic index" };
        var search = new Button { Name = "CpSemanticSearch", Content = "Semantic search", IsEnabled = false };
        var related = new Button { Content = "Related to first selected document", IsEnabled = false };
        var analyze = new Button { Content = "Analyze reviewed documents" };
        var cancel = new Button { Content = "Cancel request", IsEnabled = false };
        var clear = new Button { Content = "Clear derived index/results" };
        var persistent = new CheckBox { Content = "Reuse/store derived vectors in local Axiom state (requires verified model digest)", IsEnabled = cache is not null };
        var clearCache = new Button { Content = "Clear all persisted semantic results", IsEnabled = cache is not null };
        var results = new ListBox { Name = "CpSemanticResults", MinHeight = 120 };
        var answer = new TextBox { IsReadOnly = true, AcceptsReturn = true, MinHeight = 120, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var open = new Button { Content = "Open selected source section" };
        var close = new Button { Content = "Close", IsCancel = true };
        var index = new CpSemanticIndex();
        CancellationTokenSource? cancellation = null;
        AnalysisSnapshot[] Selected() => selectors.Where(selector => selector.IsChecked == true).Select(selector => (AnalysisSnapshot)selector.Tag!).ToArray();
        void RefreshReview()
        {
            review.Text = string.Join("\n\n", Selected().Select(source => $"SOURCE {source.ReferenceKey}\nHASH {source.SourceHash}\n{source.Text}"));
            search.IsEnabled = related.IsEnabled = index.AppliesTo(Selected());
        }
        foreach (var selector in selectors) selector.IsCheckedChanged += (_, _) => RefreshReview();
        async Task RunAsync(Func<AnalysisSnapshot[], CancellationToken, Task> operation)
        {
            var selected = Selected();
            if (selected.Length is < 1 or > 16 || selected.Sum(source => (long)source.Text.Length) > 200_000)
            { status.Text = "Select 1–16 nonempty documents totaling at most 200,000 characters."; return; }
            if (!fresh(selected)) { status.Text = "Sources changed or have external conflicts. Close and reopen to review fresh snapshots."; return; }
            cancellation = new();
            build.IsEnabled = analyze.IsEnabled = search.IsEnabled = related.IsEnabled = clear.IsEnabled = false;
            persistent.IsEnabled = clearCache.IsEnabled = false;
            foreach (var selector in selectors) selector.IsEnabled = false;
            question.IsEnabled = false;
            cancel.IsEnabled = true;
            try { await operation(selected, cancellation.Token); }
            catch (OperationCanceledException) { status.Text = "Cancelled; source retained. A partial index is never published."; }
            catch (Exception exception) { status.Text = exception is ArgumentException or InvalidOperationException ? exception.Message : "CP returned an invalid or unavailable response."; }
            finally
            {
                cancellation.Dispose(); cancellation = null;
                build.IsEnabled = analyze.IsEnabled = clear.IsEnabled = true;
                persistent.IsEnabled = clearCache.IsEnabled = cache is not null;
                foreach (var selector in selectors) selector.IsEnabled = true;
                question.IsEnabled = true;
                cancel.IsEnabled = false;
                search.IsEnabled = related.IsEnabled = index.AppliesTo(Selected());
            }
        }
        build.Click += async (_, _) => await RunAsync(async (selected, token) =>
        {
            status.Text = "Submitting reviewed source sections for embeddings…";
            await index.BuildAsync(client, selected, token, persistent.IsChecked == true ? cache : null);
            status.Text = $"Index built: {index.Provider}/{index.Model}; digest {index.ModelRevision ?? "unavailable (no persistent reuse)"}; reused {index.ReusedSections} sections. {(fresh(selected) ? "Hashes current." : "STALE sources.")}";
        });
        async Task QueryAsync(bool relationships) => await RunAsync(async (selected, token) =>
        {
            if (!index.AppliesTo(selected)) throw new InvalidOperationException("Reviewed source set changed; rebuild first.");
            var query = relationships ? selected[0].Text[..Math.Min(selected[0].Text.Length, 8000)] : question.Text ?? "";
            var matches = await index.SearchAsync(client, query, relationships ? selected[0].ReferenceKey : null, token);
            results.ItemsSource = matches.Where(match =>
            {
                var classification = classifications?.GetValueOrDefault(match.Section.DocumentKey);
                return (string.IsNullOrWhiteSpace(typeFilter.Text) || string.Equals(classification?.Type, typeFilter.Text.Trim(), StringComparison.OrdinalIgnoreCase))
                    && (string.IsNullOrWhiteSpace(tagFilter.Text) || classification?.Tags.Any(tag => string.Equals(tag, tagFilter.Text.Trim(), StringComparison.OrdinalIgnoreCase)) == true);
            }).ToArray();
            status.Text = "Cosine similarity is a ranking score, not a probability. Suggestions never create relationships. " + (fresh(selected) ? "Source hashes current." : "STALE sources; reopen.");
        });
        search.Click += async (_, _) => await QueryAsync(false);
        related.Click += async (_, _) => await QueryAsync(true);
        analyze.Click += async (_, _) => await RunAsync(async (selected, token) =>
        {
            var result = await client.GetInsightsAsync(selected, question.Text, token);
            answer.Text = result.Text;
            status.Text = $"Derived answer: {result.Provider}/{result.Model ?? "not attested"}; {result.CreatedAt:u}; {result.Provenance}. {(fresh(selected) ? "Hashes current." : "STALE sources.")} Review citations/claims; no source edits.";
        });
        clear.Click += (_, _) => { index = new(); results.ItemsSource = null; answer.Text = ""; search.IsEnabled = related.IsEnabled = false; status.Text = "Derived session index and results discarded."; };
        clearCache.Click += async (_, _) =>
        {
            if (cache is null) return;
            try { await cache.ClearAsync(); status.Text = "All persisted semantic results cleared; Markdown, metadata and recovery unchanged."; }
            catch { status.Text = "Could not clear semantic results."; }
        };
        cancel.Click += (_, _) => cancellation?.Cancel();
        open.Click += (_, _) =>
        {
            if (results.SelectedItem is not SemanticMatch match || !fresh(Selected())) { status.Text = "Select a current source match first."; return; }
            Close(match);
        };
        close.Click += (_, _) => Close(null);
        Closed += (_, _) => cancellation?.Cancel();
        results.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<SemanticMatch>((match, _) => new TextBlock
        {
            Text = match is null ? "" : $"{match.Section.DocumentKey} · {match.Section.Heading} · line {match.Section.LineNumber} · similarity {match.Similarity:F3}"
          , TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });
        var sourceList = new StackPanel { Spacing = 6 };
        foreach (var selector in selectors) sourceList.Children.Add(selector);
        Content = new ScrollViewer { Content = new StackPanel
        {
            Margin = new Thickness(18), Spacing = 10
          , Children = { status, sourceList, review, question, typeFilter, tagFilter, persistent, clearCache
                      , new WrapPanel { Children = { build, search, related, analyze, cancel, clear } }
                      , results, open, answer, close }
        } };
    }
}
