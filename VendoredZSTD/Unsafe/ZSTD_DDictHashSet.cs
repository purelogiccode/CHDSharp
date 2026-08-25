using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

/* Hashset for storing references to multiple ZSTD_DDict within ZSTD_DCtx */
[StructLayout(LayoutKind.Sequential)]
public unsafe struct ZstdDDictHashSet
{
    public ZstdDDictS** ddictPtrTable;
    public nuint ddictPtrTableSize;
    public nuint ddictPtrCount;
}