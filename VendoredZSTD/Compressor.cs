using System.Buffers;
using VendoredZSTD.Unsafe;

namespace VendoredZSTD;

public unsafe class Compressor : IDisposable
{
    /// <summary>
    /// Minimum negative compression level allowed
    /// </summary>
    public static int MinCompressionLevel => Methods.ZSTD_minCLevel();

    /// <summary>
    /// Maximum compression level available
    /// </summary>
    public static int MaxCompressionLevel => Methods.ZSTD_maxCLevel();

    /// <summary>
    /// Default compression level
    /// </summary>
    /// <see cref="Methods.ZSTD_defaultCLevel"/>
    public const int DefaultCompressionLevel = 3;

    private int _level = DefaultCompressionLevel;

    private readonly SafeCctxHandle _handle;

    public int Level
    {
        get => _level;
        set
        {
            if (_level != value)
            {
                _level = value;
                SetParameter(ZstdCParameter.ZstdCCompressionLevel, value);
            }
        }
    }

    public void SetParameter(ZstdCParameter parameter, int value)
    {
        using var cctx = _handle.Acquire();
        Methods.ZSTD_CCtx_setParameter(cctx, parameter, value).EnsureZstdSuccess();
    }

    public int GetParameter(ZstdCParameter parameter)
    {
        using var cctx = _handle.Acquire();
        int value;
        Methods.ZSTD_CCtx_getParameter(cctx, parameter, &value).EnsureZstdSuccess();
        return value;
    }

    public void LoadDictionary(byte[] dict)
    {
        var dictReadOnlySpan = new ReadOnlySpan<byte>(dict);
        LoadDictionary(dictReadOnlySpan);
    }

    public void LoadDictionary(ReadOnlySpan<byte> dict)
    {
        using var cctx = _handle.Acquire();
        fixed (byte* dictPtr = dict)
        {
            Methods.ZSTD_CCtx_loadDictionary(cctx, dictPtr, (nuint)dict.Length).EnsureZstdSuccess();
        }
    }

    public Compressor(int level = DefaultCompressionLevel)
    {
        _handle = SafeCctxHandle.Create();
        Level = level;
    }

    public static int GetCompressBound(int length)
    {
        return (int)Methods.ZSTD_compressBound((nuint)length);
    }

    public static ulong GetCompressBoundLong(ulong length)
    {
        return Methods.ZSTD_compressBound((nuint)length);
    }

    public Span<byte> Wrap(ReadOnlySpan<byte> src)
    {
        var dest = new byte[GetCompressBound(src.Length)];
        var length = Wrap(src, dest);
        return new Span<byte>(dest, 0, length);
    }

    public int Wrap(byte[] src, byte[] dest, int offset)
    {
        return Wrap(src, new Span<byte>(dest, offset, dest.Length - offset));
    }

    public int Wrap(ReadOnlySpan<byte> src, Span<byte> dest)
    {
        fixed (byte* srcPtr = src)
        fixed (byte* destPtr = dest)
        {
            using var cctx = _handle.Acquire();
            return (int)Methods.ZSTD_compress2(cctx, destPtr, (nuint)dest.Length, srcPtr, (nuint)src.Length)
                .EnsureZstdSuccess();
        }
    }

    public int Wrap(ArraySegment<byte> src, ArraySegment<byte> dest)
    {
        return Wrap((ReadOnlySpan<byte>)src, dest);
    }

    public int Wrap(byte[] src, int srcOffset, int srcLength, byte[] dst, int dstOffset, int dstLength)
    {
        return Wrap(new ReadOnlySpan<byte>(src, srcOffset, srcLength), new Span<byte>(dst, dstOffset, dstLength));
    }

    public bool TryWrap(byte[] src, byte[] dest, int offset, out int written)
    {
        return TryWrap(src, new Span<byte>(dest, offset, dest.Length - offset), out written);
    }

