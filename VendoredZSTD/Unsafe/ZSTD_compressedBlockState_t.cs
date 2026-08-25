using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

[StructLayout(LayoutKind.Sequential)]
public unsafe struct ZstdCompressedBlockStateT
{
    public ZstdEntropyCTablesT entropy;
    public fixed uint rep[3];
}