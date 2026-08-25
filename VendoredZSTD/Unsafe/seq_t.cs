using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

[StructLayout(LayoutKind.Sequential)]
public struct seq_t
{
    public nuint litLength;
    public nuint matchLength;
    public nuint offset;
}
