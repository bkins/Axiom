using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Axiom.Infrastructure;

internal static class ExclusiveDirectoryCreator
{
    public static void Create(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path) || path.Contains('\0'))
            throw new ArgumentException("Choose an absolute directory path without a null character.", nameof(path));
        bool created;
        if (OperatingSystem.IsWindows()) created = CreateWindowsDirectory(path, nint.Zero);
        else if (OperatingSystem.IsMacOS()) created = CreateMacDirectory(path, 0x1c0) == 0;
        else if (OperatingSystem.IsLinux()) created = CreateLinuxDirectory(path, 0x1c0) == 0;
        else throw new PlatformNotSupportedException("Exclusive directory creation requires Windows, macOS or Linux.");
        if (!created)
            throw new IOException("Folder creation failed; the target may already exist or its parent may be unavailable.",
                new Win32Exception(Marshal.GetLastPInvokeError()));
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateDirectoryW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateWindowsDirectory(string path, nint securityAttributes);

    [DllImport("libSystem.B.dylib", EntryPoint = "mkdir", SetLastError = true)]
    private static extern int CreateMacDirectory([MarshalAs(UnmanagedType.LPUTF8Str)] string path, uint mode);

    [DllImport("libc", EntryPoint = "mkdir", SetLastError = true)]
    private static extern int CreateLinuxDirectory([MarshalAs(UnmanagedType.LPUTF8Str)] string path, uint mode);
}
