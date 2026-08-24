using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

[StructLayout(LayoutKind.Sequential)]
public struct ZSTD_SequenceLength
{
    public uint litLength;
    public uint matchLength;
}