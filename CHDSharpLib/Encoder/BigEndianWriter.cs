namespace CHDSharp.Encoder;

/// <summary>
///     A growable big-endian byte buffer used internally for serializing CHD header fields,
///     map entries, and metadata entries. All multi-byte values are written in big-endian
///     (network) byte order, matching the CHD on-disk format.
/// </summary>
internal class BigEndianWriter
{
    private byte[] _buffer;

    /// <summary>Initializes a new <see cref="BigEndianWriter" /> with the given initial capacity.</summary>
    /// <param name="capacity">The initial buffer size in bytes (default 256).</param>
    internal BigEndianWriter(int capacity = 256)
    {
        _buffer = new byte[capacity];
        Position = 0;
    }

    /// <summary>Gets the current write position (number of bytes written so far).</summary>
    internal int Position { get; private set; }

    /// <summary>Writes a single byte at the current position and advances by 1.</summary>
    /// <param name="v">The byte value to write.</param>
    internal void WriteU8(byte v)
    {
        EnsureCapacity(1);
        _buffer[Position++] = v;
    }

    /// <summary>Writes a 16-bit unsigned integer in big-endian order and advances by 2.</summary>
    /// <param name="v">The value to write.</param>
    internal void WriteU16(ushort v)
    {
        EnsureCapacity(2);
        _buffer[Position] = (byte)(v >> 8);
        _buffer[Position + 1] = (byte)v;
        Position += 2;
    }

    /// <summary>Writes a 24-bit unsigned integer in big-endian order and advances by 3.</summary>
    /// <param name="v">The value to write (only the low 24 bits are stored).</param>
    internal void WriteU24(uint v)
    {
        EnsureCapacity(3);
        _buffer[Position] = (byte)(v >> 16);
        _buffer[Position + 1] = (byte)(v >> 8);
        _buffer[Position + 2] = (byte)v;
        Position += 3;
    }

    /// <summary>Writes a 32-bit unsigned integer in big-endian order and advances by 4.</summary>
    /// <param name="v">The value to write.</param>
    internal void WriteU32(uint v)
    {
        EnsureCapacity(4);
        _buffer[Position] = (byte)(v >> 24);
        _buffer[Position + 1] = (byte)(v >> 16);
        _buffer[Position + 2] = (byte)(v >> 8);
        _buffer[Position + 3] = (byte)v;
        Position += 4;
    }

    /// <summary>Writes a 48-bit unsigned integer in big-endian order and advances by 6.</summary>
    /// <param name="v">The value to write (only the low 48 bits are stored).</param>
    internal void WriteU48(ulong v)
    {
        EnsureCapacity(6);
        _buffer[Position] = (byte)(v >> 40);
        _buffer[Position + 1] = (byte)(v >> 32);
        _buffer[Position + 2] = (byte)(v >> 24);
        _buffer[Position + 3] = (byte)(v >> 16);
        _buffer[Position + 4] = (byte)(v >> 8);
        _buffer[Position + 5] = (byte)v;
        Position += 6;
    }

    /// <summary>Writes a 64-bit unsigned integer in big-endian order and advances by 8.</summary>
    /// <param name="v">The value to write.</param>
    internal void WriteU64(ulong v)
    {
        EnsureCapacity(8);
        _buffer[Position] = (byte)(v >> 56);
        _buffer[Position + 1] = (byte)(v >> 48);
        _buffer[Position + 2] = (byte)(v >> 40);
        _buffer[Position + 3] = (byte)(v >> 32);
        _buffer[Position + 4] = (byte)(v >> 24);
        _buffer[Position + 5] = (byte)(v >> 16);
        _buffer[Position + 6] = (byte)(v >> 8);
        _buffer[Position + 7] = (byte)v;
        Position += 8;
    }

    /// <summary>Writes a span of bytes at the current position and advances by its length.</summary>
    /// <param name="data">The bytes to write.</param>
    internal void WriteBytes(ReadOnlySpan<byte> data)
    {
        EnsureCapacity(data.Length);
        data.CopyTo(_buffer.AsSpan(Position));
        Position += data.Length;
    }

    /// <summary>Writes zero bytes at the current position and advances by <paramref name="count" />.</summary>
    /// <param name="count">The number of zero bytes to write.</param>
    internal void WriteZeroes(int count)
    {
        EnsureCapacity(count);
        Array.Clear(_buffer, Position, count);
        Position += count;
    }

    /// <summary>Returns a copy of the written bytes as a new array sized to <see cref="Position" />.</summary>
    /// <returns>A byte array containing the written data.</returns>
    internal byte[] ToArray()
    {
        var result = new byte[Position];
        Array.Copy(_buffer, result, Position);
        return result;
    }

    /// <summary>Returns a span over the written bytes (from the start of the internal buffer to <see cref="Position" />).</summary>
    /// <returns>A <see cref="Span{T}" /> of the written data.</returns>
    internal Span<byte> AsSpan()
    {
        return _buffer.AsSpan(0, Position);
    }

    private void EnsureCapacity(int bytes)
    {
        var needed = Position + bytes;
        if (needed <= _buffer.Length)
            return;

        var newSize = _buffer.Length * 2;
        while (newSize < needed)
            newSize *= 2;

        Array.Resize(ref _buffer, newSize);
    }
}
