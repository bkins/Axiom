using System.Text.Json;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Input.Platform;
using Axiom.Core;
using Axiom.Infrastructure;

namespace Axiom.App;

internal static class CpHelpProbe
{
    internal static string? Root { get; private set; }

    internal static void Configure(string root)
    {
        Root = Path.GetFullPath(root);
        if (Directory.Exists(Root)) throw new IOException("Choose a fresh CP/Help evidence directory.");
        Directory.CreateDirectory(Root);
        Environment.SetEnvironmentVariable("AXIOM_STATE_ROOT", Path.Combine(Root, "state"));
    }

    internal static async Task StartAsync(MainWindow window)
    {
        CpDocumentAnalysisClient? client = null;
        window.UnsavedChoiceForTesting = _ => Task.FromResult("Discard");
        try
        {
            await window.StartupCompleted;
            var workspace = Path.Combine(Root!, "workspace");
            Directory.CreateDirectory(workspace);
            var path = Path.Combine(workspace, "source.md");
            await File.WriteAllTextAsync(path, "# Saved source\n");
            await window.SwitchWorkspaceAsync(workspace);
            await window.OpenDocumentAsync(path);
            window.SetEditorTextForTesting("# Unsaved protected buffer\n");
            var draft = window.EditorTextForTesting;
            var commands = window.RegisteredCommandsForTesting;
            var guide = new MarkdownStructureService().Parse(OfflineHelp.Read(commands));
            foreach (var command in commands)
                Require(guide.Headings.Any(heading => heading.Text == OfflineHelp.CommandTopic(command.Id)), "Missing command Help: " + command.Id);
            await window.ExecuteCommandAsync("help.semantic");
            var panel = window.PersistentHelpForTesting!;
            await WaitAsync(async () => await ContainsAsync(panel.PreviewForTesting, "Solar.md"));
            Require(panel.SelectedTopicForTesting == "Semantic search: step-by-step test", "Help command did not select the semantic exercise.");
            await WaitAsync(async () => await ContainsAsync(panel.PreviewForTesting, "All commands and default shortcuts"));
            var cpHeading = guide.Headings.Single(heading => heading.Text == "CP connection and reviewed analysis");
            await panel.PreviewForTesting.InvokeScript("document.querySelector('a[href=\"#" + cpHeading.Fragment + "\"]').click()");
            await WaitAsync(async () => (await panel.PreviewForTesting.InvokeScript("Math.abs(document.getElementById(" + JsonSerializer.Serialize(cpHeading.Fragment) + ").getBoundingClientRect().top) < 120"))?.Trim().Trim('"') == "true");
            await CheckWalkthroughAsync(window);

            var sources = new[]
            {
                AnalysisSnapshot.Capture("Solar.md", "# Solar electricity\nSolar panels convert sunlight into electricity. Batteries store surplus energy for night.")
              , AnalysisSnapshot.Capture("Wind.md", "# Wind electricity\nWind turbines use moving air to generate electrical power. Output varies with wind speed.")
              , AnalysisSnapshot.Capture("Lunch.md", "# Lunch recipe\nCombine pasta with tomatoes and olive oil. Add basil and serve warm.")
            };
            var connection = new CpAnalysisWindow(sources[0], () => true, () => client, candidate => client = candidate);
            var connectionDialog = connection.ShowDialog(window);
            await Task.Delay(300);
            await CheckModalHelpAsync(connection, "CP connection and reviewed analysis");
            Click(connection, "CpCopyLocalCredential");
            await WaitAsync(() => Task.FromResult(Control<TextBlock>(connection, "CpConnectionStatus").Text?.StartsWith("Local credential copied", StringComparison.Ordinal) == true));
            Require(client is null, "Copy helper connected or submitted a document.");
            Click(connection, "CpStartLocal");
            await WaitAsync(() => Task.FromResult(Control<TextBlock>(connection, "CpConnectionStatus").Text?.StartsWith("Local CP is running", StringComparison.Ordinal) == true));
            Require(client is null, "Start helper connected or submitted a document.");
            var clipboard = TopLevel.GetTopLevel(connection)?.Clipboard ?? throw new IOException("Native clipboard unavailable.");
            Control<TextBox>(connection, "CpCredential").Text = await clipboard.TryGetTextAsync();
            Require(Control<TextBox>(connection, "CpEndpoint").Text == "http://127.0.0.1:5277", "Start did not select the local endpoint.");
            Click(connection, "CpConnect");
            await WaitAsync(() => Task.FromResult(client is not null));
            Require(Control<TextBox>(connection, "CpCredential").Text == "", "Successful connection did not clear credential entry.");
            connection.Close(); await connectionDialog;

            var semantic = new CpWorkspaceAnalysisWindow(client!, sources, _ => true);
            var semanticDialog = semantic.ShowDialog<SemanticMatch?>(window);
            await Task.Delay(250);
            await CheckModalHelpAsync(semantic, "Semantic search: step-by-step test");
            foreach (var selector in semantic.GetLogicalDescendants().OfType<CheckBox>().Where(selector => selector.Tag is AnalysisSnapshot)) selector.IsChecked = true;
            Click(semantic, "CpSemanticBuild");
            await WaitAsync(() => Task.FromResult(Control<TextBlock>(semantic, "CpWorkspaceStatus").Text?.StartsWith("Index built:", StringComparison.Ordinal) == true));
            Control<TextBox>(semantic, "CpSemanticQuery").Text = "Which passages discuss generating electrical energy from moving air?";
            Click(semantic, "CpSemanticSearch");
            var results = Control<ListBox>(semantic, "CpSemanticResults");
            await WaitAsync(() => Task.FromResult(results.ItemCount > 0));
            var matches = results.Items.OfType<SemanticMatch>().ToArray();
            Require(matches.Any(match => match.Section.DocumentKey == "Wind.md"), "Semantic query did not return the selected wind source.");
            await File.WriteAllTextAsync(Path.Combine(Root!, "semantic-rankings.json"), JsonSerializer.Serialize(matches.Select(match => new { match.Section.DocumentKey, match.Section.Heading, match.Similarity }), new JsonSerializerOptions { WriteIndented = true }));
            semantic.Close(null); await semanticDialog;
            Require(window.EditorTextForTesting == draft && await File.ReadAllTextAsync(path) == "# Saved source\n", "CP/Help changed or saved editor source.");
            await File.WriteAllTextAsync(Path.Combine(Root!, "result.json"), JsonSerializer.Serialize(new
            {
                Version = window.BuildIdentityForTesting.InformationalVersion
              , AllRegisteredCommandsHaveFeatureHelp = true
              , CommandCount = commands.Count
              , CurrentBundledHelpAndSemanticTopic = true
              , FeatureReferenceLinksNavigate = true
              , CurrentWalkthroughNavigation = true
              , ModalCpAndSemanticHelpNavigation = true
              , LocalCopyAndStartButtonsWithoutSubmission = true
              , ActualClipboardCredentialConnectAndClear = true
              , RealReviewedEmbeddingBuildAndSemanticQuery = true
              , SourceAndDirtyBufferPreserved = true
              , SyntheticSourcesOnly = true
              , HumanQualityAccepted = false
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception error) { await File.WriteAllTextAsync(Path.Combine(Root!, "error.txt"), error.ToString()); }
        finally
        {
            client?.Dispose();
            foreach (var child in window.OwnedWindows.ToArray()) child.Close();
            window.Close();
        }
    }

    private static T Control<T>(Window window, string name) where T : Control => window.GetLogicalDescendants().OfType<T>().Single(control => control.Name == name);
    private static void Click(Window window, string name)
    {
        var button = Control<Button>(window, name);
        Require(button.IsEffectivelyEnabled, "Button is disabled: " + name);
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
    }
    private static void Require(bool condition, string message) { if (!condition) throw new IOException(message); }
    private static async Task<bool> ContainsAsync(Avalonia.Controls.NativeWebView view, string text)
    {
        try { return (await view.InvokeScript("document.body.innerText.includes(" + JsonSerializer.Serialize(text) + ")"))?.Trim().Trim('"') == "true"; }
        catch { return false; }
    }
    private static async Task CheckModalHelpAsync(Window owner, string heading)
    {
        Click(owner, "ContextualHelp");
        HelpWindow? help = null;
        await WaitAsync(() => Task.FromResult((help = owner.OwnedWindows.OfType<HelpWindow>().FirstOrDefault()) is not null));
        await WaitAsync(async () => await ContainsAsync(help!.PreviewForTesting, heading));
        var topic = Control<ComboBox>(help!, "HelpTopics").SelectedItem as DocumentHeading;
        Require(topic?.Text == heading, "Contextual Help selected the wrong topic.");
        help!.Close();
    }
    private static async Task CheckWalkthroughAsync(Window owner)
    {
        var view = new NativeWebView();
        view.EnvironmentRequested += (_, args) =>
        {
            if (args is Avalonia.Platform.WindowsWebView2EnvironmentRequestedEventArgs windows) windows.UserDataFolder = Path.Combine(Root!, "walkthrough-cache");
        };
        view.AdapterCreated += (_, _) => view.Navigate(new Uri(Path.Combine(AppContext.BaseDirectory, "Walkthrough", "index.html")));
        var dialog = new Window { Title = "Current walkthrough probe", Width = 880, Height = 700, Content = view };
        var operation = dialog.ShowDialog(owner);
        await WaitAsync(async () =>
        {
            try { return (await view.InvokeScript("document.querySelector('#counter')?.textContent === '1 / 19'"))?.Trim().Trim('"') == "true"; }
            catch { return false; }
        });
        await view.InvokeScript("document.querySelector('#next').click()");
        Require((await view.InvokeScript("document.querySelector('#counter').textContent === '2 / 19'"))?.Trim().Trim('"') == "true", "Walkthrough Next failed.");
        await view.InvokeScript("(() => { const selector = document.querySelector('#jump'); selector.value='18'; selector.dispatchEvent(new Event('change')); })()");
        Require((await view.InvokeScript("document.querySelector('#counter').textContent === '19 / 19' && document.querySelector('#image-link').hidden && !document.body.innerText.includes('1.0.')"))?.Trim().Trim('"') == "true", "Walkthrough topics or current instructions failed.");
        dialog.Close(); await operation;
    }
    private static async Task WaitAsync(Func<Task<bool>> check)
    {
        for (var attempt = 0; attempt < 500; attempt++) { if (await check()) return; await Task.Delay(100); }
        throw new TimeoutException("Native CP/Help check timed out.");
    }
}
