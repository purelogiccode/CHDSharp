using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

[StructLayout(LayoutKind.Sequential)]
public struct ZstdEntropyCTablesMetadataT
{
    public ZstdHufCTablesMetadataT hufMetadata;
    public ZstdFseCTablesMetadataT fseMetadata;
}