    public bool TryWrap(ReadOnlySpan<byte> src, Span<byte> dest, out int written)
    {
        fixed (byte* srcPtr = src)
        fixed (byte* destPtr = dest)
        {
            nuint returnValue;
            using (var cctx = _handle.Acquire())
            {
                returnValue =
                    Methods.ZSTD_compress2(cctx, destPtr, (nuint)dest.Length, srcPtr, (nuint)src.Length);
            }

            if (returnValue == unchecked(0 - (nuint)ZSTD_ErrorCode.ZSTD_error_dstSize_tooSmall))
            {
                written = 0;
                return false;
            }

            returnValue.EnsureZstdSuccess();
            written = (int)returnValue;
            return true;
        }
    }

    public bool TryWrap(ArraySegment<byte> src, ArraySegment<byte> dest, out int written)
    {
        return TryWrap((ReadOnlySpan<byte>)src, dest, out written);
    }

    public bool TryWrap(byte[] src, int srcOffset, int srcLength, byte[] dst, int dstOffset, int dstLength, out int written)
    {
        return TryWrap(new ReadOnlySpan<byte>(src, srcOffset, srcLength), new Span<byte>(dst, dstOffset, dstLength), out written);
    }

    public void Dispose()
    {
        _handle.Dispose();
        GC.SuppressFinalize(this);
    }

    internal nuint CompressStream(ref ZSTD_inBuffer_s input, ref ZSTD_outBuffer_s output, ZSTD_EndDirective directive)
    {
        fixed (ZSTD_inBuffer_s* inputPtr = &input)
        fixed (ZSTD_outBuffer_s* outputPtr = &output)
        {
            using var cctx = _handle.Acquire();
            return Methods.ZSTD_compressStream2(cctx, outputPtr, inputPtr, directive).EnsureZstdSuccess();
        }
    }

    public void SetPledgedSrcSize(ulong pledgedSrcSize)
    {
        using var cctx = _handle.Acquire();
        Methods.ZSTD_CCtx_setPledgedSrcSize(cctx, pledgedSrcSize).EnsureZstdSuccess();
    }


    public OperationStatus WrapStream(ReadOnlySpan<byte> source, Span<byte> destination, out int bytesConsumed, out int bytesWritten, bool isFinalBlock)
    {
        return WrapStream(source, destination, out bytesConsumed, out bytesWritten, isFinalBlock ? ZSTD_EndDirective.ZSTD_e_end : ZSTD_EndDirective.ZSTD_e_continue);
    }

    public OperationStatus FlushStream(Span<byte> destination, out int bytesWritten)
    {
        return WrapStream(ReadOnlySpan<byte>.Empty, destination, out _, out bytesWritten, ZSTD_EndDirective.ZSTD_e_flush);
    }

    public OperationStatus FlushStream(Span<byte> destination, out int bytesWritten, bool isFinalBlock)
    {
        return WrapStream(ReadOnlySpan<byte>.Empty, destination, out _, out bytesWritten, isFinalBlock ? ZSTD_EndDirective.ZSTD_e_end : ZSTD_EndDirective.ZSTD_e_flush);
    }

    public void ResetStream()
    {
        using var cctx = _handle.Acquire();
        Methods.ZSTD_CCtx_reset(cctx, ZSTD_ResetDirective.ZSTD_reset_session_only).EnsureZstdSuccess();
    }

    internal OperationStatus WrapStream(ReadOnlySpan<byte> source, Span<byte> destination, out int bytesConsumed, out int bytesWritten, ZSTD_EndDirective directive)
    {
        using var cctx = _handle.Acquire();
        bytesConsumed = bytesWritten = 0;

        fixed (byte* srcPtr = source)
        fixed (byte* dstPtr = destination)
        {
            var input = new ZSTD_inBuffer_s { src = srcPtr, size = (nuint)source.Length, pos = 0 };
            var output = new ZSTD_outBuffer_s { dst = dstPtr, size = (nuint)destination.Length, pos = 0 };

            while (output.pos != output.size)
            {
                var remaining = Methods.ZSTD_compressStream2(cctx, &output, &input, directive);
                bytesConsumed = (int)input.pos;
                bytesWritten = (int)output.pos;

                if (Methods.ZSTD_isError(remaining))
                    return OperationStatus.InvalidData;

                // input is finished and no more internal buffers left
                if (input.pos == input.size && remaining == 0)
                    return OperationStatus.Done;
            }

            return OperationStatus.DestinationTooSmall;
        }
    }
}