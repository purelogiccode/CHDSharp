using CHDSharp.Encoder.Interfaces;

namespace CHDSharp.Encoder;

/// <summary>
///     MAME A/V Huffman codec ('avhu'), matching <c>chd_avhuff_compressor</c>: each hunk is one
///     raw 'chav' A/V frame (assembled by <see cref="ChdEncoder.EncodeLaserDisc" />) compressed as
///     delta-RLE Huffman video + per-channel mono FLAC audio via <see cref="AvHuffEncoder" />.
///     Multi-frame hunks (hunkBytes > bytesPerFrame) are stored raw, matching MAME's codec-chain
///     behavior where the avhuff compress fails on already-encoded data.
///     Decodable by CHDSharpLib's <c>ChdReaders.AvHuff</c> and chdman.
/// </summary>
public sealed class AvHuffCodec : IChdCodec
{
    /// <inheritdoc />
    public uint Tag => CodecTags.Avhu;

    /// <inheritdoc />
    public byte[]? Compress(byte[] data)
    {
        if (
            data.Length < 12
            || data[0] != 'c'
            || data[1] != 'h'
            || data[2] != 'a'
            || data[3] != 'v'
        )
            return null;

        // Determine raw frame size from the 'chav' header. The hunk may hold several
        // whole frames (multi-frame hunks) or a single frame padded with zeroes to the
        // maximum sample count. MAME's avhuff codec compresses single-frame hunks even
        // when the tail is zero-padded (its encoder uses the header's sample count), and
        // stores multi-frame hunks raw. Only reject when non-zero data follows the frame.
        uint channels = data[5];
        var samples = (uint)((data[6] << 8) | data[7]);
        var width = (uint)((data[8] << 8) | data[9]);
        var height = (uint)((data[10] << 8) | data[11]);
        var rawFrameSize = AvHuffEncoder.RawDataSize(width, height, channels, samples);
        if (rawFrameSize > 0 && data.Length > rawFrameSize)
        {
            var hasTrailingData = false;
            for (var i = rawFrameSize; i < data.Length; i++)
                if (data[(int)i] != 0)
                {
                    hasTrailingData = true;
                    break;
                }

            if (hasTrailingData)
                return null;
        }

        var encoder = new AvHuffEncoder();
        var dest = new byte[data.Length];
        int length;
        try
        {
            length = encoder.EncodeData(data, dest);
        }
        catch (InvalidDataException)
        {
            return null;
        }

        return length < data.Length ? dest.AsSpan(0, length).ToArray() : null;
    }
}
