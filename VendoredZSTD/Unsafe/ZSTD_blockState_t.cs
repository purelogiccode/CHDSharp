using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

[StructLayout(LayoutKind.Sequential)]
public unsafe struct ZstdBlockStateT
{
    public ZstdCompressedBlockStateT* prevCBlock;
    public ZstdCompressedBlockStateT* nextCBlock;
    public ZstdMatchStateT matchState;
}