using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

[StructLayout(LayoutKind.Sequential)]
public struct ZstdEntropyCTablesT
{
    public ZstdHufCTablesT huf;
    public ZstdFseCTablesT fse;
}