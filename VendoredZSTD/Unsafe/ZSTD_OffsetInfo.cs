using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe
{
    [StructLayout(LayoutKind.Sequential)]
    public struct ZSTD_OffsetInfo
    {
        public uint longOffsetShare;
        public uint maxNbAdditionalBits;
    }
}
