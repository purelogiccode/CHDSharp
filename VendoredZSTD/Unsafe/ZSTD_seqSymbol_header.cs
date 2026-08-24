using System.Runtime.InteropServices;
namespace ZstdSharp.Unsafe
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
