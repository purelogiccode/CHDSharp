using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

[StructLayout(LayoutKind.Sequential)]
public unsafe struct EStatsRessT
{
    /* dictionary */
    public ZstdCDictS* dict;

    /* working context */
    public ZstdCCtxS* zc;

    /* must be ZSTD_BLOCKSIZE_MAX allocated */
    public void* workPlace;
}