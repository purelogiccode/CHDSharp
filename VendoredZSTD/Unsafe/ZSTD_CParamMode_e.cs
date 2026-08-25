namespace VendoredZSTD.Unsafe;

public enum ZstdCParamModeE
{
    /* Compression with ZSTD_noDict or ZSTD_extDict.
     * In this mode we use both the srcSize and the dictSize
     * when selecting and adjusting parameters.
     */
    ZstdCpmNoAttachDict = 0,

    /* Compression with ZSTD_dictMatchState or ZSTD_dedicatedDictSearch.
     * In this mode we only take the srcSize into account when selecting
     * and adjusting parameters.
     */
    ZstdCpmAttachDict = 1,

    /* Creating a CDict.
     * In this mode we take both the source size and the dictionary size
     * into account when selecting and adjusting the parameters.
     */
    ZstdCpmCreateCDict = 2,

    /* ZSTD_getCParams, ZSTD_getParams, ZSTD_adjustParams.
     * We don't know what these parameters are for. We default to the legacy
     * behavior of taking both the source size and the dict size into account
     * when selecting and adjusting parameters.
     */
    ZstdCpmUnknown = 3
}