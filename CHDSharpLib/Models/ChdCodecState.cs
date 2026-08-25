using VendoredFlac;
using VendoredFlac.Models.FlacDeps;
using VendoredZSTD;

namespace CHDSharp.Models;

/// <summary>
///     Holds per-codec state and scratch buffers used across multiple hunk decompressions, avoiding repeated
///     allocations.
/// </summary>
internal class ChdCodecState : IDisposable
{
    /// <summary>Reusable AVHuff audio decoder instance.</summary>
    internal AudioDecoder? AvhuffAudioDecoder;

    /// <summary>AVHuff audio configuration.</summary>
    internal AudioPcmConfig? AvhuffSettings;

    /// <summary>Huffman lookup table for standard audio/sector Huffman decoding.</summary>
    internal ushort[]? BHuffman;

    /// <summary>Huffman lookup table for AVHuff video Cb (chroma blue) channel.</summary>
    internal ushort[]? BHuffmanCb;

    /// <summary>Huffman lookup table for AVHuff video Cr (chroma red) channel.</summary>
    internal ushort[]? BHuffmanCr;

    /// <summary>Huffman lookup table for AVHuff video high-byte decoding.</summary>
    internal ushort[]? BHuffmanHi;

    /// <summary>Huffman lookup table for AVHuff video low-byte decoding.</summary>
    internal ushort[]? BHuffmanLo;

    /// <summary>Huffman lookup table for AVHuff video Y (luma) channel.</summary>
    internal ushort[]? BHuffmanY;

    /// <summary>Scratch buffer for CD sector data reassembly.</summary>
    internal byte[]? BSector;

    /// <summary>Scratch buffer for CD subcode data reassembly.</summary>
    internal byte[]? BSubcode;

    /// <summary>Reusable Zstandard decompressor instance.</summary>
    internal Decompressor? BZstd;

    /// <summary>Scratch buffer for LZMA decompression (reused across hunks).</summary>
    internal byte[]? Blzma;

    /// <summary>Reusable FLAC audio output buffer.</summary>
    internal AudioBuffer? FlacAudioBuffer;

    /// <summary>Reusable FLAC audio decoder instance.</summary>
    internal AudioDecoder? FlacAudioDecoder;

    /// <summary>FLAC audio configuration (16-bit, 2-channel, 44100 Hz).</summary>
    internal AudioPcmConfig? FlacSettings;

    /// <summary>Releases all disposable codec resources and clears scratch buffers.</summary>
    public void Dispose()
    {
        BZstd?.Dispose();
        BZstd = null;

        FlacAudioDecoder?.Close();
        FlacAudioDecoder = null;

        AvhuffAudioDecoder?.Close();
        AvhuffAudioDecoder = null;

        FlacSettings = null;
        FlacAudioBuffer = null;
        AvhuffSettings = null;

        BSector = null;
        BSubcode = null;
        Blzma = null;

        BHuffman = null;
        BHuffmanHi = null;
        BHuffmanLo = null;
        BHuffmanY = null;
        BHuffmanCb = null;
        BHuffmanCr = null;
    }
}