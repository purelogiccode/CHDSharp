using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

[StructLayout(LayoutKind.Sequential)]
public unsafe struct ZstdFseState
{
    public nuint state;
    public ZstdSeqSymbol* table;
}