using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

[StructLayout(LayoutKind.Sequential)]
public struct ZstdSequenceLength
{
    public uint litLength;
    public uint matchLength;
}