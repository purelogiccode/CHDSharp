using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe
{
    /*********************************
     *  Compression internals structs *
     *********************************/
    [StructLayout(LayoutKind.Sequential)]
    public struct ZSTD_match_t
    {
        /* Offset sumtype code for the match, using ZSTD_storeSeq() format */
        public uint off;

        /* Raw length of match */
        public uint len;
    }
}