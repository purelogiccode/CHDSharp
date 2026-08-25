using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

[StructLayout(LayoutKind.Sequential)]
public struct ZstdCCtxParamsS
{
    public ZstdFormatE format;
    public ZstdCompressionParameters cParams;
    public ZstdFrameParameters fParams;
    public int compressionLevel;

    /* force back-references to respect limit of
     * 1<<wLog, even for dictionary */
    public int forceWindow;

    /* Tries to fit compressed block size to be around targetCBlockSize.
     * No target when targetCBlockSize == 0.
     * There is no guarantee on compressed block size */
    public nuint targetCBlockSize;

    /* User's best guess of source size.
     * Hint is not valid when srcSizeHint == 0.
     * There is no guarantee that hint is close to actual source size */
    public int srcSizeHint;
    public ZstdDictAttachPrefE attachDictPref;
    public ZstdParamSwitchE literalCompressionMode;

    /* Multithreading: used to pass parameters to mtctx */
    public int nbWorkers;
    public nuint jobSize;
    public int overlapLog;
    public int rsyncable;

    /* Long distance matching parameters */
    public LdmParamsT ldmParams;

    /* Dedicated dict search algorithm trigger */
    public int enableDedicatedDictSearch;

    /* Input/output buffer modes */
    public ZstdBufferModeE inBufferMode;
    public ZstdBufferModeE outBufferMode;

    /* Sequence compression API */
    public ZstdSequenceFormatE blockDelimiters;
    public int validateSequences;

    /* Block splitting */
    public ZstdParamSwitchE useBlockSplitter;

    /* Param for deciding whether to use row-based matchfinder */
    public ZstdParamSwitchE useRowMatchFinder;

    /* Always load a dictionary in ext-dict mode (not prefix mode)? */
    public int deterministicRefPrefix;

    /* Internal use, for createCCtxParams() and freeCCtxParams() only */
    public ZstdCustomMem customMem;

    /* Controls prefetching in some dictMatchState matchfinders */
    public ZstdParamSwitchE prefetchCDictTables;

    /* Controls whether zstd will fall back to an internal matchfinder
     * if the external matchfinder returns an error code. */
    public int enableMatchFinderFallback;

    /* Indicates whether an external matchfinder has been referenced.
     * Users can't set this externally.
     * It is set internally in ZSTD_registerSequenceProducer(). */
    public int useSequenceProducer;

    /* Adjust the max block size*/
    public nuint maxBlockSize;

    /* Controls repcode search in external sequence parsing */
    public ZstdParamSwitchE searchForExternalRepcodes;
}