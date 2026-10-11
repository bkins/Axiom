using Axiom.App;
using Xunit;

namespace Axiom.App.Tests;

public sealed class LocalCpSetupTests
{
    [Fact]
    public async Task Missing_Helper_Reports_Command_Failure_And_Path()
    {
        if (!OperatingSystem.IsWindows()) return;
        var missing = Path.Combine(Path.GetTempPath(), "axiom-missing-helper-" + Guid.NewGuid().ToString("N"), "Start-Axiom-Cp.cmd");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => LocalCpSetup.RunHelperAsync(missing, CancellationToken.None));
        Assert.Contains(missing, error.Message);
        Assert.Contains("exited with code", error.Message);
        Assert.Contains("Command host:", error.Message);
    }

    [Fact]
    public async Task Failed_Helper_Does_Not_Disclose_Arbitrary_Output()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Path.Combine(Path.GetTempPath(), "axiom-helper-output-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var helper = Path.Combine(root, "Start-Axiom-Cp.cmd");
            await File.WriteAllTextAsync(helper, "@echo off\r\necho sensitive-output\r\necho sensitive-error 1>&2\r\nexit /b 1\r\n", TestContext.Current.CancellationToken);
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => LocalCpSetup.RunHelperAsync(helper, TestContext.Current.CancellationToken));
            Assert.DoesNotContain("sensitive", error.Message);
            Assert.Contains("No recognized command-host diagnostic", error.Message);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Real_Cmd_Helper_With_Spaces_And_Ampersand_Runs_Through_Explicit_Command_Host()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Path.Combine(Path.GetTempPath(), "Axiom helper & quoted path " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var helper = Path.Combine(root, "Start-Axiom-Cp.cmd");
            await File.WriteAllTextAsync(helper, "@echo off\r\necho helper-completed>\"%~dp0completed.txt\"\r\nexit /b 0\r\n", TestContext.Current.CancellationToken);
            await LocalCpSetup.RunHelperAsync(helper, TestContext.Current.CancellationToken);
            Assert.Equal("helper-completed", (await File.ReadAllTextAsync(Path.Combine(root, "completed.txt"), TestContext.Current.CancellationToken)).Trim());
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Cancellation_Is_Checked_Before_Attempting_To_Launch()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => LocalCpSetup.RunHelperAsync("missing-helper.cmd", cancelled.Token));
    }
}
