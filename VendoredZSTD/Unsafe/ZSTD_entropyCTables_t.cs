using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

[StructLayout(LayoutKind.Sequential)]
public struct ZSTD_entropyCTables_t
{
    public ZSTD_hufCTables_t huf;
    public ZSTD_fseCTables_t fse;
}