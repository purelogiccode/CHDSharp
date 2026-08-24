using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

[StructLayout(LayoutKind.Sequential)]
public struct OffsetCountT
{
    public uint offset;
    public uint count;
}