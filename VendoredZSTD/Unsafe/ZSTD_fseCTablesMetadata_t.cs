using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

/**
 * ZSTD_fseCTablesMetadata_t :
 * Stores symbol compression modes for a super-block in {ll, ol, ml}Type, and
 * fse tables in fseTablesBuffer.
 * fseTablesSize refers to the size of fse tables in bytes.
 * This metadata is populated in ZSTD_buildBlockEntropyStats_sequences()
 */
[StructLayout(LayoutKind.Sequential)]
public unsafe struct ZstdFseCTablesMetadataT
{
    public SymbolEncodingTypeE llType;
    public SymbolEncodingTypeE ofType;
    public SymbolEncodingTypeE mlType;
    public fixed byte fseTablesBuffer[133];
    public nuint fseTablesSize;

    /* This is to account for bug in 1.3.4. More detail in ZSTD_entropyCompressSeqStore_internal() */
    public nuint lastCountSize;
}