using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Axiom.Core;
using Axiom.Infrastructure;

namespace Axiom.App;

internal static class StructuredWikiProbe
{
    internal static string? Root { get; private set; }

    internal static void Configure(string root)
    {
        root = Path.GetFullPath(root);
        if (Directory.Exists(root)) throw new IOException("Choose a fresh structured wiki evidence directory.");
        Directory.CreateDirectory(root);
        Root = root;
        Environment.SetEnvironmentVariable("AXIOM_STATE_ROOT", Path.Combine(root, "state"));
    }

    internal static async Task StartAsync(MainWindow window)
    {
        var root = Root!;
        window.UnsavedChoiceForTesting = _ => Task.FromResult("Discard");
        try
        {
            await window.StartupCompleted;
            var workspace = Path.Combine(root, "workspace");
            Directory.CreateDirectory(workspace);
            var sourcePath = Path.Combine(workspace, "source.md");
            var targetPath = Path.Combine(workspace, "target.md");
            var identity = Guid.NewGuid();
            var savedTarget = $"<!-- axiom:documentId={identity:D} -->\n# Target\n\n## Saved heading\n";
            var source = "# Source\n\n- [ ] Inspect this task\n\n[[Alternate#Live heading|Visit target]]\n[[dup]]\n[[missing]]\n\n```md\n[[code example]]\n```\n";
            await File.WriteAllTextAsync(sourcePath, source);
            await File.WriteAllTextAsync(targetPath, savedTarget);
            Directory.CreateDirectory(Path.Combine(workspace, "one"));
            Directory.CreateDirectory(Path.Combine(workspace, "two"));
            await File.WriteAllTextAsync(Path.Combine(workspace, "one", "dup.md"), "# One\n");
            await File.WriteAllTextAsync(Path.Combine(workspace, "two", "dup.md"), "# Two\n");
            await window.SwitchWorkspaceAsync(workspace);
            var state = new SqliteStateStore(new AppDataPaths(Path.Combine(root, "state")));
            await state.ApplyClassificationsAsync([new(new(identity, [], null, ["Alternate"]), 0)]);
            await window.OpenDocumentAsync(targetPath);
            var workingTarget = savedTarget.Replace("Saved heading", "Live heading", StringComparison.Ordinal) + "Unsaved target\n";
            window.SetEditorTextForTesting(workingTarget);
            await window.OpenDocumentAsync(sourcePath);
            var workingSource = source + "Unsaved source\n";
            window.SetEditorTextForTesting(workingSource);
            await window.ProtectIfNeededForTestingAsync();
            var structureOperation = window.ExecuteCommandAsync("document.structure");
            var browser = await WaitForWindowAsync<StructureBrowserWindow>();
            var nodes = browser.GetLogicalDescendants().OfType<ListBox>().Single();
            nodes.SelectedItem = window.StructureForTesting.Nodes.Single(node => node.Kind == DocumentStructureNodeKind.Task);
            Click(browser, "Show source");
            Require((await structureOperation).Outcome == CommandOutcome.Completed, "Structure source navigation failed.");
            var editor = window.FindControl<AvaloniaEdit.TextEditor>("Editor")!;
            Require(editor.SelectedText.Contains("Inspect this task", StringComparison.Ordinal), "Structure browser did not select its exact task range.");
            Require(window.EditorTextForTesting == workingSource, "Structure browsing changed source.");
            var preview = window.FindControl<NativeWebView>("Preview")!;
            await WaitAsync(async () => IsTrue(await preview.InvokeScript("[...document.querySelectorAll('a')].some(link => link.textContent === 'Visit target' && link.href.startsWith('axiom-link:'))")));
            await preview.InvokeScript("[...document.querySelectorAll('a')].find(link => link.textContent === 'Visit target').click()");
            await WaitAsync(() => Task.FromResult(window.PathContextForTesting == targetPath));
            Require(window.EditorTextForTesting == workingTarget, "Native wiki preview discarded target edits.");
            Require(window.EditorCaretOffsetForTesting == window.StructureForTesting.Headings[1].Source.Offset, "Wiki heading navigation missed the live source range.");
            await window.RebuildBacklinksForTestingAsync();
            Require(window.BacklinksForTesting.Any(reference => reference.IsUnsaved && reference.Status == MarkdownLinkResolutionStatus.Resolved), "Dirty wiki backlink was not resolved.");
            await window.OpenDocumentAsync(sourcePath);
            var ambiguous = window.FollowLinkForTestingAsync(WikiLinkResolver.Prefix + Uri.EscapeDataString("dup"));
            var choice = await WaitForWindowAsync<WikiTargetWindow>();
            Require(!choice.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, "Open selected")).IsEnabled, "Ambiguous wiki target was selected automatically.");
            choice.GetLogicalDescendants().OfType<ListBox>().Single().SelectedIndex = 1;
            Click(choice, "Open selected");
            await ambiguous;
            Require(window.PathContextForTesting!.EndsWith(Path.Combine("two", "dup.md"), StringComparison.OrdinalIgnoreCase), "Explicit wiki target choice was ignored.");
            await window.OpenDocumentAsync(sourcePath);
            await window.FollowLinkForTestingAsync(WikiLinkResolver.Prefix + Uri.EscapeDataString("missing"));
            Require(window.PathContextForTesting == sourcePath && !File.Exists(Path.Combine(workspace, "missing.md")), "Missing wiki target changed or created a document.");
            Require(await File.ReadAllTextAsync(sourcePath) == source && await File.ReadAllTextAsync(targetPath) == savedTarget, "Wiki workflows saved working buffers implicitly.");
            Require(DocumentIdCodec.Find(window.EditorTextForTesting) is null, "Wiki navigation enrolled ordinary source.");
            await File.WriteAllTextAsync(Path.Combine(root, "result.json"), JsonSerializer.Serialize(new
            {
                Version = window.BuildIdentityForTesting.InformationalVersion
              , NativeStructureBrowserAndExactTaskSelection = true
              , ActualPreviewWikiNavigation = true
              , DirtyTargetHeadingAndBuffersPreserved = true
              , AliasAndBacklinkResolution = true
              , ExplicitAmbiguousTargetChoice = true
              , MissingTargetNeverCreatedOrEnrolled = true
              , SourceDiskUnchanged = true
              , HumanChecks = "Physical keyboard/focus, screen reader, themes/DPI and release acceptance remain pending."
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception exception) { await File.WriteAllTextAsync(Path.Combine(root, "error.txt"), exception.ToString()); }
        finally
        {
            foreach (var open in Windows().Where(open => open != window).ToArray()) open.Close();
            foreach (var tab in window.TabsForTesting.ToArray())
            {
                await window.SelectTabForTestingAsync(tab);
                window.SetEditorTextForTesting(tab.Session.SavedText);
            }
            window.Close();
        }
    }

    private static IEnumerable<Window> Windows() => ((IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!).Windows;
    private static void Click(Window window, string label) => window.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, label)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static async Task<TWindow> WaitForWindowAsync<TWindow>() where TWindow : Window
    {
        await WaitAsync(() => Task.FromResult(Windows().OfType<TWindow>().Any()));
        return Windows().OfType<TWindow>().Single();
    }
    private static async Task WaitAsync(Func<Task<bool>> predicate)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < deadline)
        {
            try { if (await predicate()) return; } catch (InvalidOperationException) { }
            await Task.Delay(100);
        }
        throw new IOException("Native structured wiki check timed out.");
    }
    private static bool IsTrue(string? value) => value?.Trim().Trim('"') == "true";
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new IOException(message);
    }
}
