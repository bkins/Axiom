using System.Runtime.InteropServices;

namespace Axiom.App;

internal static class StartupFailure
{
    internal static void Show(string message)
    {
        Console.Error.WriteLine(message);
        if (OperatingSystem.IsWindows()) MessageBox(nint.Zero, message, "Axiom could not activate the existing window", 0x10);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(nint owner, string message, string title, uint flags);
}
