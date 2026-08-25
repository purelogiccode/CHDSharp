using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe
{
    /* Hashset for storing references to multiple ZSTD_DDict within ZSTD_DCtx */
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct ZSTD_DDictHashSet
    {
        public ZSTD_DDict_s** ddictPtrTable;
        public nuint ddictPtrTableSize;
        public nuint ddictPtrCount;
    }
}