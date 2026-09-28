using System.Runtime.InteropServices;

namespace Axiom.Infrastructure;

[ComImport, Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IWindowsFileOperation
{
    [PreserveSig] int Advise(nint sink, out uint cookie);
    [PreserveSig] int Unadvise(uint cookie);
    [PreserveSig] int SetOperationFlags(uint flags);
    [PreserveSig] int SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string message);
    [PreserveSig] int SetProgressDialog(nint dialog);
    [PreserveSig] int SetProperties(nint properties);
    [PreserveSig] int SetOwnerWindow(uint window);
    [PreserveSig] int ApplyPropertiesToItem(nint item);
    [PreserveSig] int ApplyPropertiesToItems(nint items);
    [PreserveSig] int RenameItem(nint item, nint name, nint sink);
    [PreserveSig] int RenameItems(nint items, nint name);
    [PreserveSig] int MoveItem(nint item, nint folder, nint name, nint sink);
    [PreserveSig] int MoveItems(nint items, nint folder);
    [PreserveSig] int CopyItem(nint item, nint folder, nint name, nint sink);
    [PreserveSig] int CopyItems(nint items, nint folder);
    [PreserveSig] int DeleteItem([MarshalAs(UnmanagedType.Interface)] IWindowsShellItem item, nint sink);
    [PreserveSig] int DeleteItems(nint items);
    [PreserveSig] int NewItem(nint folder, uint attributes, nint name, nint template, nint sink);
    [PreserveSig] int PerformOperations();
    [PreserveSig] int GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool aborted);
}
