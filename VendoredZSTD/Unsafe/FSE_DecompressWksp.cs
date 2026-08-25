using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

[StructLayout(LayoutKind.Sequential)]
public unsafe struct FseDecompressWksp
{
    public fixed short ncount[256];

    /* Dynamically sized */
    public fixed uint dtable[1];
}