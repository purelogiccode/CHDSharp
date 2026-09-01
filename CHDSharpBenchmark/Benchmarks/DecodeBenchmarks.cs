using BenchmarkDotNet.Attributes;
using CHDSharp;

namespace CHDSharpBenchmark.Benchmarks;

/// <summary>
///     Decode throughput per codec: one benchmark case per codec the library can read
///     (<see cref="CodecMap.All" />: zlib, zstd, lzma, huff, flac, cdzl, cdlz, cdzs, cdfl,
///     avhu and the uncompressed 'none' map). Each case opens the corpus CHD whose header
///     declares that codec — V5 single-codec files for the raw codecs, V5 CD files for the
///     cd* codecs, the V5 laserdisc file for avhu — and reads every hunk (the dominant
///     emulator workload). The file is opened once in setup with the default 1-hunk cache so
///     every iteration measures real decompression, not cache hits. Bytes processed per
///     operation = <c>HunkBytes × HunkCount</c>; MB/s = value ÷ Mean.
/// </summary>
[Config(typeof(BenchConfig))]
public class DecodeBenchmarks
{
    /// <summary>
    ///     Preferred corpus file per codec (the names the test-corpus generator produces);
    ///     guarantees a V5 single-codec file instead of whichever V3/V4/V5 file happens to
    ///     sort first in a header-only scan. Falls back to <see cref="Corpus.FindChdForCodec" />
    ///     when the preferred file is absent from a custom corpus.
    /// </summary>
    private static readonly Dictionary<string, string> PreferredFiles = new(StringComparer.Ordinal)
    {
        ["zlib"] = "v5_zlib.chd",
        ["zstd"] = "v5_zstd.chd",
        ["lzma"] = "v5_lzma.chd",
        ["huff"] = "v5_huff.chd",
        ["flac"] = "v5_flac.chd",
        ["cdzl"] = "v5_cd_cdzl.chd",
        ["cdlz"] = "v5_cd_cdlz.chd",
        ["cdzs"] = "v5_cd_cdzs.chd",
        ["cdfl"] = "v5_cd_cdfl.chd",
        ["avhu"] = "v5_ld_avhu.chd",
        ["none"] = "v5_none.chd"
    };

    private byte[] _buffer = [];
    private ChdFile? _chd;
    private ulong _imageBytes;

    [ParamsSource(nameof(CodecNames))]
    public string Codec { get; set; } = "zlib";

    public static IEnumerable<string> CodecNames()
    {
        return CodecMap.All.Select(c => c.Name);
    }

    [GlobalSetup]
    public void Setup()
    {
        var file = ResolveFile(Codec);
        var err = ChdFile.Open(file, out var chd);
        if (err != ChdError.Chderrnone || chd == null)
        {
            throw new InvalidOperationException(
                $"Could not open corpus file '{file}' for codec '{Codec}': {err}"
            );
        }

        _chd = chd;
        _imageBytes = chd.TotalBytes;
        _buffer = new byte[chd.HunkBytes];
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _chd?.Dispose();
        _chd = null;
    }

    /// <summary>Sequential whole-image hunk decode: every hunk once, in order.</summary>
    [Benchmark(Baseline = true)]
    public ulong ReadAllHunks()
    {
        var chd = _chd!;
        var bytes = 0UL;
        for (uint h = 0; h < chd.HunkCount; h++)
        {
            if (chd.ReadHunk(h, _buffer) != ChdError.Chderrnone)
                throw new InvalidOperationException($"ReadHunk failed for '{Codec}' at hunk {h}");

            bytes += chd.HunkBytes;
        }

        return bytes;
    }

    /// <summary>
    ///     Sequential full-image read through
    ///     <see cref="ChdFile.Read(ulong, byte[], int, int, System.Threading.CancellationToken)" />
    ///     (byte-granular, the path used by extraction/verification tools).
    /// </summary>
    [Benchmark]
    public ulong Read_Sequential_ByteGranular()
    {
        var chd = _chd!;
        var bytes = 0UL;
        ulong offset = 0;
        while (offset < _imageBytes)
        {
            var count = (int)Math.Min((ulong)_buffer.Length, _imageBytes - offset);
            if (chd.Read(offset, _buffer, 0, count) != ChdError.Chderrnone)
                throw new InvalidOperationException($"Read failed for '{Codec}' at {offset}");

            offset += (uint)count;
            bytes += (uint)count;
        }

        return bytes;
    }

    /// <summary>Resolves the corpus file for a codec name (preferred file, then header scan).</summary>
    private static string ResolveFile(string codecName)
    {
        var entry = CodecMap.All.First(c =>
            string.Equals(c.Name, codecName, StringComparison.Ordinal)
        );

        if (PreferredFiles.TryGetValue(codecName, out var preferred))
        {
            var path = Path.Combine(Corpus.Dir, preferred);
            if (File.Exists(path))
                return path;
        }

        return Corpus.FindChdForCodec(entry.Encode)
               ?? throw new InvalidOperationException(
                   $"No corpus CHD found for codec '{codecName}' in '{Corpus.Dir}'"
               );
    }
}
