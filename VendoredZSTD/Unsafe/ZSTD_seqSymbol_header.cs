using System.Runtime.InteropServices;
namespace VendoredZSTD.Unsafe
{
    /*-*******************************************************
     *  Decompression types
     *********************************************************/
    [StructLayout(LayoutKind.Sequential)]
    public struct ZSTD_seqSymbol_header
    {
        public uint fastMode;
        public uint tableLog;
    }
}
