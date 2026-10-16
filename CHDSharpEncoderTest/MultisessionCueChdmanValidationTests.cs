using System.Security.Cryptography;
using CHDSharp.Encoder;

namespace CHDSharpEncoderTest;

/// <summary>
///     Validates the multisession cue handling (REM SESSION / LEAD-IN / LEAD-OUT / session
///     pregap) against chdman: the built-in encoder must produce the same bytes as
///     <c>chdman createcd</c> for both the single-bin (IsoBuster/ImgBurn) and multi-bin
///     (Redump/DiscImageCreator) session layouts.
/// </summary>
public class MultisessionCueChdmanValidationTests : IDisposable
{
    private readonly string _testDataDir;

    public MultisessionCueChdmanValidationTests()
    {
        _testDataDir = Path.Combine(
            Path.GetTempPath(),
            "multisession_cue_tests_" + Guid.NewGuid().ToString("N")
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
    public void SingleBinMultisession_MatchesChdmanBytes()
    {
        // 100 frames of session 1 + 50 frames of inter-session dummy data + 400 frames of
        // session 2 in one bin. The declared lead-out (00:01:25 = 100 frames) trims track 1 and
        // turns the 50-frame gap into zero-filled pad frames (IsoBuster/ImgBurn layout).
        const string cue = """
                           REM SESSION 01
                           FILE "disc.bin" BINARY
                             TRACK 01 MODE2/2352
                               INDEX 01 00:00:00
                           REM LEAD-OUT 00:01:25
                           REM SESSION 02
                             TRACK 02 MODE2/2352
                               INDEX 01 00:02:00
                           """;
        var cuePath = Path.Combine(_testDataDir, "multisession.cue");
        File.WriteAllText(cuePath, cue);
        File.WriteAllBytes(Path.Combine(_testDataDir, "disc.bin"), PatternBytes(2352 * 550));

        AssertMatchesChdman(cuePath, "single_bin");
    }

    [Fact]
    public void SingleBinMultisessionImg_MatchesChdmanBytes()
    {
        // .img images (DiscImageCreator) have no dummy data: the lead-out trims track 1, but
        // track 2 keeps reading the gap, so the stored layout differs from the .bin case.
        const string cue = """
                           REM SESSION 01
                           FILE "disc.img" BINARY
                             TRACK 01 MODE2/2352
                               INDEX 01 00:00:00
                           REM LEAD-OUT 00:01:25
                           REM SESSION 02
                             TRACK 02 MODE2/2352
                               INDEX 01 00:02:00
                           """;
        var cuePath = Path.Combine(_testDataDir, "multisession_img.cue");
        File.WriteAllText(cuePath, cue);
        File.WriteAllBytes(Path.Combine(_testDataDir, "disc.img"), PatternBytes(2352 * 550));

        AssertMatchesChdman(cuePath, "single_bin_img");
    }

    [Fact]
    public void MultiBinMultisession_MatchesChdmanBytes()
    {
        // sessions in separate files with a session pregap: chdman synthesizes the lead-out and
        // uses the pregap as the session's lead-in; the stored layout is unchanged.
        const string cue = """
                           REM SESSION 01
                           FILE "t1.bin" BINARY
                             TRACK 01 MODE1/2352
                               INDEX 01 00:00:00
                           REM SESSION 02
                           REM PREGAP 00:01:00
                           FILE "t2.bin" BINARY
                             TRACK 02 MODE1/2352
                               INDEX 01 00:00:00
                           """;
        var cuePath = Path.Combine(_testDataDir, "multibin.cue");
        File.WriteAllText(cuePath, cue);
        File.WriteAllBytes(Path.Combine(_testDataDir, "t1.bin"), PatternBytes(2352 * 300));
        File.WriteAllBytes(Path.Combine(_testDataDir, "t2.bin"), PatternBytes(2352 * 300));

        AssertMatchesChdman(cuePath, "multi_bin");
    }

    private void AssertMatchesChdman(string cuePath, string tag)
    {
        if (ChdmanHelper.ChdmanPath == null)
            return;

        var chdmanChd = Path.Combine(_testDataDir, $"{tag}_chdman.chd");
        var (exitCode, stdout, stderr) = ChdmanHelper.RunChdman(
            "createcd",
            "-i",
            cuePath,
            "-o",
            chdmanChd,
            "-c",
            "zlib",
            "-f"
        );
        Assert.True(exitCode == 0, $"chdman createcd failed: {exitCode}\n{stdout}\n{stderr}");

        var ourChd = Path.Combine(_testDataDir, $"{tag}_ours.chd");
        ChdEncoder.EncodeCd(cuePath, ourChd, codecTags: [CodecTags.Zlib]);

        var ours = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(ourChd)));
        var theirs = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(chdmanChd)));
        Assert.True(
            string.Equals(ours, theirs, StringComparison.OrdinalIgnoreCase),
            $"{tag}: products not byte-identical: ours={ours[..12]} chdman={theirs[..12]}"
        );
    }

    private static byte[] PatternBytes(int length)
    {
        var bytes = new byte[length];
        for (var i = 0; i < bytes.Length; i++)
            bytes[i] = (byte)(i % 251);
        return bytes;
    }
}
