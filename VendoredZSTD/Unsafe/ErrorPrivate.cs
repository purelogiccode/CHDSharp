using System.Runtime.CompilerServices;

namespace VendoredZSTD.Unsafe;

public static partial class Methods
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool ERR_isError(nuint code)
    {
        return code > unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorMaxCode));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ZstdErrorCode ERR_getErrorCode(nuint code)
    {
        if (!ERR_isError(code))
            return ZstdErrorCode.ZstdErrorNoError;

        return (ZstdErrorCode)(0 - code);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string ERR_getErrorName(nuint code)
    {
        return ERR_getErrorString(ERR_getErrorCode(code));
    }

    /*-****************************************
     *  Error Strings
     ******************************************/
    private static string ERR_getErrorString(ZstdErrorCode code)
    {
        const string notErrorCode = "Unspecified error code";
        switch (code)
        {
            case ZstdErrorCode.ZstdErrorNoError:
                return "No error detected";
            case ZstdErrorCode.ZstdErrorGeneric:
                return "Error (generic)";
            case ZstdErrorCode.ZstdErrorPrefixUnknown:
                return "Unknown frame descriptor";
            case ZstdErrorCode.ZstdErrorVersionUnsupported:
                return "Version not supported";
            case ZstdErrorCode.ZstdErrorFrameParameterUnsupported:
                return "Unsupported frame parameter";
            case ZstdErrorCode.ZstdErrorFrameParameterWindowTooLarge:
                return "Frame requires too much memory for decoding";
            case ZstdErrorCode.ZstdErrorCorruptionDetected:
                return "Data corruption detected";
            case ZstdErrorCode.ZstdErrorChecksumWrong:
                return "Restored data doesn't match checksum";
            case ZstdErrorCode.ZstdErrorLiteralsHeaderWrong:
                return "Header of Literals' block doesn't respect format specification";
            case ZstdErrorCode.ZstdErrorParameterUnsupported:
                return "Unsupported parameter";
            case ZstdErrorCode.ZstdErrorParameterCombinationUnsupported:
                return "Unsupported combination of parameters";
            case ZstdErrorCode.ZstdErrorParameterOutOfBound:
                return "Parameter is out of bound";
            case ZstdErrorCode.ZstdErrorInitMissing:
                return "Context should be init first";
            case ZstdErrorCode.ZstdErrorMemoryAllocation:
                return "Allocation error : not enough memory";
            case ZstdErrorCode.ZstdErrorWorkSpaceTooSmall:
                return "workSpace buffer is not large enough";
            case ZstdErrorCode.ZstdErrorStageWrong:
                return "Operation not authorized at current processing stage";
            case ZstdErrorCode.ZstdErrorTableLogTooLarge:
                return "tableLog requires too much memory : unsupported";
            case ZstdErrorCode.ZstdErrorMaxSymbolValueTooLarge:
                return "Unsupported max Symbol Value : too large";
            case ZstdErrorCode.ZstdErrorMaxSymbolValueTooSmall:
                return "Specified maxSymbolValue is too small";
            case ZstdErrorCode.ZstdErrorCannotProduceUncompressedBlock:
                return "This mode cannot generate an uncompressed block";
            case ZstdErrorCode.ZstdErrorStabilityConditionNotRespected:
                return "pledged buffer stability condition is not respected";
            case ZstdErrorCode.ZstdErrorDictionaryCorrupted:
                return "Dictionary is corrupted";
            case ZstdErrorCode.ZstdErrorDictionaryWrong:
                return "Dictionary mismatch";
            case ZstdErrorCode.ZstdErrorDictionaryCreationFailed:
                return "Cannot create Dictionary from provided samples";
            case ZstdErrorCode.ZstdErrorDstSizeTooSmall:
                return "Destination buffer is too small";
            case ZstdErrorCode.ZstdErrorSrcSizeWrong:
                return "Src size is incorrect";
            case ZstdErrorCode.ZstdErrorDstBufferNull:
                return "Operation on NULL destination buffer";
            case ZstdErrorCode.ZstdErrorNoForwardProgressDestFull:
                return "Operation made no progress over multiple calls, due to output buffer being full";
            case ZstdErrorCode.ZstdErrorNoForwardProgressInputEmpty:
                return "Operation made no progress over multiple calls, due to input being empty";
            case ZstdErrorCode.ZstdErrorFrameIndexTooLarge:
                return "Frame index is too large";
            case ZstdErrorCode.ZstdErrorSeekableIo:
                return "An I/O error occurred when reading/seeking";
            case ZstdErrorCode.ZstdErrorDstBufferWrong:
                return "Destination buffer is wrong";
            case ZstdErrorCode.ZstdErrorSrcBufferWrong:
                return "Source buffer is wrong";
            case ZstdErrorCode.ZstdErrorSequenceProducerFailed:
                return "Block-level external sequence producer returned an error code";
            case ZstdErrorCode.ZstdErrorExternalSequencesInvalid:
                return "External sequences are not valid";
            case ZstdErrorCode.ZstdErrorMaxCode:
            default:
                return notErrorCode;
        }
    }
}