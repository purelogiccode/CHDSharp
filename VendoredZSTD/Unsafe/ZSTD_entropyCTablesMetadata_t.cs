using System.Runtime.InteropServices;
namespace ZstdSharp.Unsafe
{
    [StructLayout(LayoutKind.Sequential)]
    public struct ZSTD_entropyCTablesMetadata_t
    {
        public ZSTD_hufCTablesMetadata_t hufMetadata;
        public ZSTD_fseCTablesMetadata_t fseMetadata;
    }
}
