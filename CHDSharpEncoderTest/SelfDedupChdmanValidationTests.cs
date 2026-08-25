using CHDSharp.Encoder;

namespace CHDSharpEncoderTest;

/// <summary>
///     Validates SELF-hunk deduplication output against chdman.exe: deduplicated CHDs must
///     pass chdman verify, extract byte-identically, and report repeat blocks in chdman info.
/// </summary>
public class SelfDedupChdmanValidationTests : IDisposable
{
    private readonly string _testDataDir;

    public SelfDedupChdmanValidationTests()
    {
        // unique per test class instance: the test host runs per-TFM in parallel
        _testDataDir = Path.Combine(
            Path.GetTempPath(),
            "self_dedup_chdman_tests_" + Guid.NewGuid().ToString("N")
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

    [Fact]
    public void RepeatedHunks_PassChdmanVerify_AndExtract()
    {
        if (ChdmanHelper.ChdmanPath == null)
            return;

        // 1 MiB made of 256 identical 4 KiB hunks
        var source = new byte[4096 * 256];
        for (var i = 0; i < 4096; i++)
            source[i] = (byte)(i & 0xFF);

        for (var h = 1; h < 256; h++)
            Array.Copy(source, 0, source, h * 4096, 4096);

        var srcPath = Path.Combine(_testDataDir, "repeated.bin");
        var chdPath = Path.Combine(_testDataDir, "repeated.chd");
        var extractPath = Path.Combine(_testDataDir, "repeated.raw");
        File.WriteAllBytes(srcPath, source);

        ChdEncoder.EncodeRaw(srcPath, chdPath);

        // dedup proof: 255 of 256 hunks are SELF references, so the CHD is tiny
        Assert.True(
            new FileInfo(chdPath).Length < 4096 * 4,
            $"expected a deduplicated CHD, got {new FileInfo(chdPath).Length} bytes"
        );

        var (verifyExit, vOut, vErr) = ChdmanHelper.RunChdman("verify", "-i", chdPath);
        Assert.True(verifyExit == 0, $"chdman verify failed (exit={verifyExit})\n{vOut}{vErr}");

        var (extractExit, eOut, eErr) = ChdmanHelper.RunChdman(
            "extractraw",
            "-i",
            chdPath,
            "-o",
            extractPath,
            "-f"
        );
        Assert.True(extractExit == 0, $"extractraw failed (exit={extractExit})\n{eOut}{eErr}");

        Assert.Equal(source, File.ReadAllBytes(extractPath));
    }

    [Fact]
    public void RepeatedHunks_MatchChdmanExtraction()
    {
        if (ChdmanHelper.ChdmanPath == null)
            return;

        var patternA = new byte[4096];
        var patternB = new byte[4096];
        for (var i = 0; i < 4096; i++)
        {
            patternA[i] = (byte)(i & 0xFF);
            patternB[i] = (byte)(~i & 0xFF);
        }

        var source = new byte[4096 * 128];
        for (var h = 0; h < 128; h++)
            Array.Copy(h % 2 == 0 ? patternA : patternB, 0, source, h * 4096, 4096);

        var srcPath = Path.Combine(_testDataDir, "alternating.bin");
        var ourChd = Path.Combine(_testDataDir, "our.chd");
        var chdmanChd = Path.Combine(_testDataDir, "chdman.chd");
        File.WriteAllBytes(srcPath, source);

        ChdEncoder.EncodeRaw(srcPath, ourChd);

        var (createExit, cOut, cErr) = ChdmanHelper.RunChdman(
            "createraw",
            "-i",
            srcPath,
            "-o",
            chdmanChd,
            "-c",
            "zlib",
            "-hs",
            "4096",
            "-us",
            "512",
            "-f"
        );
        Assert.True(createExit == 0, $"chdman createraw failed (exit={createExit})\n{cOut}{cErr}");

        // strongest check: byte-for-byte identical CHD files (dedup + map encoding parity)
        Assert.Equal(File.ReadAllBytes(chdmanChd), File.ReadAllBytes(ourChd));

        var ourExtract = Path.Combine(_testDataDir, "our.raw");
        var chdmanExtract = Path.Combine(_testDataDir, "chdman.raw");
        var (e1, o1, e1R) = ChdmanHelper.RunChdman(
            "extractraw",
            "-i",
            ourChd,
            "-o",
            ourExtract,
            "-f"
        );
        Assert.True(e1 == 0, $"extractraw our failed (exit={e1})\n{o1}{e1R}");
        var (e2, o2, e2R) = ChdmanHelper.RunChdman(
            "extractraw",
            "-i",
            chdmanChd,
            "-o",
            chdmanExtract,
            "-f"
        );
        Assert.True(e2 == 0, $"extractraw chdman failed (exit={e2})\n{o2}{e2R}");

        Assert.Equal(File.ReadAllBytes(chdmanExtract), File.ReadAllBytes(ourExtract));
        Assert.Equal(source, File.ReadAllBytes(ourExtract));
    }
}