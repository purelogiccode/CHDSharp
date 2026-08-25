using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

/*-*******************************************************
 *  Decompression types
 *********************************************************/
[StructLayout(LayoutKind.Sequential)]
public struct ZstdSeqSymbolHeader
{
    public uint fastMode;
    public uint tableLog;
}