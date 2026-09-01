using System.Buffers;
using VendoredZSTD.Unsafe;

namespace VendoredZSTD;

public class CompressionStream : Stream
{
    private readonly Stream _innerStream;
    private readonly byte[] _outputBuffer;
    private readonly bool _preserveCompressor;
    private readonly bool _leaveOpen;
    private Compressor? _compressor;
    private ZstdOutBufferS _output;

    public CompressionStream(
        Stream stream,
        int level = Compressor.DefaultCompressionLevel,
        int bufferSize = 0,
        bool leaveOpen = true
    )
        : this(stream, new Compressor(level), bufferSize, false, leaveOpen)
    {
    }

    public CompressionStream(
        Stream stream,
        Compressor compressor,
        int bufferSize = 0,
        bool preserveCompressor = true,
        bool leaveOpen = true
    )
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanWrite)
            throw new ArgumentException("Stream is not writable", nameof(stream));

        ArgumentOutOfRangeException.ThrowIfNegative(bufferSize);

        _innerStream = stream;
        _compressor = compressor;
        _preserveCompressor = preserveCompressor;
        _leaveOpen = leaveOpen;

        var outputBufferSize =
            bufferSize > 0 ? bufferSize : (int)Methods.ZSTD_CStreamOutSize().EnsureZstdSuccess();
        _outputBuffer = ArrayPool<byte>.Shared.Rent(outputBufferSize);
        _output = new ZstdOutBufferS { pos = 0, size = (nuint)outputBufferSize };
    }

    public void SetParameter(ZstdCParameter parameter, int value)
    {
        EnsureNotDisposed();
        _compressor!.SetParameter(parameter, value);
    }

    public int GetParameter(ZstdCParameter parameter)
    {
        EnsureNotDisposed();
        return _compressor!.GetParameter(parameter);
    }

    public void LoadDictionary(byte[] dict)
    {
        EnsureNotDisposed();
        _compressor!.LoadDictionary(dict);
    }

    ~CompressionStream()
    {
        Dispose(false);
    }

#if !NETSTANDARD2_0 && !NETFRAMEWORK
    public override async ValueTask DisposeAsync()
#else
    public async ValueTask DisposeAsync()
#endif
    {
        if (_compressor == null)
            return;

        try
        {
            await FlushInternalAsync(ZstdEndDirective.ZstdEEnd).ConfigureAwait(false);
        }
        finally
        {
            ReleaseUnmanagedResources();
            GC.SuppressFinalize(this);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (_compressor == null)
            return;

        try
        {
            if (disposing)
                FlushInternal(ZstdEndDirective.ZstdEEnd);
        }
        finally
        {
            ReleaseUnmanagedResources();
        }
    }

    private void ReleaseUnmanagedResources()
    {
        if (!_preserveCompressor)
            _compressor?.Dispose();

        _compressor = null;

        ArrayPool<byte>.Shared.Return(_outputBuffer);

        if (!_leaveOpen)
            _innerStream.Dispose();
    }

    public override void Flush()
    {
        FlushInternal(ZstdEndDirective.ZstdEFlush);
    }

    public override async Task FlushAsync(CancellationToken cancellationToken)
    {
        await FlushInternalAsync(ZstdEndDirective.ZstdEFlush, cancellationToken)
            .ConfigureAwait(false);
    }

    private void FlushInternal(ZstdEndDirective directive)
    {
        WriteInternal(null, directive);
    }

    private async Task FlushInternalAsync(
        ZstdEndDirective directive,
        CancellationToken cancellationToken = default
    )
    {
        await WriteInternalAsync(null, directive, cancellationToken).ConfigureAwait(false);
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        Write(new ReadOnlySpan<byte>(buffer, offset, count));
    }

#if !NETSTANDARD2_0 && !NETFRAMEWORK
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        WriteInternal(buffer, ZstdEndDirective.ZstdEContinue);
    }
#else
    public void Write(ReadOnlySpan<byte> buffer) =>
        WriteInternal(buffer, ZSTD_EndDirective.ZSTD_e_continue);
#endif

    private void WriteInternal(ReadOnlySpan<byte> buffer, ZstdEndDirective directive)
    {
        EnsureNotDisposed();

        var input = new ZstdInBufferS { pos = 0, size = (nuint)buffer.Length };
        nuint remaining;
        do
        {
            _output.pos = 0;
            remaining = CompressStream(ref input, buffer, directive);

            var written = (int)_output.pos;
            if (written > 0)
                _innerStream.Write(_outputBuffer, 0, written);
        } while (
            directive == ZstdEndDirective.ZstdEContinue ? input.pos < input.size : remaining > 0
        );
    }

    private async ValueTask WriteInternalAsync(
        ReadOnlyMemory<byte>? buffer,
        ZstdEndDirective directive,
        CancellationToken cancellationToken = default
    )
    {
        EnsureNotDisposed();

        var input = new ZstdInBufferS
        {
            pos = 0,
            size = buffer.HasValue ? (nuint)buffer.Value.Length : 0
        };
        nuint remaining;
        do
        {
            _output.pos = 0;
            remaining = CompressStream(
                ref input,
                buffer.HasValue ? buffer.Value.Span : null,
                directive
            );

            var written = (int)_output.pos;
            if (written > 0)
                await _innerStream
                    .WriteAsync(_outputBuffer.AsMemory(0, written), cancellationToken)
                    .ConfigureAwait(false);
        } while (
            directive == ZstdEndDirective.ZstdEContinue ? input.pos < input.size : remaining > 0
        );
    }

    public override Task WriteAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken
    )
    {
        return WriteAsync(new ReadOnlyMemory<byte>(buffer, offset, count), cancellationToken)
            .AsTask();
    }

#if !NETSTANDARD2_0 && !NETFRAMEWORK
    public override async ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default
    )
    {
        await WriteInternalAsync(buffer, ZstdEndDirective.ZstdEContinue, cancellationToken)
            .ConfigureAwait(false);
    }
#else
    public async ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default
    ) =>
        await WriteInternalAsync(buffer, ZSTD_EndDirective.ZSTD_e_continue, cancellationToken)
            .ConfigureAwait(false);
#endif

    internal unsafe nuint CompressStream(
        ref ZstdInBufferS input,
        ReadOnlySpan<byte> inputBuffer,
        ZstdEndDirective directive
    )
    {
        fixed (byte* inputBufferPtr = inputBuffer)
        {
            fixed (byte* outputBufferPtr = _outputBuffer)
            {
                input.src = inputBufferPtr;
                _output.dst = outputBufferPtr;
                return _compressor!.CompressStream(ref input, ref _output, directive).EnsureZstdSuccess();
            }
        }
    }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    private void EnsureNotDisposed()
    {
        if (_compressor == null)
            throw new ObjectDisposedException(nameof(CompressionStream));
    }
}