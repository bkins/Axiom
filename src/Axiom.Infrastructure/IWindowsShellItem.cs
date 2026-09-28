using System.Runtime.InteropServices;

namespace Axiom.Infrastructure;

[ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IWindowsShellItem
{
    [PreserveSig] int BindToHandler(nint context, ref Guid handler, ref Guid interfaceId, out nint result);
    [PreserveSig] int GetParent(out IWindowsShellItem parent);
    [PreserveSig] int GetDisplayName(uint displayName, out nint name);
    [PreserveSig] int GetAttributes(uint mask, out uint attributes);
    [PreserveSig] int Compare(IWindowsShellItem other, uint hint, out int order);
}
