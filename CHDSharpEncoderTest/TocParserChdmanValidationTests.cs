using System.Security.Cryptography;
using CHDSharp.Encoder;

namespace CHDSharpEncoderTest;

/// <summary>
///     Validates the cdrdao TOC parser against chdman: a `.toc` chdman can convert must encode
///     byte-identically through CHDSharp. Covers the bare <c>START</c> form cdrdao emits, which
///     MAME's parse_toc accepts as "no pregap" (msf_to_frames of an empty token is 0) — our
///     parser used to reject it while chdman converted the same file.
/// </summary>
public class TocParserChdmanValidationTests : IDisposable
{
    private readonly string _testDataDir;

    public TocParserChdmanValidationTests()
    {
        // unique per test class instance: the test host runs per-TFM in parallel
        _testDataDir = Path.Combine(
            Path.GetTempPath(),
            "toc_parser_chdman_tests_" + Guid.NewGuid().ToString("N")
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

    [Theory]
    [InlineData("START")]
    [InlineData("START 00:00:00")]
    public void DataTrackToc_MatchesChdmanBytes(string startLine)
    {
        if (ChdmanHelper.ChdmanPath == null)
            return;

        var tocPath = Path.Combine(_testDataDir, "disc.toc");
        var binPath = Path.Combine(_testDataDir, "t.bin");
        File.WriteAllText(
            tocPath,
            "CD_DA\r\n\r\nTRACK MODE2_RAW\r\nNO COPY\r\nDATAFILE \"t.bin\" 0 1000\r\n" + startLine + "\r\n"
        );
        using (var fs = File.Create(binPath))
            fs.SetLength(2352L * 1000);

        var chdmanChd = Path.Combine(_testDataDir, "chdman.chd");
        var (exitCode, stdout, stderr) = ChdmanHelper.RunChdman(
            "createcd",
            "-i",
            tocPath,
            "-o",
            chdmanChd,
            "-c",
            "zlib",
            "-f"
        );
        Assert.True(exitCode == 0, $"chdman createcd failed: {exitCode}\n{stdout}\n{stderr}");

        var ourChd = Path.Combine(_testDataDir, "ours.chd");
        ChdEncoder.EncodeCd(tocPath, ourChd, codecTags: [CodecTags.Zlib]);

        var ours = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(ourChd)));
        var theirs = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(chdmanChd)));
        Assert.True(
            string.Equals(ours, theirs, StringComparison.OrdinalIgnoreCase),
            $"products not byte-identical for '{startLine}': ours={ours[..12]} chdman={theirs[..12]}"
        );
    }
}
