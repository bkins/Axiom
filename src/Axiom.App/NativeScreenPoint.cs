using System.Runtime.InteropServices;

namespace Axiom.App;

[StructLayout(LayoutKind.Sequential)]
internal struct NativeScreenPoint
{
    internal int X;
    internal int Y;
}
