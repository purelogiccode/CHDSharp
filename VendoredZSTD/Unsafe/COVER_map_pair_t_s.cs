using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

[StructLayout(LayoutKind.Sequential)]
public struct CoverMapPairTS
{
    public uint key;
    public uint value;
}