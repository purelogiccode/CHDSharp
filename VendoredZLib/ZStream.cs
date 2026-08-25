#nullable disable
// Copyright (C) 2022-2024 Magnus Montin


using System.Runtime.InteropServices;
using VendoredZLib.Deflate;
using VendoredZLib.Inflate;

namespace VendoredZLib;

/// <summary>
///     Represents a stream of data that can be compressed and uncompressed using the zlib data format.
/// </summary>
#pragma warning disable CA1711
public ref struct ZStream
#pragma warning restore CA1711
{
    internal uint NextInput; // the index of next input byte in the input buffer
    internal uint AvailIn; // number of bytes available at NextInput
    internal uint TotalInput; // total number of input bytes read so far

    internal uint NextOutput; // the index of next output byte in the output buffer
    internal uint AvailOut; // remaining free space at NextOutput

    // ReSharper disable once InconsistentNaming
    internal uint total_out; // total number of bytes output so far

    internal string Msg; // last error message

    internal InflateState InflateState;
    internal DeflateState DeflateState;

    internal int
        DataType2; // best guess about the data type: binary or text for deflate, or the decoding state for inflate

    internal ReadOnlySpan<byte> Input2;
    internal Span<byte> Output2;

#if NET7_0_OR_GREATER
    internal ref byte InputPtr;
    internal ref byte OutputPtr;
    internal InflateRefs InflateRefs;
    internal DeflateRefs DeflateRefs;
#endif

    /// <summary>
    ///     Gets or sets the input buffer.
    /// </summary>
    /// <remarks>
    ///     Setting the <see cref="Input" /> property resets the <see cref="AvailableIn" /> and <see cref="NextIn" />
    ///     properties to their default values.
    /// </remarks>
    public ReadOnlySpan<byte> Input
    {
        readonly get => Input2;
        set
        {
            Input2 = value;
            NextInput = 0;
            AvailIn = (uint)value.Length;
#if NET7_0_OR_GREATER
            InputPtr = ref MemoryMarshal.GetReference(Input2);
#endif
        }
    }

    /// <summary>
    ///     Gets or sets number of bytes available in <see cref="Input" />, starting from an offset specified by the
    ///     <see cref="NextIn" /> property.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="AvailableIn" /> is set to a negative value.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     <see cref="AvailableIn" /> is set to a value that is greater than the
    ///     length of the <see cref="Input" /> buffer minus the value of the <see cref="NextIn" /> property.
    /// </exception>
    /// <remarks>
    ///     If you choose to set this optional property, you should set it after you have set the <see cref="Input" />
    ///     property.
    /// </remarks>
    public int AvailableIn
    {
        readonly get => (int)AvailIn;
        set
        {
            ValidateAvailableBytes(value, NextInput, Input2, nameof(Input), nameof(NextIn));
            AvailIn = (uint)value;
        }
    }

    /// <summary>
    ///     Gets or sets the index of the next input byte in <see cref="Input" />.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="NextIn" /> is set to a negative value.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     <see cref="NextIn" /> is set to a value that is equal to or greater than
    ///     the size of the <see cref="Input" /> buffer.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     <see cref="NextIn" /> is set to a value that is not within the range of
    ///     available bytes in the <see cref="Input" /> buffer.
    /// </exception>
    /// <remarks>
    ///     If you choose to set this optional property, you should set it after you have set the <see cref="Input" />
    ///     property.
    /// </remarks>
    public int NextIn
    {
        readonly get => (int)NextInput;
        set
        {
            ValidateOffset(value, AvailableIn, Input2, nameof(Input));
            NextInput = (uint)value;
        }
    }

    /// <summary>
    ///     Gets the total number of input bytes read so far.
    /// </summary>
    public readonly uint TotalIn => TotalInput;

    /// <summary>
    ///     Gets or sets the output buffer.
    /// </summary>
    /// <remarks>
    ///     Setting the <see cref="Output" /> property resets the <see cref="AvailableOut" /> and <see cref="NextOut" />
    ///     properties to their default values.
    /// </remarks>
#pragma warning disable CA1819
    public Span<byte> Output
#pragma warning restore CA1819
    {
        readonly get => Output2;
        set
        {
            Output2 = value;
            NextOutput = 0;
            AvailOut = (uint)value.Length;
#if NET7_0_OR_GREATER
            OutputPtr = ref MemoryMarshal.GetReference(Output2);
#endif
        }
    }

    /// <summary>
    ///     Gets or sets the remaining free space in <see cref="Output" />, starting from an offset specified by the
    ///     <see cref="NextOut" /> property.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="AvailableOut" /> is set to a negative value.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     <see cref="AvailableOut" /> is set to a value that is greater than the
    ///     length of the <see cref="Output" /> buffer minus the value of the <see cref="NextOut" /> property.
    /// </exception>
    /// <remarks>
    ///     If you choose to set this optional property, you should set it after you have set the <see cref="Output" />
    ///     property.
    /// </remarks>
    public int AvailableOut
    {
        readonly get => (int)AvailOut;
        set
        {
            ValidateAvailableBytes(value, NextOutput, Output2, nameof(Output), nameof(NextOut));
            AvailOut = (uint)value;
        }
    }

    /// <summary>
    ///     Gets or sets the index of the next output byte in <see cref="Output" />.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="NextOut" /> is set to a negative value.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     <see cref="NextOut" /> is set to a value that is equal to or greater than
    ///     the size of the <see cref="Output" /> buffer.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     <see cref="NextOut" /> is set to a value that is not within the range of
    ///     available bytes in the <see cref="Output" /> buffer.
    /// </exception>
    /// <remarks>
    ///     If you choose to set this optional property, you should set it after you have set the <see cref="Output" />
    ///     property.
    /// </remarks>
    public int NextOut
    {
        readonly get => (int)NextOutput;
        set
        {
            ValidateOffset(value, AvailableOut, Output2, nameof(Output));
            NextOutput = (uint)value;
        }
    }

    /// <summary>
    ///     Gets the total number of bytes output so far.
    /// </summary>
    public readonly uint TotalOut => total_out;

    /// <summary>
    ///     Gets the last error message, or <see langword="null" /> if no error.
    /// </summary>
    public readonly string Message => Msg;

    /// <summary>
    ///     Gets a value that represents a best guess about the data type: binary or text for deflate, or the decoding state
    ///     for inflate.
    /// </summary>
    public readonly int DataType => DataType2;

    /// <summary>
    ///     Gets the Adler-32 value of the uncompressed data.
    /// </summary>
    public uint Adler { get; internal set; }

    private static void ValidateAvailableBytes(int value, uint offset, ReadOnlySpan<byte> buffer, string bufferName,
        string offsetPropertyName)
    {
        if (value < 0 || value > buffer.Length - offset)
            throw new ArgumentOutOfRangeException(nameof(value),
                $"Value was out of range. Must be non-negative and less than or equal to the size of the {bufferName} buffer minus the value of the {offsetPropertyName} property.");
    }

    private static void ValidateOffset(int value, int availableBytes, ReadOnlySpan<byte> buffer, string bufferName)
    {
        if (value < 0 || value >= buffer.Length)
            throw new ArgumentOutOfRangeException(nameof(value),
                $"Value was out of range. Must be non-negative and less than the size of the {bufferName} buffer.");
        if (buffer.Length - value < availableBytes)
            throw new ArgumentOutOfRangeException(nameof(value),
                $"The value must refer to a location within the available bytes of the {bufferName} buffer.");
    }
}