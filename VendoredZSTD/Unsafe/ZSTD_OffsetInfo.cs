using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

[StructLayout(LayoutKind.Sequential)]
public struct ZstdOffsetInfo
{
    public uint longOffsetShare;
    public uint maxNbAdditionalBits;
}