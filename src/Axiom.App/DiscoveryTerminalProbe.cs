using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Axiom.Core;

namespace Axiom.App;

internal static class DiscoveryTerminalProbe
{
    internal static string? Root { get; private set; }
    internal static void Configure(string root)
    {
        Root = Path.GetFullPath(root);
        if (Directory.Exists(Root)) throw new IOException("Choose a fresh discovery/terminal evidence directory.");
        Directory.CreateDirectory(Root); Environment.SetEnvironmentVariable("AXIOM_STATE_ROOT", Path.Combine(Root, "state"));
    }
    internal static async Task StartAsync(MainWindow window)
    {
        try
        {
            await window.StartupCompleted;
            var workspace = Path.Combine(Root!, "repository with spaces & 'quotes'"); Directory.CreateDirectory(workspace);
            Git(workspace, "init", "-q"); Git(workspace, "config", "user.name", "Axiom probe"); Git(workspace, "config", "user.email", "probe@axiom.local");
            await File.WriteAllTextAsync(Path.Combine(workspace, "a.md"), "# Plan\n\nFind retained discovery passage.\n");
            await File.WriteAllTextAsync(Path.Combine(workspace, "b.md"), "# Related\n\nAnother discovery passage.\n");
            Git(workspace, "add", "."); Git(workspace, "commit", "-qm", "Baseline");
            await File.WriteAllTextAsync(Path.Combine(workspace, "untracked.txt"), "saved terminal fixture");
            await window.SwitchWorkspaceAsync(workspace); await window.OpenDocumentAsync(Path.Combine(workspace, "a.md"));
            window.SetEditorTextForTesting(window.EditorTextForTesting + "Unsaved draft stays in editor.\n");
            var draft = window.EditorTextForTesting; var tab = window.TabsForTesting.Last();
            var query = window.FindControl<TextBox>("WorkspaceSearchBox")!; query.Text = "discovery";
            await window.SearchWorkspaceForTestingAsync("discovery");
            var results = window.FindControl<ListBox>("WorkspaceSearchResults")!;
            Require(results.ItemCount == 2, "Offline discovery did not return saved passages."); results.SelectedIndex = 0;
            var match = (WorkspaceSearchResult)results.SelectedItem!;
            await window.OpenSearchResultForTestingAsync(match);
            await window.ExecuteCommandAsync("navigation.returnToDiscovery");
            foreach (var placement in new[] { DiscoveryPlacement.Bottom, DiscoveryPlacement.Right })
            {
                await window.ExecuteCommandAsync("view.discoveryPlacement"); window.UpdateLayout();
                Require(window.EditorPreferencesForTesting.DiscoveryPlacement == placement, "Layout did not persist.");
                Require(query.Text == "discovery" && results.ItemCount == 2 && Equals(results.SelectedItem, match), "Discovery layout lost query/results/selection.");
                Require(tab.Session.Text == draft && tab.Session.IsDirty, "Layout changed or saved draft.");
            }
            window.ApplyEditorPreferencesForTesting(window.EditorPreferencesForTesting with { CommandPlacement = CommandPlacement.Side });
            Require(window.FindControl<Border>("SideCommandHost")!.IsVisible && window.VisibleToolbarIdsForTesting.Contains("document.save"), "Side toolbar unavailable.");
            await window.ExecuteCommandAsync("view.mode.zen"); Require(!window.FindControl<Border>("SideCommandHost")!.IsVisible, "Zen retained side commands.");
            await window.ExecuteCommandAsync("view.mode.standard"); Require(window.FindControl<Border>("SideCommandHost")!.IsVisible, "Side toolbar did not restore.");
            await window.ExecuteCommandAsync("workspace.git");
            var terminal = window.GitTerminalForTesting!; var browser = terminal.BrowserForTesting;
            terminal.InputForTesting = input => File.AppendAllText(Path.Combine(Root!, "input-events.jsonl"), JsonSerializer.Serialize(input) + "\n");
            await WaitAsync(() => Task.FromResult(terminal.CanPrepareForTesting));
            window.Topmost = true; window.Activate(); browser.Focus(); await browser.InvokeScript("window.axiomTerminal.focus()");
            Require(await terminal.PrepareAsync("git add ."), "Recipe could not fill empty prompt: " + terminal.StatusForTesting);
            await Task.Delay(200);
            Require(Git(workspace, "diff", "--cached", "--name-only") == "", "Recipe executed before Enter.");
            Require(!await terminal.PrepareAsync("git push"), "Recipe overwrote pending input.");
            var modes = (TabControl)window.FindControl<ContentControl>("GitPanelHost")!.Content!;
            var sameProcess = terminal.ProcessIdForTesting;
            modes.SelectedIndex = 1; window.UpdateLayout(); await Task.Delay(80);
            modes.SelectedIndex = 0; window.UpdateLayout(); await Task.Delay(160);
            Require(terminal.ProcessIdForTesting == sameProcess && !terminal.CanPrepareForTesting, "Switching Changes lost shell or pending command.");
            // Physical Enter is sent only to the probe's own window and focused terminal.
            window.Activate(); browser.Focus(); await browser.InvokeScript("window.axiomTerminal.focus()"); await Task.Delay(100);
            NativeProbeInput.Key(0x0D);
            await WaitAsync(() => Task.FromResult(terminal.CanPrepareForTesting && Git(workspace, "diff", "--cached", "--name-only").Contains("untracked.txt")));
            Require(await File.ReadAllTextAsync(Path.Combine(workspace, "a.md")) != draft && tab.Session.Text == draft, "Terminal saved the editor buffer.");
            Require(await terminal.PrepareAsync("git status --short"), "Status recipe unavailable after Enter.");
            browser.Focus(); await browser.InvokeScript("window.axiomTerminal.focus()"); NativeProbeInput.Key(0x0D);
            await WaitAsync(() => Task.FromResult(terminal.CanPrepareForTesting));
            await WaitAsync(async () => await browser.InvokeScript("Array.from({length:window.axiomTerminal.buffer.active.length},(_,i)=>window.axiomTerminal.buffer.active.getLine(i).translateToString()).join('\\n').includes('untracked.txt')") == "true");
            var screen = await browser.InvokeScript("JSON.stringify(Array.from({length:window.axiomTerminal.buffer.active.length},(_,i)=>window.axiomTerminal.buffer.active.getLine(i).translateToString()).join('\\n'))");
            await File.WriteAllTextAsync(Path.Combine(Root!, "terminal-screen.json"), screen);
            Require(screen?.Contains("untracked.txt") == true, "Real terminal output did not render.");
            Require(await terminal.PrepareAsync("Start-Sleep -Seconds 10"), "Could not prepare running-command fixture.");
            browser.Focus(); await browser.InvokeScript("window.axiomTerminal.focus()"); NativeProbeInput.Key(0x0D); await Task.Delay(300);
            Require(!await terminal.PrepareAsync("git push"), "Recipe injected into a running command.");
            NativeProbeInput.Key(0x43, control: true); await WaitAsync(() => Task.FromResult(terminal.CanPrepareForTesting));
            Require(await terminal.PrepareAsync("cd .."), "Could not prepare directory-change fixture.");
            browser.Focus(); await browser.InvokeScript("window.axiomTerminal.focus()"); NativeProbeInput.Key(0x0D);
            await WaitAsync(() => Task.FromResult(terminal.StatusForTesting.StartsWith("Current directory differs", StringComparison.Ordinal)));
            Require(!await terminal.PrepareAsync("git add ."), "Recipe used a changed directory without checking actual shell location.");
            var retained = terminal.ProcessIdForTesting;
            await window.ExecuteCommandAsync("workspace.git"); Require(window.GitTerminalForTesting == terminal && terminal.ProcessIdForTesting == retained, "Reopening Git discarded the terminal session.");
            window.Width = 1040; window.Height = 640; window.UpdateLayout(); await Task.Delay(120);
            Require(await browser.InvokeScript("window.axiomTerminal.cols >= 20 && window.axiomTerminal.rows >= 5") == "true", "Terminal did not resize.");
            browser.Focus(); await browser.InvokeScript("window.axiomTerminal.focus()");
            NativeProbeInput.Type(window, "cd '" + workspace.Replace("'", "''") + "'"); NativeProbeInput.Key(0x0D);
            await WaitAsync(() => Task.FromResult(terminal.CanPrepareForTesting));
            Require(await terminal.PrepareAsync("exit"), "Could not prepare exit fixture."); NativeProbeInput.Key(0x0D);
            await WaitAsync(() => Task.FromResult(terminal.StatusForTesting.StartsWith("Shell exited", StringComparison.Ordinal)));
            var restart = terminal.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, "Restart shell"));
            restart.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            await WaitAsync(() => Task.FromResult(terminal.CanPrepareForTesting && terminal.ProcessIdForTesting != retained));
            await browser.InvokeScript("window.chrome.webview.postMessage(JSON.stringify({token:window.axiomDrain().token,input:'',response:false,rejected:true}))");
            await WaitAsync(() => Task.FromResult(terminal.StatusForTesting.StartsWith("Input limit reached", StringComparison.Ordinal)));
            Require(!await terminal.PrepareAsync("git push"), "Recipe bypassed rejected-input guard.");
            browser.Focus(); await browser.InvokeScript("window.axiomTerminal.focus()"); NativeProbeInput.Key(0x43, control: true);
            await WaitAsync(() => Task.FromResult(terminal.CanPrepareForTesting));
            var baseline = new List<object>();
            foreach (var (file, source) in new[] { ("Guide.md", @"C:\Users\benho\source\Application Documentation\Axiom\07 - User Documentation\Axiom v1 User Guide.md"), ("Strategy.md", @"C:\Users\benho\source\Application Documentation\Axiom\02 - Planning\v1.1.x-1.2.0\Axiom Next Release Strategy.md") })
                if (File.Exists(source)) File.Copy(source, Path.Combine(workspace, file));
            foreach (var term in new[] { "recovery", "relationships", "CP connection" })
            {
                query.Text = term; var watch = Stopwatch.StartNew(); await window.SearchWorkspaceForTestingAsync(term); watch.Stop();
                Require(results.ItemCount > 0, "Copied documentation task had no matches: " + term);
                results.SelectedIndex = 0; await window.OpenSearchResultForTestingAsync((WorkspaceSearchResult)results.SelectedItem!);
                await window.ExecuteCommandAsync("navigation.returnToDiscovery");
                Require(query.Text == term && tab.Session.Text == draft && tab.Session.IsDirty, "Copied collection task lost results or draft.");
                baseline.Add(new { Query = term, Results = results.ItemCount, SearchMilliseconds = watch.ElapsedMilliseconds });
            }
            await File.WriteAllTextAsync(Path.Combine(Root!, "copied-documentation-tasks.json"), JsonSerializer.Serialize(baseline, new JsonSerializerOptions { WriteIndented = true }));
            var shell = terminal.ProcessIdForTesting!.Value;
            await terminal.DisposeAsync(); await Task.Delay(100);
            Require(!Process.GetProcesses().Any(process => process.Id == shell), "Disposed terminal shell remained running.");
            await File.WriteAllTextAsync(Path.Combine(Root!, "result.json"), JsonSerializer.Serialize(new
            {
                Version = window.BuildIdentityForTesting.InformationalVersion
              , RetainedDiscoveryAndDirtyBuffer = true
              , PersistentRightAndBottomLayouts = true
              , SideToolbarAndZenRestore = true
              , RealConPtyPowerShellAndRenderedOutput = true
              , RecipeDoesNotExecuteBeforePhysicalEnter = true
              , PendingCommandNotOverwritten = true
              , LiteralRootWithSpacesAndQuotes = true
              , ShellDisposed = true
              , RunningCommandBlockedAndCtrlCWorks = true
              , ChangedDirectoryDisablesRecipes = true
              , ReopeningGitRetainsShell = true
              , TerminalResize = true
              , PhysicalManualDirectoryReturnAndShellExitRestart = true
              , CopiedDocumentationTasks = true
              , ChangesTabRetainsShellAndPendingCommand = true
              , InputLimitRequiresExplicitCancellation = true
              , HumanChecks = "Human usability, keyboard/screen-reader/DPI, sustained use and CP account/server integration remain pending. No push performed."
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception error)
        {
            if (window.GitTerminalForTesting is { } terminal)
            {
                await File.WriteAllTextAsync(Path.Combine(Root!, "diagnostic.txt"), terminal.DiagnosticForTesting);
                try { await File.WriteAllTextAsync(Path.Combine(Root!, "browser-state.txt"), await terminal.BrowserForTesting.InvokeScript("JSON.stringify({url:location.href,drain:typeof window.axiomDrain,terminal:typeof window.axiomTerminal,body:document.body.innerText})")); } catch { }
            }
            await File.WriteAllTextAsync(Path.Combine(Root!, "error.txt"), error.ToString());
        }
        finally { window.UnsavedChoiceForTesting = _ => Task.FromResult("Discard"); window.Close(); }
    }
    private static async Task WaitAsync(Func<Task<bool>> check)
    {
        for (var count = 0; count < 250; count++) { if (await check()) return; await Task.Delay(80); }
        throw new TimeoutException("Native discovery/terminal operation timed out.");
    }
    private static string Git(string root, params string[] arguments)
    {
        var info = new ProcessStartInfo("git") { WorkingDirectory = root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info)!; var output = process.StandardOutput.ReadToEnd(); var error = process.StandardError.ReadToEnd(); process.WaitForExit();
        if (process.ExitCode != 0) throw new IOException(error); return output.Trim();
    }
    private static void Require(bool condition, string message) { if (!condition) throw new IOException(message); }
}
