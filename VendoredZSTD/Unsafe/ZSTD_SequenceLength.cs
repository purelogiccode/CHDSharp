using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

[StructLayout(LayoutKind.Sequential)]
public struct ZSTD_sequenceLength
{
    public uint litLength;
    public uint matchLength;
}
