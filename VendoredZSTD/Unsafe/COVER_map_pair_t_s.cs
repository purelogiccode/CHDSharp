using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

[StructLayout(LayoutKind.Sequential)]
public struct CoverMapPairTs
{
    public uint key;
    public uint value;
}