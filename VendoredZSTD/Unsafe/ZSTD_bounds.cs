using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

[StructLayout(LayoutKind.Sequential)]
public struct ZstdBounds
{
    public nuint error;
    public int lowerBound;
    public int upperBound;
}