using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

[StructLayout(LayoutKind.Sequential)]
public unsafe struct ZstdCCtxS
{
    public ZstdCompressionStageE stage;
    /* == 1 if cParams(except wlog) or compression level are changed in requestedParams. Triggers transmission of new params to ZSTDMT (if available) then reset to 0. */
    public int cParamsChanged;
    /* == 1 if the CPU supports BMI2 and 0 otherwise. CPU support is determined dynamically once per context lifetime. */
    public int bmi2;
    public ZstdCCtxParamsS requestedParams;
    public ZstdCCtxParamsS appliedParams;
    /* Param storage used by the simple API - not sticky. Must only be used in top-level simple API functions for storage. */
    public ZstdCCtxParamsS simpleApiParams;
    public uint dictID;
    public nuint dictContentSize;
    /* manages buffer for dynamic allocations */
    public ZstdCwksp workspace;
    public nuint blockSizeMax;
    /* this way, 0 (default) == unknown */
    public ulong pledgedSrcSizePlusOne;
    public ulong consumedSrcSize;
    public ulong producedCSize;
    public Xxh64StateS xxhState;
    public ZstdCustomMem customMem;
    public void* pool;
    public nuint staticSize;
    public SeqCollector seqCollector;
    public int isFirstBlock;
    public int initialized;
    /* sequences storage ptrs */
    public SeqStoreT seqStore;
    /* long distance matching state */
    public LdmStateT ldmState;
    /* Storage for the ldm output sequences */
    public RawSeq* ldmSequences;
    public nuint maxNbLdmSequences;
    /* Mutable reference to external sequences */
    public RawSeqStoreT externSeqStore;
    public ZstdBlockStateT blockState;
    /* used as substitute of stack space - must be aligned for S64 type */
    public void* tmpWorkspace;
    public nuint tmpWkspSize;
    /* Whether we are streaming or not */
    public ZstdBufferedPolicyE bufferedPolicy;
    /* streaming */
    public sbyte* inBuff;
    public nuint inBuffSize;
    public nuint inToCompress;
    public nuint inBuffPos;
    public nuint inBuffTarget;
    public sbyte* outBuff;
    public nuint outBuffSize;
    public nuint outBuffContentSize;
    public nuint outBuffFlushedSize;
    public ZstdCStreamStage streamStage;
    public uint frameEnded;
    /* Stable in/out buffer verification */
    public ZstdInBufferS expectedInBuffer;
    /* nb bytes within stable input buffer that are said to be consumed but are not */
    public nuint stableIn_notConsumed;
    public nuint expectedOutBufferSize;
    /* Dictionary */
    public ZstdLocalDict localDict;
    public ZstdCDictS* cdict;
    /* single-usage dictionary */
    public ZstdPrefixDictS prefixDict;
    public ZstdmtCCtxS* mtctx;
    /* Workspace for block splitter */
    public ZstdBlockSplitCtx blockSplitCtx;
    /* Buffer for output from external sequence producer */
    public ZstdSequence* extSeqBuf;
    public nuint extSeqBufCapacity;
}