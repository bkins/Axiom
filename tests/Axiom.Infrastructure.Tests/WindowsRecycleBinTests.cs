using System.Runtime.InteropServices;
using Axiom.Infrastructure;

namespace Axiom.Infrastructure.Tests;

public sealed class WindowsRecycleBinTests
{
    [Fact]
    public void RecycleOnlyAdapterRetainsContentAndWindowsCanRestoreIt()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 2)) return;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { if (OperatingSystem.IsWindowsVersionAtLeast(6, 2)) RecycleRoundTrip(); }
            catch (Exception exception) { failure = exception; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "Recycle/restore did not finish within 20 seconds.");
        Assert.Null(failure);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows6.2")]
    private static void RecycleRoundTrip()
    {
        using var fixture = new MutationTestWorkspace();
        var path = Path.Combine(fixture.Root, "recycle-probe-" + Guid.NewGuid().ToString("N") + ".md");
        File.WriteAllText(path, "Content retained through Windows recycling.");
        new WindowsRecycleBin().Recycle(path);
        Assert.False(File.Exists(path));
        var shellType = Type.GetTypeFromProgID("Shell.Application", true)!;
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic folder = shell.Namespace(10);
        dynamic items = folder.Items();
        try
        {
            var restored = false;
            for (var index = 0; index < (int)items.Count; index++)
            {
                dynamic item = items.Item(index);
                try
                {
                    var originalDirectory = (string?)item.ExtendedProperty("System.Recycle.DeletedFrom");
                    if (!string.Equals(originalDirectory, fixture.Root, StringComparison.OrdinalIgnoreCase)) continue;
                    item.InvokeVerb("undelete");
                    var deadline = DateTime.UtcNow.AddSeconds(10);
                    while (!File.Exists(path) && DateTime.UtcNow < deadline) Thread.Sleep(50);
                    restored = File.Exists(path);
                    break;
                }
                finally { Marshal.ReleaseComObject(item); }
            }
            Assert.True(restored, "The disposable probe was not found/restored through Windows Recycle Bin.");
            Assert.Equal("Content retained through Windows recycling.", File.ReadAllText(path));
        }
        finally
        {
            Marshal.ReleaseComObject(items);
            Marshal.ReleaseComObject(folder);
            Marshal.ReleaseComObject(shell);
        }
    }
}
