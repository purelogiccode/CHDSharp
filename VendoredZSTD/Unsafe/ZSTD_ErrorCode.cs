namespace VendoredZSTD.Unsafe;

/*-*********************************************
 *  Error codes list
 *-*********************************************
 *  Error codes _values_ are pinned down since v1.3.1 only.
 *  Therefore, don't rely on values if you may link to any version < v1.3.1.
 *
 *  Only values < 100 are considered stable.
 *
 *  note 1 : this API shall be used with static linking only.
 *           dynamic linking is not yet officially supported.
 *  note 2 : Prefer relying on the enum than on its value whenever possible
 *           This is the only supported way to use the error list < v1.3.1
 *  note 3 : ZSTD_isError() is always correct, whatever the library version.
 **********************************************/
public enum ZstdErrorCode
{
    ZstdErrorNoError = 0,
    ZstdErrorGeneric = 1,
    ZstdErrorPrefixUnknown = 10,
    ZstdErrorVersionUnsupported = 12,
    ZstdErrorFrameParameterUnsupported = 14,
    ZstdErrorFrameParameterWindowTooLarge = 16,
    ZstdErrorCorruptionDetected = 20,
    ZstdErrorChecksumWrong = 22,
    ZstdErrorLiteralsHeaderWrong = 24,
    ZstdErrorDictionaryCorrupted = 30,
    ZstdErrorDictionaryWrong = 32,
    ZstdErrorDictionaryCreationFailed = 34,
    ZstdErrorParameterUnsupported = 40,
    ZstdErrorParameterCombinationUnsupported = 41,
    ZstdErrorParameterOutOfBound = 42,
    ZstdErrorTableLogTooLarge = 44,
    ZstdErrorMaxSymbolValueTooLarge = 46,
    ZstdErrorMaxSymbolValueTooSmall = 48,
    ZstdErrorCannotProduceUncompressedBlock = 49,
    ZstdErrorStabilityConditionNotRespected = 50,
    ZstdErrorStageWrong = 60,
    ZstdErrorInitMissing = 62,
    ZstdErrorMemoryAllocation = 64,
    ZstdErrorWorkSpaceTooSmall = 66,
    ZstdErrorDstSizeTooSmall = 70,
    ZstdErrorSrcSizeWrong = 72,
    ZstdErrorDstBufferNull = 74,
    ZstdErrorNoForwardProgressDestFull = 80,
    ZstdErrorNoForwardProgressInputEmpty = 82,

    /* following error codes are __NOT STABLE__, they can be removed or changed in future versions */
    ZstdErrorFrameIndexTooLarge = 100,
    ZstdErrorSeekableIo = 102,
    ZstdErrorDstBufferWrong = 104,
    ZstdErrorSrcBufferWrong = 105,
    ZstdErrorSequenceProducerFailed = 106,
    ZstdErrorExternalSequencesInvalid = 107,

    /* never EVER use this value directly, it can change in future versions! Use ZSTD_isError() instead */
    ZstdErrorMaxCode = 120
}