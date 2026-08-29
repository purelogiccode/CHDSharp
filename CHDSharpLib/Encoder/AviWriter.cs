using System.Buffers.Binary;
using System.Text;

namespace CHDSharp.Encoder;

/// <summary>
///     Minimal AVI 1.0 file writer for <c>extractld</c> output: DIB video frames (YUY2)
///     + interleaved PCM audio. The output is a valid RIFF/AVI file with an <c>idx1</c> index.
/// </summary>
internal sealed class AviWriter : IDisposable
{
    // Fixed 'indx' (master AVI index) reservation per stream, exactly like MAME's
    // aviio.cpp write_indx_chunk: 24 + 16 * MAX_AVI_SIZE_IN_GB / 4 (MAX_AVI_SIZE_IN_GB=1024).
    // With no entries in use MAME overwrites the chunk id with 'JUNK' but keeps the size.
    private const uint AviIndexChunkDataSize = 24 + 16 * 1024 / 4; // 4120
    private const uint AvifHasIndex = 0x10;
    private const uint AvifIsInterleaved = 0x100;
    private const uint HandlerDib = 0x20424944; // 'DIB '
    private static readonly uint FccYuy2 = FourCc("YUY2");
    private static readonly uint Fcc00Dc = FourCc("00dc");
    private static readonly uint Fcc01Wb = FourCc("01wb");
    private readonly uint _audioChannels;
    private readonly uint _audioSampleRate;
    private readonly uint _height;

    private readonly List<(uint Id, long Offset, uint Size)> _index = [];
    private readonly Stream _stream;
    private readonly uint _videoSampletime;
    private readonly uint _videoTimescale;
    private readonly uint _width;
    private uint _audioSampleCount;
    private long _audioStrhLengthPos;
    private long _avihFrameCountPos;
    private bool _finalized;
    private long _moviSizePos;

    // patch positions
    private long _riffSizePos;
    private uint _videoFrameCount;
    private long _videoStrhLengthPos;

    private AviWriter(
        Stream stream,
        uint width,
        uint height,
        uint videoTimescale,
        uint videoSampletime,
        uint audioChannels,
        uint audioSampleRate
    )
    {
        _stream = stream;
        _width = width;
        _height = height;
        _videoTimescale = videoTimescale;
        _videoSampletime = videoSampletime;
        _audioChannels = audioChannels;
        _audioSampleRate = audioSampleRate;
    }

    public void Dispose()
    {
        FinalizeFile();
        _stream.Dispose();
    }

