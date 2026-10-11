using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Axiom.Core;
using Axiom.Infrastructure;

namespace Axiom.App;

internal sealed class CpWorkspaceAnalysisWindow : Window
{
    internal CpWorkspaceAnalysisWindow(CpDocumentAnalysisClient client, IReadOnlyList<AnalysisSnapshot> candidates, Func<IReadOnlyList<AnalysisSnapshot>, bool> fresh, ISemanticDocumentResultStore? cache = null, IReadOnlyDictionary<string, DocumentClassification?>? classifications = null,
                                      Func<Task<IReadOnlyList<AnalysisSnapshot>>>? loadSaved = null,
                                      Func<IReadOnlyList<AnalysisSnapshot>, Task<bool>>? verifyFresh = null,
                                      AppearancePreference appearance = AppearancePreference.System)
    {
        Title = "Reviewed semantic search and cross-document analysis";
        Width = 880;
        Height = 800;
        MinWidth = 600;
        MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var selectors = candidates.Select(source => new CheckBox { Content = source.ReferenceKey, Tag = source }).ToList();
        Task<bool> FreshAsync(IReadOnlyList<AnalysisSnapshot> selected) => verifyFresh?.Invoke(selected) ?? Task.FromResult(fresh(selected));
        var review = new TextBox { Name = "CpWorkspaceSnapshotReview", IsReadOnly = true, AcceptsReturn = true, MinHeight = 120, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var question = new TextBox { Name = "CpSemanticQuery", PlaceholderText = "Explicit query or cross-document question" };
        var typeFilter = new TextBox { PlaceholderText = "Local saved Type filter (exact match, optional)" };
        var tagFilter = new TextBox { PlaceholderText = "Local saved Tag filter (exact match, optional)" };
        var status = new TextBlock { Name = "CpWorkspaceStatus", Text = "Select open buffers or explicitly add saved Markdown from the active workspace. Review exact snapshots before submitting; no background upload.", TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontWeight = Avalonia.Media.FontWeight.SemiBold };
        var addSaved = new Button { Name = "CpAddSavedSources", Content = "Add saved workspace documents for review", IsEnabled = loadSaved is not null };
        var sourceList = new StackPanel { Spacing = 6 };
        var build = new Button { Name = "CpSemanticBuild", Content = "Build reviewed semantic index" };
        var search = new Button { Name = "CpSemanticSearch", Content = "Semantic search", IsEnabled = false };
        var related = new Button { Content = "Related to first selected document", IsEnabled = false };
        var analyze = new Button { Name = "CpAnalyzeReviewed", Content = "Analyze reviewed documents" };
        var cancel = new Button { Content = "Cancel request", IsEnabled = false };
        var clear = new Button { Name = "CpClearDerived", Content = "Clear derived index/results" };
        var persistent = new CheckBox { Name = "CpReuseVectors", Content = "Reuse/store derived vectors in local Axiom state (requires verified model digest)", IsEnabled = cache is not null };
        var clearCache = new Button { Content = "Clear all persisted semantic results", IsEnabled = cache is not null };
        var results = new ListBox { Name = "CpSemanticResults", MinHeight = 120 };
        var answer = new TextBox { IsReadOnly = true, AcceptsReturn = true, MinHeight = 120, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var citations = new ListBox { Name = "CpResolvedCitations", MinHeight = 60 };
        var openCitation = new Button { Content = "Open selected cited source range" };
        var open = new Button { Content = "Open selected source section" };
        var close = new Button { Content = "Close", IsCancel = true };
        var help = OfflineHelp.TopicButton(this, "Semantic search: step-by-step test", appearance);
        var index = new CpSemanticIndex();
        CancellationTokenSource? cancellation = null;
        var loadingSaved = false;
        AnalysisSnapshot[] Selected() => selectors.Where(selector => selector.IsChecked == true).Select(selector => (AnalysisSnapshot)selector.Tag!).ToArray();
        void RefreshReview()
        {
            review.Text = string.Join("\n\n", Selected().Select(source => $"SOURCE {source.ReferenceKey}\nHASH {source.SourceHash}\n{source.Text}"));
            search.IsEnabled = related.IsEnabled = index.AppliesTo(Selected());
        }
        foreach (var selector in selectors) selector.IsCheckedChanged += (_, _) => RefreshReview();
        async Task RunAsync(Func<AnalysisSnapshot[], CancellationToken, Task> operation)
        {
            if (loadingSaved || cancellation is not null) return;
            var selected = Selected();
            if (selected.Length is < 1 or > 16 || selected.Sum(source => (long)source.Text.Length) > 200_000)
            { status.Text = "Select 1–16 nonempty documents totaling at most 200,000 characters."; return; }
            cancellation = new();
            build.IsEnabled = analyze.IsEnabled = search.IsEnabled = related.IsEnabled = clear.IsEnabled = false;
            persistent.IsEnabled = clearCache.IsEnabled = false;
            addSaved.IsEnabled = false;
            open.IsEnabled = openCitation.IsEnabled = false;
            foreach (var selector in selectors) selector.IsEnabled = false;
            question.IsEnabled = false;
            cancel.IsEnabled = true;
            try
            {
                if (!await FreshAsync(selected)) throw new InvalidOperationException("Sources changed or have external conflicts. Reopen to review fresh snapshots.");
                await operation(selected, cancellation.Token);
            }
            catch (OperationCanceledException) { status.Text = "Cancelled; source retained. A partial index is never published."; }
            catch (Exception exception) { status.Text = exception is ArgumentException or InvalidOperationException ? exception.Message : "CP returned an invalid or unavailable response."; }
            finally
            {
                cancellation.Dispose(); cancellation = null;
                build.IsEnabled = analyze.IsEnabled = clear.IsEnabled = true;
                persistent.IsEnabled = clearCache.IsEnabled = cache is not null;
                addSaved.IsEnabled = loadSaved is not null;
                open.IsEnabled = openCitation.IsEnabled = true;
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
            status.Text = $"Index built: {index.Provider}/{index.Model}; digest {index.ModelRevision ?? "unavailable (no persistent reuse)"}; reused {index.ReusedSections} sections. {(await FreshAsync(selected) ? "Hashes current." : "STALE sources.")}";
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
            status.Text = "Cosine similarity is a ranking score, not a probability. Suggestions never create relationships. " + (await FreshAsync(selected) ? "Source hashes current." : "STALE sources; reopen.");
        });
        search.Click += async (_, _) => await QueryAsync(false);
        related.Click += async (_, _) => await QueryAsync(true);
        analyze.Click += async (_, _) => await RunAsync(async (selected, token) =>
        {
            var result = await client.GetInsightsAsync(selected, question.Text, token);
            answer.Text = AnalysisCitationPolicy.Report(result);
            citations.ItemsSource = AnalysisCitationPolicy.Resolve(result).Select(section => new SemanticMatch(section, 0)).ToArray();
            var resolvedCount = AnalysisCitationPolicy.Resolve(result).Count;
            status.Text = $"Derived answer: {result.Provider}/{result.Model ?? "not attested"}; {result.CreatedAt:u}; {result.Provenance}. {(await FreshAsync(selected) ? "Hashes current." : "STALE sources.")} "
                        + (resolvedCount == 0 ? "No validated citations: answer is unverified; cited-source navigation is unavailable. " : $"{resolvedCount} validated citation ranges; review claim support yourself. ")
                        + "No source edits.";
        });
        clear.Click += (_, _) => { index = new(); results.ItemsSource = null; citations.ItemsSource = null; answer.Text = ""; search.IsEnabled = related.IsEnabled = false; status.Text = "Derived session index and results discarded."; };
        clearCache.Click += async (_, _) =>
        {
            if (cache is null) return;
            try { await cache.ClearAsync(); status.Text = "All persisted semantic results cleared; Markdown, metadata and recovery unchanged."; }
            catch { status.Text = "Could not clear semantic results."; }
        };
        cancel.Click += (_, _) => cancellation?.Cancel();
        open.Click += async (_, _) =>
        {
            if (results.SelectedItem is not SemanticMatch match || !Selected().Any(source => source.ReferenceKey == match.Section.DocumentKey && source.SourceHash == match.Section.DocumentHash) || !await FreshAsync(Selected())) { status.Text = "Select a current source match first."; return; }
            Close(match);
        };
        close.Click += (_, _) => Close(null);
        openCitation.Click += async (_, _) =>
        {
            if (citations.SelectedItem is not SemanticMatch match || !Selected().Any(source => source.ReferenceKey == match.Section.DocumentKey && source.SourceHash == match.Section.DocumentHash) || !await FreshAsync(Selected()))
            { status.Text = "Select a resolved citation with current source hashes first."; return; }
            Close(match);
        };
        Closed += (_, _) => cancellation?.Cancel();
        results.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<SemanticMatch>((match, _) => new TextBlock
        {
            Text = match is null ? "" : $"{match.Section.DocumentKey} · {match.Section.Heading} · line {match.Section.LineNumber} · similarity {match.Similarity:F3}"
          , TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });
        citations.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<SemanticMatch>((match, _) => new TextBlock
        { Text = match is null ? "" : $"{match.Section.DocumentKey} · {match.Section.Heading}", TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        foreach (var selector in selectors) sourceList.Children.Add(selector);
        addSaved.Click += async (_, _) =>
        {
            if (loadSaved is null) return;
            if (cancellation is not null || loadingSaved) return;
            loadingSaved = true;
            addSaved.IsEnabled = false;
            try
            {
                foreach (var source in await loadSaved())
                {
                    if (selectors.Any(selector => ((AnalysisSnapshot)selector.Tag!).ReferenceKey == source.ReferenceKey)) continue;
                    var selector = new CheckBox { Content = source.ReferenceKey, Tag = source };
                    selector.IsCheckedChanged += (_, _) => RefreshReview();
                    selectors.Add(selector);
                    sourceList.Children.Add(selector);
                }
                status.Text = "Saved snapshots added locally, unchecked. Select and review before submitting; no content has been sent.";
            }
            catch (Exception exception) { status.Text = exception is InvalidOperationException ? exception.Message : "Saved workspace sources could not be read safely."; }
            finally { loadingSaved = false; addSaved.IsEnabled = true; }
        };
        var actions = new StackPanel
        {
            Name = "CpSemanticActionBar", Margin = new Thickness(18, 10), Spacing = 10,
            Children =
            {
                new Border { Name = "CpSemanticStatusBanner", Padding = new Thickness(12), BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(6),
                    Child = new StackPanel { Spacing = 6, Children = { new TextBlock { Text = "Operation status", FontWeight = Avalonia.Media.FontWeight.Bold }, status } } },
                new WrapPanel { Children = { build, search, related, analyze, cancel, clear, close } }
            }
        };
        var statusBanner = actions.Children.OfType<Border>().Single();
        statusBanner.Bind(Border.BorderBrushProperty, this.GetResourceObservable("SystemControlHighlightAccentBrush"));
        var body = new ScrollViewer { Name = "CpSemanticScrollBody", Content = new StackPanel
        {
            Margin = new Thickness(18), Spacing = 10
          , Children = { help, addSaved, sourceList, review, question, typeFilter, tagFilter, persistent, clearCache
                      , results, open, answer, citations, openCitation }
        } };
        DockPanel.SetDock(actions, Dock.Bottom);
        Content = new DockPanel { Children = { actions, body } };
    }
}
