using VendoredZSTD.Unsafe;

namespace VendoredZSTD;

public unsafe class Compressor : IDisposable
{
    public const int DefaultCompressionLevel = 0;

    /*
     * We have a finalizer that releases cctx (to prevent memory leaks if Disposed is not called),
     * so we need to delay running the object's finalizer when dealing with cctx inside our methods.
     * For this purpose we use GC.KeepAlive(this)
     * For reference: https://devblogs.microsoft.com/oldnewthing/20100813-00/?p=13153
     */
    private ZstdCCtxS* cctx;

    private int level = DefaultCompressionLevel;

    public Compressor(int level = DefaultCompressionLevel)
    {
        cctx = Methods.ZSTD_createCCtx();
        if (cctx == null)
            throw new ZstdException(ZstdErrorCode.ZstdErrorGeneric, "Failed to create cctx");

        Level = level;
    }

    public static int MinCompressionLevel => Methods.ZSTD_minCLevel();
    public static int MaxCompressionLevel => Methods.ZSTD_maxCLevel();

    public int Level
    {
        get => level;
        set
        {
            if (level != value)
            {
                level = value;
                SetParameter(ZstdCParameter.ZstdCCompressionLevel, value);
            }
        }
    }

    public void Dispose()
    {
        ReleaseUnmanagedResources();
        GC.SuppressFinalize(this);
    }

    public void SetParameter(ZstdCParameter parameter, int value)
    {
        EnsureNotDisposed();
        Methods.ZSTD_CCtx_setParameter(cctx, parameter, value).EnsureZstdSuccess();
        GC.KeepAlive(this);
    }

    public int GetParameter(ZstdCParameter parameter)
    {
        EnsureNotDisposed();
        int value;
        Methods.ZSTD_CCtx_getParameter(cctx, parameter, &value).EnsureZstdSuccess();
        GC.KeepAlive(this);
        return value;
    }

    public void LoadDictionary(byte[] dict)
    {
        var dictReadOnlySpan = new ReadOnlySpan<byte>(dict);
        LoadDictionary(dictReadOnlySpan);
    }

    public void LoadDictionary(ReadOnlySpan<byte> dict)
    {
        EnsureNotDisposed();
        if (dict.IsEmpty)
            Methods.ZSTD_CCtx_loadDictionary(cctx, null, 0).EnsureZstdSuccess();
        else
            fixed (byte* dictPtr = dict)
            {
                Methods
                    .ZSTD_CCtx_loadDictionary(cctx, dictPtr, (nuint)dict.Length)
                    .EnsureZstdSuccess();
            }

        GC.KeepAlive(this);
    }

    ~Compressor()
    {
        ReleaseUnmanagedResources();
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
        EnsureNotDisposed();
        fixed (byte* srcPtr = src)
        fixed (byte* destPtr = dest)
        {
            var returnValue = (int)
                Methods
                    .ZSTD_compress2(cctx, destPtr, (nuint)dest.Length, srcPtr, (nuint)src.Length)
                    .EnsureZstdSuccess();
            GC.KeepAlive(this);
            return returnValue;
        }
    }

    public int Wrap(ArraySegment<byte> src, ArraySegment<byte> dest)
    {
        return Wrap((ReadOnlySpan<byte>)src, dest);
    }

    public int Wrap(
        byte[] src,
        int srcOffset,
        int srcLength,
        byte[] dst,
        int dstOffset,
        int dstLength
    )
    {
        return Wrap(
            new ReadOnlySpan<byte>(src, srcOffset, srcLength),
            new Span<byte>(dst, dstOffset, dstLength)
        );
    }

    public bool TryWrap(byte[] src, byte[] dest, int offset, out int written)
    {
        return TryWrap(src, new Span<byte>(dest, offset, dest.Length - offset), out written);
    }

    public bool TryWrap(ReadOnlySpan<byte> src, Span<byte> dest, out int written)
    {
        EnsureNotDisposed();
        fixed (byte* srcPtr = src)
        fixed (byte* destPtr = dest)
        {
            var returnValue = Methods.ZSTD_compress2(
                cctx,
                destPtr,
                (nuint)dest.Length,
                srcPtr,
                (nuint)src.Length
            );
            GC.KeepAlive(this);

            if (returnValue == unchecked(0 - (nuint)ZstdErrorCode.ZstdErrorDstSizeTooSmall))
            {
                written = default;
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

    public bool TryWrap(
        byte[] src,
        int srcOffset,
        int srcLength,
        byte[] dst,
        int dstOffset,
        int dstLength,
        out int written
    )
    {
        return TryWrap(
            new ReadOnlySpan<byte>(src, srcOffset, srcLength),
            new Span<byte>(dst, dstOffset, dstLength),
            out written
        );
    }

    private void ReleaseUnmanagedResources()
    {
        if (cctx != null)
        {
            Methods.ZSTD_freeCCtx(cctx);
            cctx = null;
        }
    }

    private void EnsureNotDisposed()
    {
        if (cctx == null)
            throw new ObjectDisposedException(nameof(Compressor));
    }

    internal nuint CompressStream(
        ref ZstdInBufferS input,
        ref ZstdOutBufferS output,
        ZstdEndDirective directive
    )
    {
        fixed (ZstdInBufferS* inputPtr = &input)
        fixed (ZstdOutBufferS* outputPtr = &output)
        {
            var returnValue = Methods
                .ZSTD_compressStream2(cctx, outputPtr, inputPtr, directive)
                .EnsureZstdSuccess();
            GC.KeepAlive(this);
            return returnValue;
        }
    }
}