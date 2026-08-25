using CHDSharp.Encoder;

namespace CHDSharpEncoderTest;

public class ChdmanValidationTests : IDisposable
{
    private readonly string _testDataDir;

    public ChdmanValidationTests()
    {
        // unique per test class instance: the test host runs per-TFM in parallel
        _testDataDir = Path.Combine(
            Path.GetTempPath(),
            "chd_encoder_chdman_tests_" + Guid.NewGuid().ToString("N")
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
    public void Chdman_Info_ReportsCorrectly()
    {
        if (ChdmanHelper.ChdmanPath == null)
            return;

        var source = CreateTestFile(8192, 42);
        var srcPath = Path.Combine(_testDataDir, "info_src.bin");
        var chdPath = Path.Combine(_testDataDir, "info.chd");
        File.WriteAllBytes(srcPath, source);

        ChdEncoder.EncodeRaw(srcPath, chdPath);

        var (exitCode, stdout, stderr) = ChdmanHelper.RunChdman("info", "-i", chdPath);
        Assert.True(
            exitCode == 0,
            $"chdman info exit code: {exitCode}\nstdout: {stdout}\nstderr: {stderr}"
        );

        var output = stdout + stderr;
        Assert.Contains("File Version: 5", output, StringComparison.Ordinal);
        Assert.Contains("zlib", output, StringComparison.Ordinal);
        Assert.DoesNotContain("Error", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Chdman_Verify_Passes()
    {
        if (ChdmanHelper.ChdmanPath == null)
            return;

        var source = CreateTestFile(65536, 123);
        var srcPath = Path.Combine(_testDataDir, "verify_src.bin");
        var chdPath = Path.Combine(_testDataDir, "verify.chd");
        File.WriteAllBytes(srcPath, source);

        ChdEncoder.EncodeRaw(srcPath, chdPath);

        var (verifyExit, vstdout, vstderr) = ChdmanHelper.RunChdman("verify", "-i", chdPath);
        Assert.True(
            verifyExit == 0,
            $"verify failed (exit={verifyExit})\nstdout: {vstdout}\nstderr: {vstderr}"
        );
    }

    [Fact]
    public void Chdman_Extract_ProducesIdenticalData()
    {
        if (ChdmanHelper.ChdmanPath == null)
            return;

        var source = CreateTestFile(65536, 456);
        var srcPath = Path.Combine(_testDataDir, "extract_src.bin");
        var chdPath = Path.Combine(_testDataDir, "extract.chd");
        var extractedPath = Path.Combine(_testDataDir, "extracted.raw");
        File.WriteAllBytes(srcPath, source);

        ChdEncoder.EncodeRaw(srcPath, chdPath);

        var (exitCode, estdout, estderr) = ChdmanHelper.RunChdman(
            "extractraw",
            "-i",
            chdPath,
            "-o",
            extractedPath,
            "-f"
        );
        Assert.True(
            exitCode == 0,
            $"extractraw failed (exit={exitCode})\nstdout: {estdout}\nstderr: {estderr}"
        );

        var extracted = File.ReadAllBytes(extractedPath);
        Assert.Equal(source, extracted);
    }

    [Fact]
    public void OurOutput_MatchesChdmanOutput()
    {
        if (ChdmanHelper.ChdmanPath == null)
            return;

        var source = CreateTestFile(65536, 789);
        var srcPath = Path.Combine(_testDataDir, "cross_src.bin");
        var ourChd = Path.Combine(_testDataDir, "cross_our.chd");
        var chdmanChd = Path.Combine(_testDataDir, "cross_chdman.chd");
        var ourExtract = Path.Combine(_testDataDir, "cross_our_extracted.raw");
        var chdmanExtract = Path.Combine(_testDataDir, "cross_chdman_extracted.raw");
        File.WriteAllBytes(srcPath, source);

        ChdEncoder.EncodeRaw(srcPath, ourChd);

        var (createExit, cstdout, cstderr) = ChdmanHelper.RunChdman(
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
        Assert.True(
            createExit == 0,
            $"chdman createraw failed (exit={createExit})\nstdout: {cstdout}\nstderr: {cstderr}"
        );

        var (ext1Exit, e1Stdout, e1Stderr) = ChdmanHelper.RunChdman(
            "extractraw",
            "-i",
            ourChd,
            "-o",
            ourExtract,
            "-f"
        );
        Assert.True(
            ext1Exit == 0,
            $"extractraw our failed (exit={ext1Exit})\nstdout: {e1Stdout}\nstderr: {e1Stderr}"
        );

        var (ext2Exit, e2Stdout, e2Stderr) = ChdmanHelper.RunChdman(
            "extractraw",
            "-i",
            chdmanChd,
            "-o",
            chdmanExtract,
            "-f"
        );
        Assert.True(
            ext2Exit == 0,
            $"extractraw chdman failed (exit={ext2Exit})\nstdout: {e2Stdout}\nstderr: {e2Stderr}"
        );

        var ourExtracted = File.ReadAllBytes(ourExtract);
        var chdmanExtracted = File.ReadAllBytes(chdmanExtract);

        Assert.Equal(source, ourExtracted);
        Assert.Equal(source, chdmanExtracted);
        Assert.Equal(ourExtracted, chdmanExtracted);

        var (verifyExit, vstdout, vstderr) = ChdmanHelper.RunChdman("verify", "-i", ourChd);
        Assert.True(
            verifyExit == 0,
            $"verify our failed (exit={verifyExit})\nstdout: {vstdout}\nstderr: {vstderr}"
        );
    }

    [Fact]
    public void NonAlignedSize_ChdmanExtractWorks()
    {
        if (ChdmanHelper.ChdmanPath == null)
            return;

        var source = CreateTestFile(10000, 42);
        var srcPath = Path.Combine(_testDataDir, "na_src.bin");
        var chdPath = Path.Combine(_testDataDir, "na.chd");
        var extractedPath = Path.Combine(_testDataDir, "na_extracted.raw");
        File.WriteAllBytes(srcPath, source);

        ChdEncoder.EncodeRaw(srcPath, chdPath);

        var (exitCode, nastdout, nastderr) = ChdmanHelper.RunChdman(
            "extractraw",
            "-i",
            chdPath,
            "-o",
            extractedPath,
            "-f"
        );
        Assert.True(
            exitCode == 0,
            $"extractraw failed (exit={exitCode})\nstdout: {nastdout}\nstderr: {nastderr}"
        );

        var extracted = File.ReadAllBytes(extractedPath);
        Assert.Equal(source, extracted);

        // non-aligned sizes must also pass chdman verify (SHA1 covers source bytes only,
        // not the zero-padded final hunk)
        var (verifyExit, vstdout, vstderr) = ChdmanHelper.RunChdman("verify", "-i", chdPath);
        Assert.True(
            verifyExit == 0,
            $"non-aligned verify failed (exit={verifyExit})\nstdout: {vstdout}\nstderr: {vstderr}"
        );
    }

    // ----- helpers -----

    private static byte[] CreateTestFile(int size, int seed)
    {
        var data = new byte[size];
        var rng = new Random(seed);
        rng.NextBytes(data);
        return data;
    }
}