using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

[StructLayout(LayoutKind.Sequential)]
public struct SeqT
{
    public nuint litLength;
    public nuint matchLength;
    public nuint offset;
}