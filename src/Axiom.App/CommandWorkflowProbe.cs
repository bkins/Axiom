using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Axiom.Core;
using Axiom.Infrastructure;

namespace Axiom.App;

internal static class CommandWorkflowProbe
{
    internal static string? Root { get; private set; }
    internal static void Configure(string root)
    {
        root = Path.GetFullPath(root);
        if (Directory.Exists(root)) throw new IOException("Choose fresh command workflow evidence.");
        Directory.CreateDirectory(root);
        Root = root;
        Environment.SetEnvironmentVariable("AXIOM_STATE_ROOT", Path.Combine(root, "state"));
    }
    internal static async Task StartAsync(MainWindow window)
    {
        window.UnsavedChoiceForTesting = _ => Task.FromResult("Discard");
        try
        {
            await window.StartupCompleted;
            var paletteOperation = window.ShowCommandPaletteForTestingAsync();
            var palette = await WaitWindowAsync("Command palette");
            Search(palette, "workspace.manage.recycle");
            await WaitAsync(() => palette.GetLogicalDescendants().OfType<ListBox>().Single().SelectedItem is CommandPaletteEntry { Id: "workspace.manage.recycle" });
            Require(!FindButton(palette, "Run").IsEnabled, "Unavailable workspace command was runnable.");
            Click(palette, "Cancel");
            await paletteOperation;
            var workspace = Path.Combine(Root!, "workspace");
            Directory.CreateDirectory(workspace);
            var source = Path.Combine(workspace, "notes.md");
            await File.WriteAllTextAsync(source, "# Saved notes\n");
            await window.SwitchWorkspaceAsync(workspace);
            await window.OpenDocumentAsync(source);
            var dirty = "# Dirty draft\n\nalpha beta";
            window.SetEditorTextForTesting(dirty);
            paletteOperation = window.ShowCommandPaletteForTestingAsync();
            palette = await WaitWindowAsync("Command palette");
            Search(palette, "workspace.manage.recycle");
            await WaitAsync(() => palette.GetLogicalDescendants().OfType<ListBox>().Single().SelectedItem is CommandPaletteEntry { Id: "workspace.manage.recycle" });
            Require(FindButton(palette, "Run").IsEnabled, "Recycle palette entry unavailable: " + palette.GetLogicalDescendants().OfType<ListBox>().Single().SelectedItem);
            Click(palette, "Run");
            await WaitAsync(() => paletteOperation.IsCompleted || Windows().Any(child => child.Title == "Confirm Recycle"));
            Require(!paletteOperation.IsCompleted, "Palette completed without confirmation: " + string.Join(" | ", window.GetLogicalDescendants().OfType<TextBlock>().Select(text => text.Text)));
            Click(await WaitWindowAsync("Confirm Recycle"), "Cancel");
            await paletteOperation;
            Require(await File.ReadAllTextAsync(source) == "# Saved notes\n" && window.EditorTextForTesting == dirty, "Cancelled destructive palette action changed source/draft.");
            var configure = window.ExecuteCommandAsync("application.shortcuts");
            if (configure.IsCompleted) throw new IOException("Shortcut command returned before showing its window: " + (await configure));
            var settings = await WaitWindowAsync("Keyboard shortcuts");
            var newBinding = settings.GetLogicalDescendants().OfType<TextBox>().Single(input => Avalonia.Automation.AutomationProperties.GetName(input) == "New Markdown document shortcut");
            newBinding.Text = "Ctrl+S";
            Click(settings, "Apply");
            Require(Windows().Contains(settings) && settings.GetLogicalDescendants().OfType<TextBlock>().Any(text => text.Text?.Contains("conflicts", StringComparison.Ordinal) == true), "Conflicting shortcut was accepted.");
            newBinding.Text = "Ctrl+J";
            Click(settings, "Apply");
            Require((await configure).Outcome == CommandOutcome.Completed, "Shortcut Apply failed.");
            var store = new SqliteStateStore(new AppDataPaths(Path.Combine(Root!, "state")));
            Require((await store.GetShortcutConfigurationAsync()).Overrides["document.new"] == "Ctrl+J", "Shortcut was not persisted.");
            configure = window.ExecuteCommandAsync("application.shortcuts");
            settings = await WaitWindowAsync("Keyboard shortcuts");
            Click(settings, "Reset defaults");
            Click(settings, "Cancel");
            Require((await configure).Outcome == CommandOutcome.Cancelled, "Shortcut Cancel failed.");
            var count = window.TabsForTesting.Count;
            Require(await window.ExecuteShortcutForTestingAsync(new KeyEventArgs { Key = Key.J, KeyModifiers = KeyModifiers.Control }), "Effective shortcut did not dispatch.");
            Require(window.TabsForTesting.Count == count + 1 && window.TabsForTesting.Any(tab => tab.Session.Text == dirty), "Shortcut lost a dirty tab.");
            await File.WriteAllTextAsync(Path.Combine(Root!, "result.json"), JsonSerializer.Serialize(new
            {
                Version = window.BuildIdentityForTesting.InformationalVersion
              , NativePaletteSearchAndUnavailableAction = true
              , DestructivePaletteCancellationPreservedSourceAndDraft = true
              , NativeShortcutConflictApplyCancelReset = true
              , ShortcutPersistedAndDispatched = true
              , DirtyTabRetained = true
              , RegisteredCommands = window.RegisteredCommandsForTesting.Select(command => new { command.Id, command.Title, command.MutatesFiles, command.IsCancellationControl }).ToArray()
              , HumanChecks = "Physical input, accessibility, DPI/themes and human acceptance remain pending."
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception exception) { await File.WriteAllTextAsync(Path.Combine(Root!, "error.txt"), exception.ToString()); }
        finally
        {
            foreach (var child in Windows().Where(child => child != window).ToArray()) child.Close();
            window.SetEditorTextForTesting(window.TabsForTesting.Last().Session.SavedText);
            window.Close();
        }
    }
    private static void Search(Window window, string query) => window.GetLogicalDescendants().OfType<TextBox>().Single().Text = query;
    private static IEnumerable<Window> Windows() => ((IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!).Windows;
    private static Button FindButton(Window window, string text) => window.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, text));
    private static void Click(Window window, string text) => FindButton(window, text).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static async Task<Window> WaitWindowAsync(string title)
    {
        await WaitAsync(() => Windows().Any(window => window.Title == title));
        return Windows().Single(window => window.Title == title);
    }
    private static async Task WaitAsync(Func<bool> ready)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (!ready()) { if (DateTimeOffset.UtcNow >= deadline) throw new IOException("Command probe timed out."); await Task.Delay(100); }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new IOException(message); }
}
