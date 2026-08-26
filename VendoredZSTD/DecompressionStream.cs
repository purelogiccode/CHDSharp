using System.Buffers;
using VendoredZSTD.Unsafe;

namespace VendoredZSTD;

public class DecompressionStream : Stream
{
    private readonly Stream _innerStream;
    private readonly byte[] _inputBuffer;
    private readonly int _inputBufferSize;
    private readonly bool _preserveDecompressor;
    private readonly bool _leaveOpen;
    private readonly bool _checkEndOfStream;
    private Decompressor? _decompressor;
    private ZstdInBufferS _input;
    private nuint _lastDecompressResult;
    private bool _contextDrained = true;

    public DecompressionStream(
        Stream stream,
        int bufferSize = 0,
        bool checkEndOfStream = true,
        bool leaveOpen = true
    )
        : this(stream, new Decompressor(), bufferSize, checkEndOfStream, false, leaveOpen)
    {
    }

    public DecompressionStream(
        Stream stream,
        Decompressor decompressor,
        int bufferSize = 0,
        bool checkEndOfStream = true,
        bool preserveDecompressor = true,
        bool leaveOpen = true
    )
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanRead)
            throw new ArgumentException("Stream is not readable", nameof(stream));

        if (bufferSize < 0)
            throw new ArgumentOutOfRangeException(nameof(bufferSize));

        _innerStream = stream;
        _decompressor = decompressor;
        _preserveDecompressor = preserveDecompressor;
        _leaveOpen = leaveOpen;
        _checkEndOfStream = checkEndOfStream;

        _inputBufferSize =
            bufferSize > 0 ? bufferSize : (int)Methods.ZSTD_DStreamInSize().EnsureZstdSuccess();
        _inputBuffer = ArrayPool<byte>.Shared.Rent(_inputBufferSize);
        _input = new ZstdInBufferS { pos = (nuint)_inputBufferSize, size = (nuint)_inputBufferSize };
    }

    public void SetParameter(ZstdDParameter parameter, int value)
    {
        EnsureNotDisposed();
        _decompressor!.SetParameter(parameter, value);
    }

    public int GetParameter(ZstdDParameter parameter)
    {
        EnsureNotDisposed();
        return _decompressor!.GetParameter(parameter);
    }

    public void LoadDictionary(byte[] dict)
    {
        EnsureNotDisposed();
        _decompressor!.LoadDictionary(dict);
    }

    ~DecompressionStream()
    {
        Dispose(false);
    }

    protected override void Dispose(bool disposing)
    {
        if (_decompressor == null)
            return;

        if (!_preserveDecompressor)
            _decompressor.Dispose();

        _decompressor = null;

        ArrayPool<byte>.Shared.Return(_inputBuffer);

        if (!_leaveOpen)
            _innerStream.Dispose();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        return Read(new Span<byte>(buffer, offset, count));
    }

#if !NETSTANDARD2_0 && !NETFRAMEWORK
    public override int Read(Span<byte> buffer)
#else
    public int Read(Span<byte> buffer)
#endif
    {
        EnsureNotDisposed();

        // Guard against infinite loop (output.pos would never become non-zero)
        if (buffer.Length == 0)
            return 0;

        var output = new ZstdOutBufferS { pos = 0, size = (nuint)buffer.Length };
        while (true)
        {
            // If there is still input available, or there might be data buffered in the decompressor context, flush that out
            while (_input.pos < _input.size || !_contextDrained)
            {
                var oldInputPos = _input.pos;
                var result = DecompressStream(ref output, buffer);
                if (output.pos > 0 || oldInputPos != _input.pos)
                    // Keep result from last decompress call that made some progress, so we known if we're at end of frame
                    _lastDecompressResult = result;

                // If decompression filled the output buffer, there might still be data buffered in the decompressor context
                _contextDrained = output.pos < output.size;
                // If we have data to return, return it immediately, so we won't stall on Read
                if (output.pos > 0)
                    return (int)output.pos;
            }

            // Otherwise, read some more input
            int bytesRead;
            if ((bytesRead = _innerStream.Read(_inputBuffer, 0, _inputBufferSize)) == 0)
            {
                if (_checkEndOfStream && _lastDecompressResult != 0)
                    throw new EndOfStreamException("Premature end of stream");

                return 0;
            }

            _input.size = (nuint)bytesRead;
            _input.pos = 0;
        }
    }

    public override Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken
    )
    {
        return ReadAsync(new Memory<byte>(buffer, offset, count), cancellationToken).AsTask();
    }

#if !NETSTANDARD2_0 && !NETFRAMEWORK
    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default
    )
#else
    public async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default
    )
#endif
    {
        EnsureNotDisposed();

        // Guard against infinite loop (output.pos would never become non-zero)
        if (buffer.Length == 0)
            return 0;

        var output = new ZstdOutBufferS { pos = 0, size = (nuint)buffer.Length };
        while (true)
        {
            // If there is still input available, or there might be data buffered in the decompressor context, flush that out
            while (_input.pos < _input.size || !_contextDrained)
            {
                var oldInputPos = _input.pos;
                var result = DecompressStream(ref output, buffer.Span);
                if (output.pos > 0 || oldInputPos != _input.pos)
                    // Keep result from last decompress call that made some progress, so we known if we're at end of frame
                    _lastDecompressResult = result;

                // If decompression filled the output buffer, there might still be data buffered in the decompressor context
                _contextDrained = output.pos < output.size;
                // If we have data to return, return it immediately, so we won't stall on Read
                if (output.pos > 0)
                    return (int)output.pos;
            }

            // Otherwise, read some more input
            int bytesRead;
            if (
                (
                    bytesRead = await _innerStream
                        .ReadAsync(_inputBuffer, 0, _inputBufferSize, cancellationToken)
                        .ConfigureAwait(false)
                ) == 0
            )
            {
                if (_checkEndOfStream && _lastDecompressResult != 0)
                    throw new EndOfStreamException("Premature end of stream");

                return 0;
            }

            _input.size = (nuint)bytesRead;
            _input.pos = 0;
        }
    }

    private unsafe nuint DecompressStream(ref ZstdOutBufferS output, Span<byte> outputBuffer)
    {
        fixed (byte* inputBufferPtr = _inputBuffer)
        fixed (byte* outputBufferPtr = outputBuffer)
        {
            _input.src = inputBufferPtr;
            output.dst = outputBufferPtr;
            return _decompressor!.DecompressStream(ref _input, ref output);
        }
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
        throw new NotSupportedException();
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    private void EnsureNotDisposed()
    {
        if (_decompressor == null)
            throw new ObjectDisposedException(nameof(DecompressionStream));
    }

#if NETSTANDARD2_0 || NETFRAMEWORK
    public virtual ValueTask DisposeAsync()
    {
        try
        {
            Dispose();
            return default;
        }
        catch (Exception exc)
        {
            return new ValueTask(Task.FromException(exc));
        }
    }
#endif
}