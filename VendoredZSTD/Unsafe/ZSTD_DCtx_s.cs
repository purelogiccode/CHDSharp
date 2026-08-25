using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

[StructLayout(LayoutKind.Sequential)]
public unsafe struct ZstdDCtxS
{
    public ZstdSeqSymbol* LLTptr;
    public ZstdSeqSymbol* MLTptr;
    public ZstdSeqSymbol* OFTptr;
    public uint* HUFptr;
    public ZstdEntropyDTablesT entropy;

    /* space needed when building huffman tables */
    public fixed uint workspace[640];

    /* detect continuity */
    public void* previousDstEnd;

    /* start of current segment */
    public void* prefixStart;

    /* virtual start of previous segment if it was just before current one */
    public void* virtualStart;

    /* end of previous segment */
    public void* dictEnd;
    public nuint expected;
    public ZstdFrameHeader fParams;
    public ulong processedCSize;
    public ulong decodedSize;

    /* used in ZSTD_decompressContinue(), store blockType between block header decoding and block decompression stages */
    public BlockTypeE bType;
    public ZstdDStage stage;
    public uint litEntropy;
    public uint fseEntropy;
    public Xxh64StateS xxhState;
    public nuint headerSize;
    public ZstdFormatE format;

    /* User specified: if == 1, will ignore checksums in compressed frame. Default == 0 */
    public ZstdForceIgnoreChecksumE forceIgnoreChecksum;

    /* if == 1, will validate checksum. Is == 1 if (fParams.checksumFlag == 1) and (forceIgnoreChecksum == 0). */
    public uint validateChecksum;
    public byte* litPtr;
    public ZstdCustomMem customMem;
    public nuint litSize;
    public nuint rleSize;
    public nuint staticSize;

    /* dictionary */
    public ZstdDDictS* ddictLocal;

    /* set by ZSTD_initDStream_usingDDict(), or ZSTD_DCtx_refDDict() */
    public ZstdDDictS* ddict;
    public uint dictID;

    /* if == 1 : dictionary is "new" for working context, and presumed "cold" (not in cpu cache) */
    public int ddictIsCold;
    public ZstdDictUsesE dictUses;

    /* Hash set for multiple ddicts */
    public ZstdDDictHashSet* ddictSet;

    /* User specified: if == 1, will allow references to multiple DDicts. Default == 0 (disabled) */
    public ZstdRefMultipleDDictsE refMultipleDDicts;
    public int disableHufAsm;

    /* streaming */
    public ZstdDStreamStage streamStage;
    public sbyte* inBuff;
    public nuint inBuffSize;
    public nuint inPos;
    public nuint maxWindowSize;
    public sbyte* outBuff;
    public nuint outBuffSize;
    public nuint outStart;
    public nuint outEnd;
    public nuint lhSize;
    public uint hostageByte;
    public int noForwardProgress;
    public ZstdBufferModeE outBufferMode;
    public ZstdOutBufferS expectedOutBuffer;

    /* workspace */
    public byte* litBuffer;
    public byte* litBufferEnd;
    public ZstdLitLocationE litBufferLocation;

    /* literal buffer can be split between storage within dst and within this scratch buffer */
    public fixed byte litExtraBuffer[65568];
    public fixed byte headerBuffer[18];
    public nuint oversizedDuration;
}