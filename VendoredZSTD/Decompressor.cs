using System.Buffers;
using VendoredZSTD.Unsafe;

namespace VendoredZSTD;

public unsafe class Decompressor : IDisposable
{
    private readonly SafeDctxHandle _handle;

    public Decompressor()
    {
        _handle = SafeDctxHandle.Create();
    }

    public void SetParameter(ZstdDParameter parameter, int value)
    {
        using var dctx = _handle.Acquire();
        Methods.ZSTD_DCtx_setParameter(dctx, parameter, value).EnsureZstdSuccess();
    }

    public int GetParameter(ZstdDParameter parameter)
    {
        using var dctx = _handle.Acquire();
        int value;
        Methods.ZSTD_DCtx_getParameter(dctx, parameter, &value).EnsureZstdSuccess();
        return value;
    }

    public void LoadDictionary(byte[] dict)
    {
        var dictReadOnlySpan = new ReadOnlySpan<byte>(dict);
        LoadDictionary(dictReadOnlySpan);
    }

    public void LoadDictionary(ReadOnlySpan<byte> dict)
    {
        using var dctx = _handle.Acquire();
        fixed (byte* dictPtr = dict)
        {
            Methods.ZSTD_DCtx_loadDictionary(dctx, dictPtr, (nuint)dict.Length).EnsureZstdSuccess();
        }
    }

    public static ulong GetDecompressedSize(ReadOnlySpan<byte> src)
    {
        fixed (byte* srcPtr = src)
        {
            return Methods.ZSTD_decompressBound(srcPtr, (nuint)src.Length).EnsureContentSizeOk();
        }
    }

    public static ulong GetDecompressedSize(ArraySegment<byte> src)
    {
        return GetDecompressedSize((ReadOnlySpan<byte>)src);
    }

    public static ulong GetDecompressedSize(byte[] src, int srcOffset, int srcLength)
    {
        return GetDecompressedSize(new ReadOnlySpan<byte>(src, srcOffset, srcLength));
    }

    public Span<byte> Unwrap(ReadOnlySpan<byte> src, int maxDecompressedSize = int.MaxValue)
    {
        var expectedDstSize = GetDecompressedSize(src);
        if (expectedDstSize > (ulong)maxDecompressedSize)
            throw new ZstdException(ZstdErrorCode.ZstdErrorDstSizeTooSmall,
                $"Decompressed content size {expectedDstSize} is greater than {nameof(maxDecompressedSize)} {maxDecompressedSize}");
        if (expectedDstSize > Constants.MaxByteArrayLength)
            throw new ZstdException(ZstdErrorCode.ZstdErrorDstSizeTooSmall,
                $"Decompressed content size {expectedDstSize} is greater than max possible byte array size {Constants.MaxByteArrayLength}");

        var dest = new byte[expectedDstSize];
        var length = Unwrap(src, dest);
        return new Span<byte>(dest, 0, length);
    }

    public int Unwrap(byte[] src, byte[] dest, int offset)
    {
        return Unwrap(src, new Span<byte>(dest, offset, dest.Length - offset));
    }

    public int Unwrap(ReadOnlySpan<byte> src, Span<byte> dest)
    {
        fixed (byte* srcPtr = src)
        fixed (byte* destPtr = dest)
        {
            using var dctx = _handle.Acquire();
            return (int)Methods
                .ZSTD_decompressDCtx(dctx, destPtr, (nuint)dest.Length, srcPtr, (nuint)src.Length)
                .EnsureZstdSuccess();
        }
    }

    public int Unwrap(byte[] src, int srcOffset, int srcLength, byte[] dst, int dstOffset, int dstLength)
    {
        return Unwrap(new ReadOnlySpan<byte>(src, srcOffset, srcLength), new Span<byte>(dst, dstOffset, dstLength));
    }

    public bool TryUnwrap(byte[] src, byte[] dest, int offset, out int written)
    {
        return TryUnwrap(src, new Span<byte>(dest, offset, dest.Length - offset), out written);
    }

    public bool TryUnwrap(ReadOnlySpan<byte> src, Span<byte> dest, out int written)
    {
        fixed (byte* srcPtr = src)
        fixed (byte* destPtr = dest)
        {
            nuint returnValue;
            using (var dctx = _handle.Acquire())
            {
                returnValue =
                    Methods.ZSTD_decompressDCtx(dctx, destPtr, (nuint)dest.Length, srcPtr, (nuint)src.Length);
            }

            if (returnValue == unchecked(0 - (nuint)ZstdErrorCode.ZstdErrorDstSizeTooSmall))
            {
                written = 0;
                return false;
            }

            returnValue.EnsureZstdSuccess();
            written = (int)returnValue;
            return true;
        }
    }

    public bool TryUnwrap(byte[] src, int srcOffset, int srcLength, byte[] dst, int dstOffset, int dstLength, out int written)
    {
        return TryUnwrap(new ReadOnlySpan<byte>(src, srcOffset, srcLength), new Span<byte>(dst, dstOffset, dstLength), out written);
    }

    public void Dispose()
    {
        _handle.Dispose();
        GC.SuppressFinalize(this);
    }

    internal nuint DecompressStream(ref ZstdInBufferS input, ref ZstdOutBufferS output)
    {
        fixed (ZstdInBufferS* inputPtr = &input)
        fixed (ZstdOutBufferS* outputPtr = &output)
        {
            using var dctx = _handle.Acquire();
            return Methods.ZSTD_decompressStream(dctx, outputPtr, inputPtr).EnsureZstdSuccess();
        }
    }

    public void ResetStream()
    {
        using var dctx = _handle.Acquire();
        Methods.ZSTD_DCtx_reset(dctx, ZstdResetDirective.ZstdResetSessionOnly).EnsureZstdSuccess();
    }

    public OperationStatus UnwrapStream(ReadOnlySpan<byte> source, Span<byte> destination, out int bytesConsumed, out int bytesWritten)
    {
        using var dctx = _handle.Acquire();
        bytesConsumed = bytesWritten = 0;

        fixed (byte* srcPtr = source)
        fixed (byte* dstPtr = destination)
        {
            var input = new ZstdInBufferS { src = srcPtr, size = (nuint)source.Length, pos = 0 };
            var output = new ZstdOutBufferS { dst = dstPtr, size = (nuint)destination.Length, pos = 0 };

            while (output.pos != output.size)
            {
                var remaining = Methods.ZSTD_decompressStream(dctx, &output, &input);
                bytesConsumed = (int)input.pos;
                bytesWritten = (int)output.pos;

                if (Methods.ZSTD_isError(remaining))
                    return OperationStatus.InvalidData;

                // input is finished
                if (input.pos == input.size)
                {
                    // end of frame
                    if (remaining == 0)
                        return OperationStatus.Done;

                    return OperationStatus.NeedMoreData;
                }
            }

            return OperationStatus.DestinationTooSmall;
        }
    }
}