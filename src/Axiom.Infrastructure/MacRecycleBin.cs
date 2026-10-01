using System.Runtime.InteropServices;

namespace Axiom.Infrastructure;

public sealed class MacRecycleBin : IRecycleBin
{
    private const string ObjectiveCLibrary = "/usr/lib/libobjc.A.dylib";
    private static readonly Lazy<nint> Foundation = new(() => NativeLibrary.Load("/System/Library/Frameworks/Foundation.framework/Foundation"));

    public void Recycle(string path)
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("The macOS Trash adapter requires macOS.");
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path) || path.Contains('\0')) throw new ArgumentException("Choose an absolute document path.", nameof(path));
        if (!File.Exists(path) || File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
            throw new IOException("The document is unavailable or linked; it was retained.");
        _ = Foundation.Value;
        var pool = PushAutoreleasePool();
        try
        {
            var fileManager = Send(GetClass("NSFileManager"), Selector("defaultManager"));
            var nativePath = SendUtf8(GetClass("NSString"), Selector("stringWithUTF8String:"), path);
            var url = SendArgument(GetClass("NSURL"), Selector("fileURLWithPath:"), nativePath);
            if (fileManager == nint.Zero || nativePath == nint.Zero || url == nint.Zero)
                throw new IOException("The macOS Trash service could not initialize; the document was retained.");
            // Foundation moves to Trash or reports an error. There is deliberately
            // no removeItem, shell command or permanent-delete fallback.
            if (TrashItem(fileManager, Selector("trashItemAtURL:resultingItemURL:error:"), url, out _, out var error) == 0)
            {
                var description = error == nint.Zero ? "The macOS Trash operation failed." : ReadString(Send(error, Selector("localizedDescription")));
                throw new IOException(description + " No permanent-delete fallback was attempted.");
            }
            if (File.Exists(path)) throw new IOException("macOS did not move the document to Trash; refresh before retrying.");
        }
        finally { PopAutoreleasePool(pool); }
    }

    private static string ReadString(nint value)
    {
        if (value == nint.Zero) return "The macOS Trash operation failed.";
        return Marshal.PtrToStringUTF8(Send(value, Selector("UTF8String"))) ?? "The macOS Trash operation failed.";
    }

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_getClass")]
    private static extern nint GetClass([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(ObjectiveCLibrary, EntryPoint = "sel_registerName")]
    private static extern nint Selector([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern nint Send(nint receiver, nint selector);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern nint SendArgument(nint receiver, nint selector, nint argument);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern nint SendUtf8(nint receiver, nint selector, [MarshalAs(UnmanagedType.LPUTF8Str)] string argument);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern byte TrashItem(nint receiver, nint selector, nint url, out nint resultingUrl, out nint error);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_autoreleasePoolPush")]
    private static extern nint PushAutoreleasePool();

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_autoreleasePoolPop")]
    private static extern void PopAutoreleasePool(nint pool);
}
