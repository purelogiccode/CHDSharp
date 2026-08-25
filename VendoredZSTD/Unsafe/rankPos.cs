using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

[StructLayout(LayoutKind.Sequential)]
public struct RankPos
{
    public ushort @base;
    public ushort curr;
}