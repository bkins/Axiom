using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Axiom.Core;
using Axiom.Infrastructure;

namespace Axiom.App;

internal static class SemanticReviewProbe
{
    internal static string? Root { get; private set; }
    internal static void Configure(string root)
    {
        Root = Path.GetFullPath(root);
        if (Directory.Exists(Root)) throw new IOException("Choose a fresh semantic review evidence directory.");
        Directory.CreateDirectory(Root);
        Environment.SetEnvironmentVariable("AXIOM_STATE_ROOT", Path.Combine(Root, "state"));
    }

    internal static async Task StartAsync(MainWindow window)
    {
        CpWorkspaceAnalysisWindow? semantic = null;
        try
        {
            await window.StartupCompleted;
            var endpoint = new Uri(Environment.GetEnvironmentVariable("AXIOM_SEMANTIC_PROBE_ENDPOINT") ?? "http://127.0.0.1:5288");
            if (!endpoint.IsLoopback) throw new IOException("Synthetic semantic probe requires a loopback endpoint.");
            var key = Environment.GetEnvironmentVariable("AXIOM_SEMANTIC_PROBE_KEY") ?? throw new IOException("Synthetic probe credential not supplied.");
            Environment.SetEnvironmentVariable("AXIOM_SEMANTIC_PROBE_KEY", null);
            using var client = new CpDocumentAnalysisClient(endpoint, key);
            var sources = new[]
            {
                AnalysisSnapshot.Capture("buffer-1-Solar.md", "# Solar electricity\nSolar panels convert sunlight into electricity.\nThe system generates power during daylight.\nBatteries can store surplus energy for use at night."),
                AnalysisSnapshot.Capture("buffer-2-Wind.md", "# Wind electricity\nWind turbines use moving air to generate electrical power.\nOutput varies with wind speed.\nBatteries can store surplus energy when generation exceeds demand."),
                AnalysisSnapshot.Capture("buffer-3-Lunch.md", "# Lunch recipe\nCombine cooked pasta with tomatoes and olive oil.\nAdd basil and serve warm.\nStore leftover food in the refrigerator.")
            };
            var originalBuffer = window.EditorTextForTesting;
            var store = new SqliteStateStore(new AppDataPaths(Path.Combine(Root!, "probe-cache")));
            await store.InitializeAsync();
            var fresh = true;
            semantic = new CpWorkspaceAnalysisWindow(client, sources, _ => fresh, store);
            var dialog = semantic.ShowDialog<SemanticMatch?>(window);
            await Task.Delay(250);
            foreach (var selector in semantic.GetLogicalDescendants().OfType<CheckBox>().Where(control => control.Tag is AnalysisSnapshot)) selector.IsChecked = true;
            Control<CheckBox>(semantic, "CpReuseVectors").IsChecked = true;
            Click(semantic, "CpSemanticBuild");
            await WaitAsync(() => Finished(semantic) && Status(semantic).StartsWith("Index built:"));
            Click(semantic, "CpSemanticBuild");
            await WaitAsync(() => Finished(semantic) && Status(semantic).StartsWith("Index built:"));
            var reused = int.Parse(Regex.Match(Status(semantic), @"reused (\d+) sections").Groups[1].Value);
            Require(reused > 0, "Repeated build did not reuse verified vectors.");
            Control<ScrollViewer>(semantic, "CpSemanticScrollBody").Offset = new Vector(0, 10000);
            await Task.Delay(200);
            Capture(semantic, "reuse-status.png");
            Control<TextBox>(semantic, "CpSemanticQuery").Text = "Which passages discuss generating electrical energy from moving air?";
            Click(semantic, "CpSemanticSearch");
            await WaitAsync(() => Finished(semantic) && Control<ListBox>(semantic, "CpSemanticResults").ItemCount > 0);
            var matches = Control<ListBox>(semantic, "CpSemanticResults").Items.OfType<SemanticMatch>().ToArray();
            Require(matches[0].Section.DocumentKey == sources[1].ReferenceKey, "Wind did not rank first.");
            Control<TextBox>(semantic, "CpSemanticQuery").Text = "Compare how the two energy documents generate electricity and handle surplus power. Cite the reviewed sources and exclude the recipe.";
            Click(semantic, "CpAnalyzeReviewed");
            await WaitAsync(() => Finished(semantic) && Status(semantic).StartsWith("Derived answer:"));
            var resolved = Control<ListBox>(semantic, "CpResolvedCitations").Items.OfType<SemanticMatch>().ToArray();
            Require(resolved.Any(match => match.Section.DocumentKey == sources[0].ReferenceKey) && resolved.Any(match => match.Section.DocumentKey == sources[1].ReferenceKey), "Comparison has no validated ranges for both energy sources.");
            Require(resolved.All(match => match.Section.DocumentKey != sources[2].ReferenceKey), "Comparison cites the excluded recipe.");
            var answer = semantic.GetLogicalDescendants().OfType<TextBox>().Single(control => control.IsReadOnly && control.Name != "CpWorkspaceSnapshotReview").Text;
            await File.WriteAllTextAsync(Path.Combine(Root!, "derived-comparison.txt"), answer);
            fresh = false;
            Click(semantic, "CpSemanticBuild");
            await WaitAsync(() => Finished(semantic) && Status(semantic).StartsWith("Sources changed"));
            await Task.Delay(200);
            Capture(semantic, "freshness-refusal.png");
            var status = Control<TextBlock>(semantic, "CpWorkspaceStatus");
            var body = Control<ScrollViewer>(semantic, "CpSemanticScrollBody");
            Require(!body.GetLogicalDescendants().Contains(status) && status.IsEffectivelyVisible, "Status is inside scrolling content.");
            Click(semantic, "CpClearDerived");
            Require(Control<ListBox>(semantic, "CpSemanticResults").ItemCount == 0 && !Control<Button>(semantic, "CpSemanticSearch").IsEnabled, "Clear retained searchable results.");
            Require(window.EditorTextForTesting == originalBuffer, "Probe changed editor source.");
            semantic.Close(null); await dialog;
            await File.WriteAllTextAsync(Path.Combine(Root!, "result.json"), JsonSerializer.Serialize(new
            {
                Version = window.BuildIdentityForTesting.InformationalVersion,
                WindRankedFirst = true, VerifiedReuseSections = reused, ResolvedCitationRanges = resolved.Length,
                BothEnergySourcesCited = true, ExcludedRecipeNotCited = true, FixedStatusAfterScroll = true,
                StaleRefusal = true, ClearDerivedResults = true, SourceBufferPreserved = true,
                SyntheticSourcesOnly = true, HumanQualityAccepted = false
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception error) { await File.WriteAllTextAsync(Path.Combine(Root!, "error.txt"), error.ToString()); }
        finally { semantic?.Close(null); window.Close(); }
    }

    private static T Control<T>(Window window, string name) where T : Control => window.GetLogicalDescendants().OfType<T>().Single(control => control.Name == name);
    private static string Status(Window window) => Control<TextBlock>(window, "CpWorkspaceStatus").Text ?? "";
    private static bool Finished(Window window) => Control<Button>(window, "CpSemanticBuild").IsEffectivelyEnabled;
    private static void Click(Window window, string name)
    {
        var button = Control<Button>(window, name);
        Require(button.IsEffectivelyEnabled, "Probe button is disabled: " + name);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }
    private static async Task WaitAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(130);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new IOException("Semantic review probe timed out.");
            await Task.Delay(100);
        }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new IOException(message); }
    private static void Capture(Window window, string name)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.ClientSize.Width, (int)window.ClientSize.Height), new Vector(96, 96));
        bitmap.Render(window);
        bitmap.Save(Path.Combine(Root!, name), PngBitmapEncoderOptions.Default);
    }
}
