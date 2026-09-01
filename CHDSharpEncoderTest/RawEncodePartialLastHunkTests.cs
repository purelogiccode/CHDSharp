using System.Security.Cryptography;
using CHDSharp;
using CHDSharp.Encoder;

namespace CHDSharpEncoderTest;

/// <summary>
///     Regression tests for raw encodes whose last hunk is partial (logical size is a
///     multiple of the unit size but not of the hunk size) with more than 256 hunks.
///     This input shape previously triggered the stale work-buffer shim's eager stale-hunk
///     read, which fast-forwarded <see cref="CHDSharp.Encoder" />'s ring-buffered raw reader
///     before the pipeline started: every hunk was then served from a stale window near EOF,
///     collapsed into SELF references, and produced a tiny, self-consistent CHD full of
///     garbage that both verifiers accepted (battle-corpus DVDs were all exact hunk
///     multiples, so the path was never exercised — see FailingParity.md).
/// </summary>
public class RawEncodePartialLastHunkTests : IDisposable
{
    private readonly string _testDataDir;

    public RawEncodePartialLastHunkTests()
    {
        _testDataDir = Path.Combine(
            Path.GetTempPath(),
            "chd_encoder_partial_hunk_tests_" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(_testDataDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_testDataDir, true);
        }
        catch
        {
            // ignored
        }
    }

    /// <summary>32 MiB + 2 KiB: 8,193 hunks (partial last hunk: 2,048 of 4,096 bytes), ring wraps 32x.</summary>
    private const long ProbeBytes = 32L * 1024 * 1024 + 2048;

    [Fact]
    public void PartialLastHunk_Zstd_Encode_MatchesChdman_ByteForByte()
    {
        if (ChdmanHelper.ChdmanPath == null)
            return;

        var srcPath = Path.Combine(_testDataDir, "src.bin");
        WriteProbeImage(srcPath);

        var ourChd = Path.Combine(_testDataDir, "ours.chd");
        ChdEncoder.EncodeRaw(
            srcPath,
            ourChd,
            4096,
            2048,
            [CodecTags.Zstd],
            new ChdEncodeOptions { Metadata = [MetadataWriter.BuildDvdMetadata()] }
        );

        var chdmanChd = Path.Combine(_testDataDir, "chdman.chd");
        var (exitCode, stdout, stderr) = ChdmanHelper.RunChdman(
            "createdvd",
            "-i",
            srcPath,
            "-o",
            chdmanChd,
            "-c",
            "zstd",
            "-f"
        );
        Assert.True(exitCode == 0, $"chdman createdvd failed: {exitCode}\n{stdout}\n{stderr}");

        var ours = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(ourChd)));
        var theirs = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(chdmanChd)));
        Assert.True(
            string.Equals(ours, theirs, StringComparison.OrdinalIgnoreCase),
            $"products not byte-identical: ours={ours[..12]} chdman={theirs[..12]}"
        );
    }

    [Fact]
    public void PartialLastHunk_DefaultCodecs_RoundTrips_ExactBytes()
    {
        var srcPath = Path.Combine(_testDataDir, "src.bin");
        WriteProbeImage(srcPath);
        var source = File.ReadAllBytes(srcPath);

        var chdPath = Path.Combine(_testDataDir, "roundtrip.chd");
        ChdEncoder.EncodeRaw(
            srcPath,
            chdPath,
            4096,
            2048,
            [CodecTags.Lzma, CodecTags.Zlib, CodecTags.Huff, CodecTags.Flac],
            new ChdEncodeOptions { Metadata = [MetadataWriter.BuildDvdMetadata()] }
        );

        var err = ChdFile.OpenAsStream(chdPath, out var stream);
        Assert.Equal(ChdError.Chderrnone, err);
        Assert.NotNull(stream);
        using (stream)
        {
            Assert.Equal(ProbeBytes, stream.Length);
            var decoded = new byte[ProbeBytes];
            var read = 0;
            while (read < decoded.Length)
            {
                var n = stream.Read(decoded, read, decoded.Length - read);
                Assert.True(n > 0, $"stream ended at {read}");
                read += n;
            }

            Assert.True(
                decoded.AsSpan().SequenceEqual(source),
                "decoded CHD content differs from the source image"
            );
        }
    }

    /// <summary>
    ///     Deterministic probe image: alternating pseudo-random and structured (compressible)
    ///     hunks, no 256-hunk periodicity, so SELF-dedup cannot mask a mis-served hunk.
    /// </summary>
    internal static void WriteProbeImage(string path)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        var buf = new byte[1 << 20];
        ulong state = 0x243F6A8885A308D3;
        long remaining = ProbeBytes;
        while (remaining > 0)
        {
            var n = (int)Math.Min(buf.Length, remaining);
            for (var i = 0; i < n; i += 4096)
            {
                var len = Math.Min(4096, n - i);
                if ((i / 4096) % 2 == 0)
                {
                    for (var j = 0; j < len; j++)
                    {
                        state ^= state << 13;
                        state ^= state >> 7;
                        state ^= state << 17;
                        buf[i + j] = (byte)(state >> 56);
                    }
                }
                else
                {
                    for (var j = 0; j < len; j++)
                        buf[i + j] = (byte)(j * 7 + (i / 4096));
                }
            }

            fs.Write(buf, 0, n);
            remaining -= n;
        }
    }
}
