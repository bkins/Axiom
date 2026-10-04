using System.Runtime.InteropServices;

namespace Axiom.App;

[StructLayout(LayoutKind.Explicit,Size=40)]
internal struct NativeProbeKeyboardInput
{
    [FieldOffset(0)] internal uint Type;
    [FieldOffset(10)] internal ushort ScanCode;
    [FieldOffset(12)] internal uint Flags;
}
