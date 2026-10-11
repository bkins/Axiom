using System.ComponentModel;
using Axiom.App;
using Xunit;

namespace Axiom.App.Tests;

public sealed class LocalCpSetupTests
{
    [Fact]
    public async Task Missing_Helper_Reports_Actual_Windows_Launch_Error_And_Path()
    {
        if (!OperatingSystem.IsWindows()) return;
        var missing = Path.Combine(Path.GetTempPath(), "axiom-missing-helper-" + Guid.NewGuid().ToString("N"), "Start-Axiom-Cp.cmd");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => LocalCpSetup.RunHelperAsync(missing, CancellationToken.None));
        var native = Assert.IsType<Win32Exception>(error.InnerException);
        Assert.Contains(missing, error.Message);
        Assert.Contains("Windows error " + native.NativeErrorCode, error.Message);
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
