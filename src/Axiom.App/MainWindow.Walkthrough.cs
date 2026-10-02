using System.Diagnostics;
using Axiom.Core;

namespace Axiom.App;

public partial class MainWindow
{
    internal Func<string, Task>? WalkthroughLaunchForTesting { get; set; }

    private async Task<CommandResult> ShowWalkthroughAsync()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Walkthrough", "index.html");
        if (!File.Exists(path)) return new(CommandOutcome.Unavailable, "The walkthrough is missing from this package. Re-extract the complete Axiom package.");
        try
        {
            if (WalkthroughLaunchForTesting is { } launch) await launch(path);
            else Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return new(CommandOutcome.Completed, "Opened the offline captioned walkthrough in your browser.");
        }
        catch (Exception exception)
        {
            ReportError("walkthrough.open.failed", exception, path);
            return new(CommandOutcome.Failed, "Open Walkthrough/index.html from the Axiom package in your browser.", exception);
        }
    }
}
