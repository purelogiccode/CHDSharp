using VendoredZSTD.Unsafe;

namespace VendoredZSTD;

public unsafe class Decompressor : IDisposable
{
    /*
     * We have a finalizer that releases dctx (to prevent memory leaks if Disposed is not called),
     * so we need to delay running the object's finalizer when dealing with dctx inside our methods.
     * For this purpose we use GC.KeepAlive(this)
     * For reference: https://devblogs.microsoft.com/oldnewthing/20100813-00/?p=13153
     */
    private ZstdDCtxS* _dctx;

    public Decompressor()
    {
        _dctx = Methods.ZSTD_createDCtx();
        if (_dctx == null)
            throw new ZstdException(ZstdErrorCode.ZstdErrorGeneric, "Failed to create dctx");
    }

    public void Dispose()
    {
        ReleaseUnmanagedResources();
        GC.SuppressFinalize(this);
    }

    ~Decompressor()
    {
        ReleaseUnmanagedResources();
    }

    public void SetParameter(ZstdDParameter parameter, int value)
    {
        EnsureNotDisposed();
        Methods.ZSTD_DCtx_setParameter(_dctx, parameter, value).EnsureZstdSuccess();
        GC.KeepAlive(this);
    }

    public int GetParameter(ZstdDParameter parameter)
    {
        EnsureNotDisposed();
        int value;
        Methods.ZSTD_DCtx_getParameter(_dctx, parameter, &value).EnsureZstdSuccess();
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
            Methods.ZSTD_DCtx_loadDictionary(_dctx, null, 0).EnsureZstdSuccess();
        else
            fixed (byte* dictPtr = dict)
            {
                Methods
                    .ZSTD_DCtx_loadDictionary(_dctx, dictPtr, (nuint)dict.Length)
                    .EnsureZstdSuccess();
            }

        GC.KeepAlive(this);
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
            throw new ZstdException(
                ZstdErrorCode.ZstdErrorDstSizeTooSmall,
                $"Decompressed content size {expectedDstSize} is greater than {nameof(maxDecompressedSize)} {maxDecompressedSize}"
            );
        if (expectedDstSize > Constants.MaxByteArrayLength)
            throw new ZstdException(
                ZstdErrorCode.ZstdErrorDstSizeTooSmall,
                $"Decompressed content size {expectedDstSize} is greater than max possible byte array size {Constants.MaxByteArrayLength}"
            );

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
        EnsureNotDisposed();
        fixed (byte* srcPtr = src)
        fixed (byte* destPtr = dest)
        {
            var returnValue = (int)
                Methods
                    .ZSTD_decompressDCtx(
                        _dctx,
                        destPtr,
                        (nuint)dest.Length,
                        srcPtr,
                        (nuint)src.Length
                    )
                    .EnsureZstdSuccess();
            GC.KeepAlive(this);
            return returnValue;
        }
    }

    public int Unwrap(
        byte[] src,
        int srcOffset,
        int srcLength,
        byte[] dst,
        int dstOffset,
        int dstLength
    )
    {
        return Unwrap(
            new ReadOnlySpan<byte>(src, srcOffset, srcLength),
            new Span<byte>(dst, dstOffset, dstLength)
        );
    }

    public bool TryUnwrap(byte[] src, byte[] dest, int offset, out int written)
    {
        return TryUnwrap(src, new Span<byte>(dest, offset, dest.Length - offset), out written);
    }

    public bool TryUnwrap(ReadOnlySpan<byte> src, Span<byte> dest, out int written)
    {
        EnsureNotDisposed();
        fixed (byte* srcPtr = src)
        fixed (byte* destPtr = dest)
        {
            var returnValue = Methods.ZSTD_decompressDCtx(
                _dctx,
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

    public bool TryUnwrap(
        byte[] src,
        int srcOffset,
        int srcLength,
        byte[] dst,
        int dstOffset,
        int dstLength,
        out int written
    )
    {
        return TryUnwrap(
            new ReadOnlySpan<byte>(src, srcOffset, srcLength),
            new Span<byte>(dst, dstOffset, dstLength),
            out written
        );
    }

    private void ReleaseUnmanagedResources()
    {
        if (_dctx != null)
        {
            Methods.ZSTD_freeDCtx(_dctx);
            _dctx = null;
        }
    }

    private void EnsureNotDisposed()
    {
        if (_dctx == null)
            throw new ObjectDisposedException(nameof(Decompressor));
    }

    internal nuint DecompressStream(ref ZstdInBufferS input, ref ZstdOutBufferS output)
    {
        fixed (ZstdInBufferS* inputPtr = &input)
        fixed (ZstdOutBufferS* outputPtr = &output)
        {
            var returnValue = Methods
                .ZSTD_decompressStream(_dctx, outputPtr, inputPtr)
                .EnsureZstdSuccess();
            GC.KeepAlive(this);
            return returnValue;
        }
    }
}