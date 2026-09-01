namespace CHDSharpBattleTest;

/// <summary>
///     Synthetic regression probe: a deterministic raw DVD image whose size is a multiple of
///     the 2,048-byte sector size but NOT of the 4,096-byte hunk size (33,556,480 B = 8,193
///     hunks with a 2,048-byte partial last hunk; the work-buffer ring wraps ~32 times).
///     <para>
///         This is the input class that exposed the stale work-buffer ring corruption in
///         CHDSharp: the eager stale-hunk pre-read fast-forwarded the ring-buffered raw reader,
///         every hunk was served from a stale window near EOF, and the whole image collapsed
///         into SELF references — producing a tiny, self-consistent but garbage CHDs that both
///         verifiers accepted (the embedded hashes were computed over the same mis-read bytes).
///         The library-level partial-tail case is covered by the raw-encode "long-tail" input;
///         this probe covers the CLI <c>createdvd</c> path with a cross round-trip check that
///         makes self-consistent-but-wrong products impossible to miss.
///     </para>
/// </summary>
internal sealed partial class BattleHarness
{
    /// <summary>Probe image size: 2,048-aligned, not 4,096-aligned (partial last hunk).</summary>
    internal const long ProbeBytes = 32L * 1024 * 1024 + 2048;

    private void RunSyntheticProbeSuite()
    {
        if (_cli == null)
            return;

        const string suite = "probe partial-tail dvd";
        var dir = Path.Combine(_workDir, "probe");
        Directory.CreateDirectory(dir);
        var iso = Path.Combine(dir, "probe.iso");
        WriteProbeImage(iso);
        var srcHash = Hashing.Sha256File(iso).Hash;

        try
        {
            Check(
                suite,
                "createdvd partial last hunk byte-identical (CLI vs chdman)",
                () =>
                {
                    var cliChd = Path.Combine(dir, "probe.cli.chd");
                    var refChd = Path.Combine(dir, "probe.ref.chd");
                    var cr = _cli.Run("createdvd", "-i", iso, "-o", cliChd, "-c", _corpus.CodecRaw, "-f");
                    AssertCliSuccess(cr, "CLI createdvd probe");
                    var mr = _chdman.Run("createdvd", "-i", iso, "-o", refChd, "-c", _corpus.CodecRaw, "-f");
                    Assert(mr.ExitCode == 0, $"chdman createdvd failed: {mr.Combined.Trim()}");
                    Assert(
                        string.Equals(
                            Hashing.Sha256File(refChd).Hash,
                            Hashing.Sha256File(cliChd).Hash,
                            StringComparison.OrdinalIgnoreCase),
                        "probe products are not byte-identical"
                    );
                }
            );

            Check(
                suite,
                "probe products cross-verify",
                () =>
                {
                    var cliChd = Path.Combine(dir, "probe.cli.chd");
                    var refChd = Path.Combine(dir, "probe.ref.chd");
                    var vr = _chdman.Run("verify", "-i", cliChd);
                    Assert(vr.ExitCode == 0, $"chdman verify of CLI probe CHD failed: {vr.Combined.Trim()}");
                    var vs = _cli.Run("verify", "-i", refChd);
                    Assert(vs.ExitCode == 0, $"CLI verify of chdman probe CHD failed: {vs.Combined.Trim()}");
                }
            );

            Check(
                suite,
                "probe cross round-trip == source",
                () =>
                {
                    // each product is decoded by the OTHER tool: a self-consistent but wrong
                    // CHD (the stale-ring signature) fails here even if both encoders agreed
                    var cliChd = Path.Combine(dir, "probe.cli.chd");
                    var refChd = Path.Combine(dir, "probe.ref.chd");

                    var rt1 = Path.Combine(dir, "rt_cli.bin");
                    var r1 = _chdman.Run("extractraw", "-i", cliChd, "-o", rt1, "-f");
                    Assert(r1.ExitCode == 0, $"chdman extractraw of CLI probe failed: {r1.Combined.Trim()}");
                    Assert(
                        string.Equals(Hashing.Sha256File(rt1).Hash, srcHash, StringComparison.OrdinalIgnoreCase),
                        "chdman-decoded CLI probe product differs from the source image"
                    );

                    var rt2 = Path.Combine(dir, "rt_ref.bin");
                    var r2 = _cli.Run("extractraw", "-i", refChd, "-o", rt2, "-f");
                    Assert(r2.ExitCode == 0, $"CLI extractraw of chdman probe failed: {r2.Combined.Trim()}");
                    Assert(
                        string.Equals(Hashing.Sha256File(rt2).Hash, srcHash, StringComparison.OrdinalIgnoreCase),
                        "CLI-decoded chdman probe product differs from the source image"
                    );
                }
            );
        }
        finally
        {
            if (!_corpus.KeepTemp)
                try
                {
                    Directory.Delete(dir, true);
                }
                catch
                {
                    // ignore
                }
        }
    }

    /// <summary>
    ///     Writes the deterministic probe image: alternating pseudo-random and structured
    ///     (compressible) hunks, no 256-hunk periodicity, so SELF-dedup cannot mask a
    ///     mis-served hunk.
    /// </summary>
    private static void WriteProbeImage(string path)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        var buf = new byte[1 << 20];
        ulong state = 0x243F6A8885A308D3;
        var remaining = ProbeBytes;
        while (remaining > 0)
        {
            var n = (int)Math.Min(buf.Length, remaining);
            for (var i = 0; i < n; i += 4096)
            {
                var len = Math.Min(4096, n - i);
                if (i / 4096 % 2 == 0)
                    for (var j = 0; j < len; j++)
                    {
                        state ^= state << 13;
                        state ^= state >> 7;
                        state ^= state << 17;
                        buf[i + j] = (byte)(state >> 56);
                    }
                else
                    for (var j = 0; j < len; j++)
                        buf[i + j] = (byte)(j * 7 + i / 4096);
            }

            fs.Write(buf, 0, n);
            remaining -= n;
        }
    }
}