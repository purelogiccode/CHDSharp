using CHDSharpEncoder;

namespace CHDSharpEncoderTest;

/// <summary>
/// Validates the Phase-1 codecs ('huff', 'flac', 'cdzl', 'cdlz', 'cdzs') against
/// chdman.exe: files must pass chdman verify, report the right codec in chdman info,
/// and extract byte-identically.
/// </summary>
public class NewCodecChdmanValidationTests : IDisposable
{
    private readonly string _testDataDir;

    public NewCodecChdmanValidationTests()
    {
        _testDataDir = Path.Combine(Path.GetTempPath(), "new_codec_chdman_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDataDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_testDataDir, recursive: true);
        }
        catch
        {
            // ignored
        }
    }

    [Theory]
    [InlineData("huff", "Huffman")]
    [InlineData("flac", "FLAC")]
    public void RawCodec_PassesChdmanVerify_AndExtractsByteIdentically(string codecName, string chdmanCodecName)
    {
        if (ChdmanHelper.ChdmanPath == null) return;

        // 16-bit stereo sample data: FLAC-compressible, and huff handles the raw bytes
        var source = new byte[4096 * 32];
        var rng = new Random(1234);
        for (var i = 0; i < source.Length; i += 4)
        {
            source[i] = (byte)rng.Next(0, 0x8000); // left sample (LE)
            source[i + 1] = (byte)(rng.Next(0, 0x8000) >> 8);
            source[i + 2] = (byte)(i / 4 % 0x7FFF); // right ramp
            source[i + 3] = (byte)((i / 4 % 0x7FFF) >> 8);
        }

        var srcPath = Path.Combine(_testDataDir, $"{codecName}_src.bin");
        var chdPath = Path.Combine(_testDataDir, $"{codecName}.chd");
        File.WriteAllBytes(srcPath, source);
        ChdEncoder.EncodeRaw(srcPath, chdPath, 4096, 512, [CodecTags.FromName(codecName)]);

        var (infoExit, infoOut, infoErr) = ChdmanHelper.RunChdman("info", "-i", chdPath);
        var info = infoOut + infoErr;
        Assert.True(infoExit == 0, $"chdman info failed (exit={infoExit})\n{info}");
        Assert.Contains(chdmanCodecName, info, StringComparison.Ordinal);

        var (verifyExit, vOut, vErr) = ChdmanHelper.RunChdman("verify", "-i", chdPath);
        Assert.True(verifyExit == 0, $"chdman verify failed (exit={verifyExit})\n{vOut}{vErr}");

        var extractPath = Path.Combine(_testDataDir, $"{codecName}_extracted.raw");
        var (extractExit, eOut, eErr) = ChdmanHelper.RunChdman("extractraw", "-i", chdPath, "-o", extractPath, "-f");
        Assert.True(extractExit == 0, $"extractraw failed (exit={extractExit})\n{eOut}{eErr}");

        Assert.Equal(source, File.ReadAllBytes(extractPath));
    }

    [Theory]
    [InlineData("cdzl", "CD Deflate")]
    [InlineData("cdlz", "CD LZMA")]
    [InlineData("cdzs", "CD Zstandard")]
    public void CdCodec_PassesChdmanVerify_AndExtractsByteIdentically(string codecName, string chdmanCodecName)
    {
        if (ChdmanHelper.ChdmanPath == null) return;

        const string cue = """
                           FILE "game.bin" BINARY
                             TRACK 01 MODE1/2352
                               INDEX 01 00:00:00
                             TRACK 02 AUDIO
                               INDEX 00 00:00:20
                               INDEX 01 00:00:22
                           """;
        var cuePath = Path.Combine(_testDataDir, "test.cue");
        File.WriteAllText(cuePath, cue);

        var bin = new byte[40 * CdConstants.MaxSectorData];
        for (var f = 0; f < 20; f++)
        {
            var offset = f * CdConstants.MaxSectorData;
            for (var i = 0; i < CdConstants.MaxSectorData; i++)
            {
                bin[offset + i] = (byte)(i & 0xFF);
            }
        }

        for (var f = 20; f < 40; f++)
        {
            var offset = f * CdConstants.MaxSectorData;
            for (var s = 0; s < 588; s++)
            {
                var sample = (int)(Math.Sin(s * 0.05) * 12000);
                bin[offset + s * 4] = (byte)sample;
                bin[offset + s * 4 + 1] = (byte)(sample >> 8);
                bin[offset + s * 4 + 2] = (byte)sample;
                bin[offset + s * 4 + 3] = (byte)(sample >> 8);
            }
        }

        File.WriteAllBytes(Path.Combine(_testDataDir, "game.bin"), bin);

        var chdPath = Path.Combine(_testDataDir, $"{codecName}.chd");
        ChdEncoder.EncodeCd(cuePath, chdPath, hunkBytes: CdConstants.FramesPerHunk * CdConstants.FrameSize,
            unitBytes: CdConstants.FrameSize, codecTags: [CodecTags.FromName(codecName)]);

        var (infoExit, infoOut, infoErr) = ChdmanHelper.RunChdman("info", "-i", chdPath);
        var info = infoOut + infoErr;
        Assert.True(infoExit == 0, $"chdman info failed (exit={infoExit})\n{info}");
        Assert.Contains(chdmanCodecName, info, StringComparison.Ordinal);

        var (verifyExit, vOut, vErr) = ChdmanHelper.RunChdman("verify", "-i", chdPath);
        Assert.True(verifyExit == 0, $"chdman verify failed (exit={verifyExit})\n{vOut}{vErr}");

        var extractPath = Path.Combine(_testDataDir, $"{codecName}_extracted.raw");
        var (extractExit, eOut, eErr) = ChdmanHelper.RunChdman("extractraw", "-i", chdPath, "-o", extractPath, "-f");
        Assert.True(extractExit == 0, $"extractraw failed (exit={extractExit})\n{eOut}{eErr}");

        // expected logical image: 20 data frames + 20 audio frames (byte-swapped) + zero padding
        var expected = new byte[40 * CdConstants.FrameSize];
        PlaceBinFrames(expected, 0, bin, 20, 0, swap: false);
        PlaceBinFrames(expected, 20, bin, 20, 20 * CdConstants.MaxSectorData, swap: true);

        Assert.Equal(expected, File.ReadAllBytes(extractPath));
    }

    private static void PlaceBinFrames(byte[] image, int chdFrameStart, byte[] bin, int binFrameCount, int binOffset, bool swap)
    {
        for (var f = 0; f < binFrameCount; f++)
        {
            var dest = (chdFrameStart + f) * CdConstants.FrameSize;
            Array.Copy(bin, binOffset + f * CdConstants.MaxSectorData, image, dest, CdConstants.MaxSectorData);
            if (swap)
            {
                for (var i = 0; i < CdConstants.MaxSectorData; i += 2)
                {
                    (image[dest + i], image[dest + i + 1]) = (image[dest + i + 1], image[dest + i]);
                }
            }
        }
    }
}