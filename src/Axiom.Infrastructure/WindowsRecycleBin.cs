using System.Runtime.InteropServices;

namespace Axiom.Infrastructure;

public sealed class WindowsRecycleBin : IRecycleBin
{
    public void Recycle(string path)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 2)) throw new PlatformNotSupportedException("Windows 8 or later is required for recycle-only deletion.");
        var root = Path.GetPathRoot(path)!;
        if (root.StartsWith("\\\\", StringComparison.Ordinal) || new DriveInfo(root).DriveType != DriveType.Fixed)
            throw new IOException("This location does not support safe recycling. The document was retained.");
        var shellType = Type.GetTypeFromCLSID(new Guid("3AD05575-8857-4850-9277-11B85BDB8E09"), true)!;
        var operation = (IWindowsFileOperation)Activator.CreateInstance(shellType)!;
        object? item = null;
        try
        {
            var shellItemId = new Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE");
            Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(path, nint.Zero, ref shellItemId, out item));
            // Recycle-only and early failure: never offer a permanent-delete fallback.
            Marshal.ThrowExceptionForHR(operation.SetOperationFlags(0x20000000 | 0x00080000 | 0x00100000 | 0x0400 | 0x0010 | 0x0004));
            Marshal.ThrowExceptionForHR(operation.DeleteItem(item, nint.Zero));
            Marshal.ThrowExceptionForHR(operation.PerformOperations());
            Marshal.ThrowExceptionForHR(operation.GetAnyOperationsAborted(out var aborted));
            if (aborted) throw new IOException("Windows cancelled recycling; the document was retained.");
        }
        finally
        {
            if (item is not null) Marshal.ReleaseComObject(item);
            Marshal.ReleaseComObject(operation);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(string path, nint bindingContext, ref Guid interfaceId, [MarshalAs(UnmanagedType.Interface)] out object item);
}
