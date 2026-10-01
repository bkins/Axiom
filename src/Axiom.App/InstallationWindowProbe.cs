using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Axiom.Core;

namespace Axiom.App;

internal static class InstallationWindowProbe
{
    internal static string? Root { get; private set; }
    internal static void Configure(string root)
    {
        root = Path.GetFullPath(root);
        if (Directory.Exists(root)) throw new IOException("Choose fresh installation UI evidence.");
        Directory.CreateDirectory(root);
        Root = root;
        Environment.SetEnvironmentVariable("AXIOM_STATE_ROOT", Path.Combine(root, "state"));
    }
    internal static async Task StartAsync(MainWindow window)
    {
        try
        {
            await window.StartupCompleted;
            var dirty = "# Unsaved installer protection\n";
            window.SetEditorTextForTesting(dirty);
            var install = window.ExecuteCommandAsync("application.install");
            var confirmation = await WaitWindowAsync("Install Axiom for current user");
            Click(confirmation, "Cancel");
            Require((await install).Outcome == CommandOutcome.Cancelled, "Installation cancellation did not return Cancelled.");
            Require(!Directory.Exists(Path.Combine(Root!, "state", "Versions")), "Cancelled install created a version.");
            var check = window.ExecuteCommandAsync("application.checkUpdate");
            Click(await WaitWindowAsync("Trusted update offer URL"), "Cancel");
            Require((await check).Outcome == CommandOutcome.Cancelled, "Update opt-in cancellation failed.");
            Require(window.EditorTextForTesting == dirty, "Installation/update prompts changed the dirty buffer.");
            var about = await window.GetAboutInformationAsync();
            Require(about.Contains("Channel: candidate") && about.Contains("Release notes:"), "About package provenance missing.");
            await File.WriteAllTextAsync(Path.Combine(Root!, "result.json"), JsonSerializer.Serialize(new
            {
                Version = window.BuildIdentityForTesting.InformationalVersion
              , ActualInstallerConfirmationCancelled = true
              , UpdateNetworkOptInCancelled = true
              , DirtyBufferPreserved = true
              , AboutPackageChannelAndNotes = true
              , HumanChecks = "Physical input and human release acceptance remain pending."
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
    private static IEnumerable<Window> Windows() => ((IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!).Windows;
    private static void Click(Window window, string text) => window.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, text)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static async Task<Window> WaitWindowAsync(string title)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (Windows().SingleOrDefault(window => window.Title == title) is { } found) return found;
            await Task.Delay(100);
        }
        throw new IOException("Installation UI probe timed out at " + title);
    }
    private static void Require(bool condition, string message) { if (!condition) throw new IOException(message); }
}
