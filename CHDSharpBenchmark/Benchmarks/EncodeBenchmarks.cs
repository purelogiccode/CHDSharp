using BenchmarkDotNet.Attributes;
using CHDSharp.Encoder;

namespace CHDSharpBenchmark.Benchmarks;

/// <summary>
///     Encode throughput per codec: a deterministic 64 MiB synthetic image (roughly 50% random,
///     50% compressible patterns — zlib lands around 2.5:1, so every codec gets real work) is
///     encoded with each codec in turn through <see cref="ChdEncoder" /> into a temp CHD.
///     1 vs 8 workers shows the parallel pipeline win. Bytes processed per operation = 64 MiB;
///     MB/s = value ÷ Mean; Allocated reports the per-op managed peak. CD codecs (cdzl/cdlz/cdzs/
///     cdfl) run on CD-sized hunks (8 frames); the flac path additionally benefits from
///     audio-like content, so its synthetic data pair alternates 16-bit samples. Two extras
///     beyond the single-codec grid: <see cref="Encode_MultiChain" /> exercises the 4-slot
///     fallback chain chdman uses by default (lzma,zlib,huff,flac), and
///     <see cref="Encode_Avhu" /> encodes a synthetic laserdisc AVI (YUY2 video + PCM audio)
///     through the A/V Huffman path (<c>EncodeLaserDisc</c>).
/// </summary>
[Config(typeof(BenchConfig))]
public class EncodeBenchmarks
{
    private const int RawHunkBytes = 4096;
    private const uint RawUnitBytes = 512;
    private const int CdHunkBytes = CdConstants.FramesPerHunk * CdConstants.FrameSize;
    private const uint CdUnitBytes = CdConstants.FrameSize;
    private const int ImageBytes = 64 * 1024 * 1024; // 64 MiB

    /// <summary>Dictionary key for the multi-codec fallback chain (not a real codec tag).</summary>
    private const uint ChainKey = 0xCDCDCDCD;

    /// <summary>The 4-slot codec chain chdman defaults to for hard disks (v5_multi corpus file).</summary>
    private static readonly uint[] MultiChain =
        [CodecTags.Lzma, CodecTags.Zlib, CodecTags.Huff, CodecTags.Flac];

    private readonly Dictionary<uint, byte[]> _images = new();
    private readonly Dictionary<uint, string> _tempDirs = new();
    private byte[] _chainImage = [];
    private string _aviPath = "";

    [ParamsSource(nameof(WorkerCounts))] public int TaskCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _images[CodecTags.Zlib] = BuildRawImage(ImageBytes, 0xC0DEC1B, 0.40);
        _images[CodecTags.Zstd] = BuildRawImage(ImageBytes, 0xC0DEC2B, 0.40);
        _images[CodecTags.Lzma] = BuildRawImage(ImageBytes, 0xC0DEC3B, 0.40);
        // huff crushes repetitive data; feed it the most compressible pattern mix.
        _images[CodecTags.Huff] = BuildRawImage(ImageBytes, 0xC0DEC4B, 0.15);
        // flac drives 16-bit little-endian sample pairs; audio-like content compresses best.
        _images[CodecTags.Flac] = BuildAudioImage(ImageBytes, 0xC0DEC5B);
        _images[CodecTags.None] = BuildRawImage(ImageBytes, 0xC0DEC6B, 0.40);

        // CD codecs encode CD-sized hunks (8 frames of 2448): a frame-major image with
        // mode-1-ish sector headers so the ECC path is exercised.
        _images[CodecTags.Cdzl] = BuildCdImage(ImageBytes, 0xC0DEC7B);
        _images[CodecTags.Cdlz] = BuildCdImage(ImageBytes, 0xC0DEC8B);
        _images[CodecTags.Cdzs] = BuildCdImage(ImageBytes, 0xC0DEC9B);
        _images[CodecTags.Cdfl] = BuildCdAudioImage(ImageBytes, 0xC0DECAB);

