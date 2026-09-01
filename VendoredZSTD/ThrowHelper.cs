using VendoredZSTD.Unsafe;

namespace VendoredZSTD;

public static class ThrowHelper
{
    private const ulong ZstdContentsizeUnknown = unchecked(0UL - 1);
    private const ulong ZstdContentsizeError = unchecked(0UL - 2);

    public static nuint EnsureZstdSuccess(this nuint returnValue)
    {
        if (Methods.ZSTD_isError(returnValue))
            ThrowException(returnValue, Methods.ZSTD_getErrorName(returnValue));

        return returnValue;
    }

    public static nuint EnsureZdictSuccess(this nuint returnValue)
    {
        if (Methods.ZDICT_isError(returnValue))
            ThrowException(returnValue, Methods.ZDICT_getErrorName(returnValue));

        return returnValue;
    }

    public static ulong EnsureContentSizeOk(this ulong returnValue)
    {
        if (returnValue == ZstdContentsizeUnknown)
        {
            throw new ZstdException(
                ZstdErrorCode.ZstdErrorGeneric,
                "Decompressed content size is not specified"
            );
        }

        if (returnValue == ZstdContentsizeError)
        {
            throw new ZstdException(
                ZstdErrorCode.ZstdErrorGeneric,
                "Decompressed content size cannot be determined (e.g. invalid magic number, srcSize too small)"
            );
        }

        return returnValue;
    }

    private static void ThrowException(nuint returnValue, string message)
    {
        var code = 0 - returnValue;
        throw new ZstdException((ZstdErrorCode)code, message);
    }
}