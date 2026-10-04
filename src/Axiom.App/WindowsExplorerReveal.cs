using System.Runtime.InteropServices;

namespace Axiom.App;

internal static class WindowsExplorerReveal
{
    internal static Task ShowAsync(string path)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { Show(path); completion.SetResult(); }
            catch (Exception error) { completion.SetException(error); }
        }) { IsBackground = true, Name = "Axiom Explorer selection" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static void Show(string path)
    {
        var initialized = CoInitializeEx(0, 2);
        Marshal.ThrowExceptionForHR(initialized);
        nint item = 0;
        try
        {
            Marshal.ThrowExceptionForHR(SHParseDisplayName(Path.GetFullPath(path), 0, out item, 0, out _));
            Marshal.ThrowExceptionForHR(SHOpenFolderAndSelectItems(item, 0, 0, 0));
        }
        finally
        {
            if (item != 0) Marshal.FreeCoTaskMem(item);
            CoUninitialize();
        }
    }

    [DllImport("ole32.dll")] private static extern int CoInitializeEx(nint reserved, uint mode);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHParseDisplayName(string name, nint context, out nint item, uint attributes, out uint found);
    [DllImport("shell32.dll")] private static extern int SHOpenFolderAndSelectItems(nint folder, uint count, nint children, uint flags);
}