        _chainImage = BuildRawImage(ImageBytes, 0xC0DECDB, 0.40);

        // Laserdisc source: 320x240 YUY2 @30 fps, 48 frames (one hunk per frame), PCM audio.
        // 44100 Hz divides evenly by 30 fps (1470 samples per frame).
        _aviPath = Path.Combine(
            Path.GetTempPath(),
            $"chdbench_avhu_{Guid.NewGuid():N}",
            "bench.avi"
        );
        Directory.CreateDirectory(Path.GetDirectoryName(_aviPath)!);
        SyntheticAvi.Write(_aviPath, 320, 240, 30, 48, 44100);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        foreach (var dir in _tempDirs.Values)
        {
            try
            {
                Directory.Delete(dir, true);
            }
            catch (IOException)
            {
            }
        }

        if (File.Exists(_aviPath))
        {
            try
            {
                Directory.Delete(Path.GetDirectoryName(_aviPath)!, true);
            }
            catch (IOException)
            {
            }
        }
    }

    public static IEnumerable<int> WorkerCounts()
    {
        return [1, 8];
    }

    private string RunEncode(uint codec, bool parallel)
    {
        return RunEncode([codec], parallel);
    }

    private string RunEncode(IReadOnlyList<uint> codecs, bool parallel)
    {
        var key = codecs.Count > 1 ? ChainKey : codecs[0];
        // ReSharper disable once UnusedVariable
        if (codecs.Count == 1 && !_images.TryGetValue(codecs[0], out var image))
        {
            throw new InvalidOperationException(
                $"Benchmark image for codec {CodecTags.ToString(codecs[0])} was not created (setup failed)"
            );
        }

        var outDir = GetTempDir(key);
        var outPath = Path.Combine(outDir, "bench.chd");
        var buffer = codecs.Count > 1 ? _chainImage : _images[codecs[0]];
        using var src = new MemoryStream(buffer, false);

        var options = new ChdEncodeOptions { TaskCount = parallel ? TaskCount : 1 };
        var isCd = codecs[0] is CodecTags.Cdzl or CodecTags.Cdlz or CodecTags.Cdzs or CodecTags.Cdfl;
        var hunkBytes = isCd ? CdHunkBytes : (uint)RawHunkBytes;
        var unitBytes = isCd ? CdUnitBytes : RawUnitBytes;

        ChdEncoder.EncodeRaw(src, outPath, hunkBytes, unitBytes, codecs, options);
        return outPath;
    }

    [Benchmark]
    public string Encode_Zlib()
    {
        return RunEncode(CodecTags.Zlib, TaskCount > 1);
    }

    [Benchmark]
    public string Encode_Zstd()
    {
        return RunEncode(CodecTags.Zstd, TaskCount > 1);
    }

    [Benchmark]
    public string Encode_Lzma()
    {
        return RunEncode(CodecTags.Lzma, TaskCount > 1);
    }

    [Benchmark]
    public string Encode_Huff()
    {
        return RunEncode(CodecTags.Huff, TaskCount > 1);
    }

    [Benchmark]
    public string Encode_Flac()
    {
        return RunEncode(CodecTags.Flac, TaskCount > 1);
    }

    [Benchmark]
    public string Encode_None()
    {
        return RunEncode(CodecTags.None, TaskCount > 1);
    }

    [Benchmark]
    public string Encode_Cdzl()
    {
        return RunEncode(CodecTags.Cdzl, TaskCount > 1);
    }

    [Benchmark]
    public string Encode_Cdlz()
    {
        return RunEncode(CodecTags.Cdlz, TaskCount > 1);
    }

    [Benchmark]
    public string Encode_Cdzs()
    {
        return RunEncode(CodecTags.Cdzs, TaskCount > 1);
    }

    [Benchmark]
    public string Encode_Cdfl()
    {
        return RunEncode(CodecTags.Cdfl, TaskCount > 1);
    }

    /// <summary>4-slot fallback chain (lzma,zlib,huff,flac) — chdman's default for hard disks.</summary>
    [Benchmark]
    public string Encode_MultiChain()
    {
        return RunEncode(MultiChain, TaskCount > 1);
    }

    /// <summary>Laserdisc A/V Huffman path: encodes the synthetic AVI via EncodeLaserDisc.</summary>
    [Benchmark]
    public string Encode_Avhu()
    {
        var outDir = GetTempDir(CodecTags.Avhu);
        var outPath = Path.Combine(outDir, "bench.chd");
        var options = new ChdEncodeOptions { TaskCount = TaskCount };
        ChdEncoder.EncodeLaserDisc(_aviPath, outPath, options: options);
        return outPath;
    }

    private string GetTempDir(uint codec)
    {
        if (!_tempDirs.TryGetValue(codec, out var dir) || !Directory.Exists(dir))
        {
            var name = codec switch
            {
                ChainKey => "multi",
                CodecTags.None => "none",
                _ => CodecTags.ToString(codec)
            };

            dir = Path.Combine(Path.GetTempPath(), $"chdbench_{name}_{Guid.NewGuid():N}");
            Directory.CreateDirectory(dir);
            _tempDirs[codec] = dir;
        }

        return dir;
    }

    private static byte[] BuildRawImage(int sizeBytes, int seed, double randomRatio)
    {
        var rng = new Random(seed);
        var data = new byte[sizeBytes];
        rng.NextBytes(data);
        var runStart = (int)(sizeBytes * (1.0 - randomRatio));
        for (var i = runStart; i < sizeBytes; i++)
        {
            // Compressible runs: repeating word + zeros.
            data[i] = (byte)((i & 0x3FF) == 0 ? 0 : (i / 96) & 0xFF);
        }

        return data;
    }

    private static byte[] BuildAudioImage(int sizeBytes, int seed)
    {
        var rng = new Random(seed);
        var data = new byte[sizeBytes];
        var sample = 0;
        for (var i = 0; i + 1 < sizeBytes; i += 2)
        {
            // A few sine-ish tones + white noise; the CHD FLAC codec source code is
            // sample-pair oriented (LE). Fill first half, repeat-ish second.
            sample = (sample + (rng.Next(32000) & 15)) & 0xFFFF;
            data[i] = (byte)sample;
            data[i + 1] = (byte)(sample >> 8);
        }

        return data;
    }

    // Mode-1-ish CD frame: 12-byte sync + 4-byte header + 2048 data + 288 ECC-ish/zeros,
    // repeated with per-frame variance so the ECC-clear path has interesting input.
    private static byte[] BuildCdImage(int sizeBytes, int seed)
    {
        var rng = new Random(seed);
        var data = new byte[sizeBytes];
        const int frameSize = CdConstants.FrameSize; // 2448
        for (var frame = 0; frame * frameSize < sizeBytes; frame++)
        {
            var off = frame * frameSize;
            var len = Math.Min(frameSize, sizeBytes - off);
            for (var i = 0; i < len; i++)
                data[off + i] = (byte)rng.Next(256);
        }

        return data;
    }

    private static byte[] BuildCdAudioImage(int sizeBytes, int seed)
    {
        var rng = new Random(seed);
        var data = new byte[sizeBytes];
        const int frameSize = CdConstants.FrameSize;
        for (var frame = 0; frame * frameSize < sizeBytes; frame++)
        {
            var off = frame * frameSize;
            var len = Math.Min(frameSize, sizeBytes - off);
            var sample = 0;
            for (var i = 0; i < len; i += 2)
            {
                sample = (sample + rng.Next(64000)) & 0xFFFF;
                data[off + i] = (byte)sample;
                if (i + 1 < len)
                    data[off + i + 1] = (byte)(sample >> 8);
            }
        }

        return data;
    }
}