    internal static AviWriter Create(
        string path,
        uint width,
        uint height,
        uint videoTimescale,
        uint videoSampletime,
        uint audioChannels,
        uint audioSampleRate
    )
    {
        var fs = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            1024 * 1024
        );
        var writer = new AviWriter(
            fs,
            width,
            height,
            videoTimescale,
            videoSampletime,
            audioChannels,
            audioSampleRate
        );
        writer.WriteHeaders();
        return writer;
    }

    internal static AviWriter Create(
        Stream stream,
        uint width,
        uint height,
        uint videoTimescale,
        uint videoSampletime,
        uint audioChannels,
        uint audioSampleRate
    )
    {
        if (!stream.CanSeek)
            throw new ArgumentException("Stream must be seekable", nameof(stream));

        var writer = new AviWriter(
            stream,
            width,
            height,
            videoTimescale,
            videoSampletime,
            audioChannels,
            audioSampleRate
        );
        writer.WriteHeaders();
        return writer;
    }

    internal void AppendVideoFrame(byte[] yuy2Data)
    {
        ObjectDisposedException.ThrowIf(_finalized, this);
        var offset = _stream.Position - (_moviSizePos + 4);
        WriteU32(Fcc00Dc);
        WriteU32((uint)yuy2Data.Length);
        _stream.Write(yuy2Data);
        if (yuy2Data.Length % 2 != 0)
            _stream.WriteByte(0);
        _index.Add((Fcc00Dc, offset, (uint)yuy2Data.Length));
        _videoFrameCount++;
    }

    internal void AppendSoundSamples(byte[] pcmData, uint sampleCount)
    {
        ObjectDisposedException.ThrowIf(_finalized, this);
        var offset = _stream.Position - (_moviSizePos + 4);
        WriteU32(Fcc01Wb);
        WriteU32((uint)pcmData.Length);
        _stream.Write(pcmData);
        if (pcmData.Length % 2 != 0)
            _stream.WriteByte(0);
        _index.Add((Fcc01Wb, offset, (uint)pcmData.Length));
        _audioSampleCount += sampleCount;
    }

    private void WriteHeaders()
    {
        // RIFF 'AVI '
        WriteFourCc("RIFF");
        _riffSizePos = _stream.Position;
        WriteU32(0); // patched in FinalizeFile
        WriteFourCc("AVI ");

        // LIST 'hdrl'
        WriteFourCc("LIST");
        var hdrlSizePos = _stream.Position;
        WriteU32(0); // patched below
        WriteFourCc("hdrl");

        // avih
        var avihStart = _stream.Position;
        WriteFourCc("avih");
        WriteU32(56);
        var usecPerFrame =
            _videoTimescale > 0 ? (uint)((ulong)_videoSampletime * 1000000 / _videoTimescale) : 0;
        WriteAvih(usecPerFrame);
        // avih has a fixed size of 56 bytes, frame count at offset 16
        _avihFrameCountPos = avihStart + 4 + 4 + 16; // fourcc + size + 16

        // video LIST 'strl'
        WriteFourCc("LIST");
        var videoStrlSizePos = _stream.Position;
        WriteU32(0); // patched below
        WriteFourCc("strl");

        // video strh
        WriteFourCc("strh");
        WriteU32(56);
        var videoStrhStart = _stream.Position;
        WriteVideoStrh();
        _videoStrhLengthPos = videoStrhStart + 32; // dwLength at offset 32 in strh

        // video strf
        WriteFourCc("strf");
        WriteU32(40);
        WriteVideoStrf();

        // fixed index reservation, matches MAME's indx chunk (JUNK when unused)
        WriteFourCc("JUNK");
        WriteU32(AviIndexChunkDataSize);
        for (var i = 0; i < AviIndexChunkDataSize; i++)
            _stream.WriteByte(0);

        // patch video strl size
        PatchLength(videoStrlSizePos, (uint)(_stream.Position - videoStrlSizePos - 4));

        // audio LIST 'strl' (only when the CHD carries audio; chdman skips it for 0 channels)
        if (_audioChannels > 0)
        {
            WriteFourCc("LIST");
            var audioStrlSizePos = _stream.Position;
            WriteU32(0); // patched below
            WriteFourCc("strl");

            // audio strh
            WriteFourCc("strh");
            WriteU32(56);
            var audioStrhStart = _stream.Position;
            WriteAudioStrh();
            _audioStrhLengthPos = audioStrhStart + 32; // dwLength at offset 32 in strh

            // audio strf
            WriteFourCc("strf");
            WriteU32(16);
            WriteAudioStrf();

            // fixed index reservation, matches MAME's indx chunk (JUNK when unused)
            WriteFourCc("JUNK");
            WriteU32(AviIndexChunkDataSize);
            for (var i = 0; i < AviIndexChunkDataSize; i++)
                _stream.WriteByte(0);

            // patch audio strl size
            PatchLength(audioStrlSizePos, (uint)(_stream.Position - audioStrlSizePos - 4));
        }

        // patch hdrl size
        PatchLength(hdrlSizePos, (uint)(_stream.Position - hdrlSizePos - 4));

        // LIST 'movi'
        WriteFourCc("LIST");
        _moviSizePos = _stream.Position;
        WriteU32(0); // patched in FinalizeFile
        WriteFourCc("movi");
    }

    private void FinalizeFile()
    {
        if (_finalized)
            return;

        _finalized = true;

        // close movi LIST
        PatchLength(_moviSizePos, (uint)(_stream.Position - _moviSizePos - 4));

        // write idx1
        WriteFourCc("idx1");
        WriteU32((uint)(_index.Count * 16));
        var buf = new byte[16];
        foreach (var (id, offset, size) in _index)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(0), id);
            BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(4), id == Fcc00Dc ? 0x10u : 0u);
            BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(8), (uint)offset);
            BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(12), size);
            _stream.Write(buf);
        }

        // patch RIFF size
        PatchLength(_riffSizePos, (uint)(_stream.Position - _riffSizePos - 4));

        // patch frame counts
        PatchU32(_avihFrameCountPos, _videoFrameCount);
        PatchU32(_videoStrhLengthPos, _videoFrameCount);
        if (_audioChannels > 0)
            PatchU32(_audioStrhLengthPos, _audioSampleCount);
    }

    private void WriteAvih(uint usecPerFrame)
    {
        var buf = new byte[56];
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(0), usecPerFrame);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(12), AvifHasIndex | AvifIsInterleaved);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(16), 0); // dwTotalFrames (patched)
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(24), _audioChannels > 0 ? 2u : 1u);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(32), _width);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(36), _height);
        _stream.Write(buf);
    }

    private void WriteVideoStrh()
    {
        var buf = new byte[56];
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(0), FourCc("vids"));
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(4), HandlerDib);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(20), _videoSampletime);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(24), _videoTimescale);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(32), 0); // dwLength (patched)
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(36), _width * _height * 4);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(40), 10000); // dwQuality
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(52), (ushort)_width); // rcFrame.right
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(54), (ushort)_height); // rcFrame.bottom
        _stream.Write(buf);
    }

    private void WriteVideoStrf()
    {
        var buf = new byte[40];
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(0), 40);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(4), _width);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(8), _height);
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(14), 16);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(16), FccYuy2);
        // MAME aviio.cpp: width * height * (depth + 7) / 8 — the multiplication happens
        // before the division in uint32 math, i.e. (624 * 352 * 23) / 8, not 624 * 352 * 2.
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(20), _width * _height * 23u / 8u);
        _stream.Write(buf);
    }

    private void WriteAudioStrh()
    {
        var buf = new byte[56];
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(0), FourCc("auds"));
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(20), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(24), _audioSampleRate);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(32), 0); // dwLength (patched)
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(36), _audioSampleRate * _audioChannels * 2);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(44), _audioChannels * 2);
        _stream.Write(buf);
    }

    private void WriteAudioStrf()
    {
        var buf = new byte[16];
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(0), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(2), (ushort)_audioChannels);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(4), _audioSampleRate);
        BinaryPrimitives.WriteUInt32LittleEndian(
            buf.AsSpan(8),
            _audioSampleRate * _audioChannels * 2
        );
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(12), (ushort)(_audioChannels * 2));
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(14), 16);
        _stream.Write(buf);
    }

    private void WriteFourCc(string s)
    {
        var b = Encoding.ASCII.GetBytes(s);
        _stream.Write(b, 0, 4);
    }

    private void WriteU32(uint value)
    {
        Span<byte> buf = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buf, value);
        _stream.Write(buf);
    }

    private void PatchLength(long pos, uint length)
    {
        var cur = _stream.Position;
        _stream.Position = pos;
        WriteU32(length);
        _stream.Position = cur;
    }

    private void PatchU32(long pos, uint value)
    {
        var cur = _stream.Position;
        _stream.Position = pos;
        WriteU32(value);
        _stream.Position = cur;
    }

    private static uint FourCc(string s)
    {
        var b = Encoding.ASCII.GetBytes(s);
        return b[0] | ((uint)b[1] << 8) | ((uint)b[2] << 16) | ((uint)b[3] << 24);
    }
}