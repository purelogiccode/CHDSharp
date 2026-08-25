using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe
{
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct ZSTD_compressedBlockState_t
    {
        public ZSTD_entropyCTables_t entropy;
        public fixed uint rep[3];
    }
}
