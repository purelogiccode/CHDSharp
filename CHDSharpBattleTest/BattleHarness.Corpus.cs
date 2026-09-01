using System.Diagnostics;
using System.Globalization;

namespace CHDSharpBattleTest;

/// <summary>
///     Timed real-corpus battles (merged from the former CHDBattleTest harness): for every
///     standalone corpus CHD, runs extractraw, the structured extract, a copy and a create
///     battle with chdman.exe vs CHDSharp.exe, SHA-256 parity on all products, and cross
///     verification. Results land in results.csv (per row) and battles.md (summary).
/// </summary>
internal sealed partial class BattleHarness
{
    private readonly List<BattleRow> _battleRows = [];
    private HashSet<string> _corpusCompleted = new(StringComparer.OrdinalIgnoreCase);

    // results.csv lives NEXT to the timestamped battle-* dirs so --out runs accumulate
    // history and --resume can skip files already processed by earlier runs
    private string CorpusCsvPath => Path.Combine(Path.GetDirectoryName(OutDir)!, "results.csv");

    private void RunCorpusBattles(ChdmanRunner chdman, CliRunner cli, string file)
    {
        var name = Path.GetFileName(file);
        var insp = CorpusClassifier.Inspect(file);
        var rows = new List<BattleRow>();

        if (!insp.IsChd || insp.Error is not null)
        {
            Console.WriteLine($"      battles: skipped ({insp.Error ?? "not a chd"})");
            rows.Add(SkipRow(name, insp, insp.Error ?? "not a chd"));
            CommitRows(rows);
            return;
        }

        if (_corpus.Resume && _corpusCompleted.Contains(file))
        {
            Console.WriteLine("      battles: skipped (resume: already in results.csv)");
            return;
        }

        if (!EnoughSpaceFor(OutDir, (long)insp.LogicalBytes * 3))
        {
            Console.WriteLine("      battles: skipped (insufficient free disk space)");
            rows.Add(SkipRow(name, insp, "insufficient free disk space"));
            CommitRows(rows);
            return;
        }

        Console.WriteLine(
            $"      battles: chd={new FileInfo(file).Length / 1048576.0:F1} MiB logical={insp.LogicalBytes / 1048576.0:F1} MiB kind={insp.Kind} V{insp.Version}");

        var work = Path.Combine(_workDir, "corpus", Sanitize(name) + "_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(work);
        try
        {
            if (_corpus.LibDecode && insp.Kind != MediaKind.LaserDisc)
                LibDecodeBattle(file, insp, work, rows);

            ExtractRawBattle(chdman, cli, file, insp, work, rows);
            StructuredExtractBattle(chdman, cli, file, insp, work, rows);
            CopyBattle(chdman, cli, file, insp, work, rows);
            CreateBattle(chdman, cli, file, insp, work, rows);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"      battles: error: {ex.Message}");
            rows.Add(SkipRow(name, insp, "battle error: " + ex.Message));
        }
        finally
        {
            if (!_corpus.KeepTemp)
                try
                {
                    Directory.Delete(work, true);
                }
                catch
                {
                    // ignore
                }

            CommitRows(rows);
        }
    }

    private void CommitRows(List<BattleRow> rows)
    {
        if (rows.Count == 0)
            return;

        BattleReporter.AppendCsv(CorpusCsvPath, rows);
        _battleRows.AddRange(rows);
        foreach (var r in rows)
            if (string.Equals(r.Tool, "cross", StringComparison.OrdinalIgnoreCase))
                Console.WriteLine(
                    $"      battle {r.Battle}: {(r.Ok ? "MATCH " + ShortHash(r.Hash) : "PARITY FAILURE" + (r.Error is null ? "" : " - " + r.Error))}");
    }

    // ----- decode battles -----

    private static void LibDecodeBattle(string file, CorpusInfo insp, string work, List<BattleRow> rows)
    {
        var outPath = Path.Combine(work, "lib_raw.bin");
        var sw = Stopwatch.StartNew();
        try
        {
            var bytes = Hashing.LibDecodeTo(file, outPath);
            sw.Stop();
            rows.Add(Row(file, insp, "decode-lib", "chdsharp-lib", true, sw.Elapsed.TotalSeconds,
                bytes, null, 0, insp.LogicalBytes, null, null));
        }
        catch (Exception ex)
        {
            sw.Stop();
            rows.Add(Row(file, insp, "decode-lib", "chdsharp-lib", false, sw.Elapsed.TotalSeconds,
                0, null, -1, insp.LogicalBytes, null, ex.Message));
        }
        finally
        {
            TryDeleteFile(outPath);
        }
    }

    private void ExtractRawBattle(
        ChdmanRunner chdman, CliRunner cli, string file, CorpusInfo insp, string work, List<BattleRow> rows)
    {
        const string battle = "extractraw";
        var mDir = Path.Combine(work, "m_raw");
        var sDir = Path.Combine(work, "s_raw");
        Directory.CreateDirectory(mDir);
        Directory.CreateDirectory(sDir);
        var mRaw = Path.Combine(mDir, "raw.bin");
        var sRaw = Path.Combine(sDir, "raw.bin");

        var (rm, rmIdx) = RunBattleTool("chdman", (c, a) => chdman.Run(c, a), battle, file, insp, rows,
            "extractraw", "-i", file, "-o", mRaw, "-f");
        var (rs, rsIdx) = RunBattleTool("chdsharp", (c, a) => cli.Run(c, a), battle, file, insp, rows,
            "extractraw", "-i", file, "-o", sRaw, "-f");

        string? mHash = null, sHash = null;
        if (rm is { ExitCode: 0 })
        {
            mHash = Hashing.Sha256File(mRaw).Hash;
            rows[rmIdx] = Row(file, insp, battle, "chdman", true, rm.Seconds, FileLen(mRaw), mHash, 0,
                insp.LogicalBytes, null, null);
        }

        if (rs is { ExitCode: 0 })
        {
            sHash = Hashing.Sha256File(sRaw).Hash;
            rows[rsIdx] = Row(file, insp, battle, "chdsharp", true, rs.Seconds, FileLen(sRaw), sHash, 0,
                insp.LogicalBytes, null, null);
        }

        AddParityRow(rows, file, insp, battle + "-parity", mHash, sHash,
            rm is { ExitCode: 0 } && rs is { ExitCode: 0 } && FileLen(mRaw) != FileLen(sRaw)
                ? $"output format differs (chdman={FileLen(mRaw)} B vs chdsharp={FileLen(sRaw)} B)"
                : "decoded outputs differ");

        // keep exactly one decoded raw artifact for the createraw battle: prefer chdman's
        if (!_corpus.KeepTemp)
        {
            if (rm is { ExitCode: 0 })
                TryDeleteFile(sRaw);
            else
                TryDeleteFile(mRaw);
        }
    }

    private void StructuredExtractBattle(
        ChdmanRunner chdman, CliRunner cli, string file, CorpusInfo insp, string work, List<BattleRow> rows)
    {
        if (!_corpus.IncludeAv && insp.Kind == MediaKind.LaserDisc)
        {
            Console.WriteLine("      structured extract: laserdisc skipped (enable --include-av)");
            return;
        }

        var (cmd, outName) = insp.Kind switch
        {
            MediaKind.Cd => ("extractcd", "disc.cue"),
            MediaKind.GdRom => ("extractcd", "disc.gdi"),
            MediaKind.Dvd => ("extractdvd", "disc.iso"),
            MediaKind.Hdd => ("extracthd", "disc.img"),
            MediaKind.LaserDisc => ("extractld", "disc.avi"),
            _ => ("", "")
        };

        if (cmd.Length == 0)
        {
            Console.WriteLine($"      structured extract: skipped (kind={insp.Kind})");
            return;
        }

        var mDir = Path.Combine(work, "m_struct");
        var sDir = Path.Combine(work, "s_struct");
        Directory.CreateDirectory(mDir);
        Directory.CreateDirectory(sDir);
        var mOut = Path.Combine(mDir, outName);
        var sOut = Path.Combine(sDir, outName);

        var (rm, rmIdx) = RunBattleTool("chdman", (c, a) => chdman.Run(c, a), cmd, file, insp, rows,
            cmd, "-i", file, "-o", mOut, "-f");
        var (rs, rsIdx) = RunBattleTool("chdsharp", (c, a) => cli.Run(c, a), cmd, file, insp, rows,
            cmd, "-i", file, "-o", sOut, "-f");

        long mBytes = 0, sBytes = 0;
        string? mHash = null, sHash = null;
        if (rm is { ExitCode: 0 })
            (mHash, mBytes) = Hashing.Sha256Directory(mDir);
        if (rs is { ExitCode: 0 })
            (sHash, sBytes) = Hashing.Sha256Directory(sDir);

        var denom = (ulong)Math.Max(mBytes, sBytes);
        if (rm is { ExitCode: 0 })
            rows[rmIdx] = Row(file, insp, cmd, "chdman", true, rm.Seconds, mBytes, mHash, 0, denom, null, null);
        if (rs is { ExitCode: 0 })
            rows[rsIdx] = Row(file, insp, cmd, "chdsharp", true, rs.Seconds, sBytes, sHash, 0, denom, null, null);

        AddParityRow(rows, file, insp, cmd + "-parity", mHash, sHash,
            rm is { ExitCode: 0 } && rs is { ExitCode: 0 } && mBytes != sBytes
                ? $"output convention differs (chdman={mBytes} B vs chdsharp={sBytes} B total)"
                : "structured extraction outputs differ");

        // keep exactly one decoded artifact set for the create battle: prefer chdman's
        if (!_corpus.KeepTemp)
        {
            if (rm is { ExitCode: 0 })
                TryDeleteDir(sDir);
            else
                TryDeleteDir(mDir);
        }
    }

    // ----- encode battles -----

    private void CopyBattle(
        ChdmanRunner chdman, CliRunner cli, string file, CorpusInfo insp, string work, List<BattleRow> rows)
    {
        var codec = _corpus.CodecFor(insp.Kind);
        var battle = "copy:" + codec;

        var mChd = Path.Combine(work, "copy_m.chd");
        var sChd = Path.Combine(work, "copy_s.chd");

        var (rm, rmIdx) = RunBattleTool("chdman", (c, a) => chdman.Run(c, a), battle, file, insp, rows, "copy",
            "-i", file, "-o", mChd, "-c", codec, "-f", "-np", _corpus.Workers.ToString(CultureInfo.InvariantCulture));
        var (rs, rsIdx) = RunBattleTool("chdsharp", (c, a) => cli.Run(c, a), battle, file, insp, rows, "copy",
            "-i", file, "-o", sChd, "-c", codec, "-f", "-np", _corpus.Workers.ToString(CultureInfo.InvariantCulture));

        FillProductRows(rows, file, insp, battle, rm, rs, rmIdx, rsIdx, mChd, sChd);

        if (rm is { ExitCode: 0 })
            CrossVerify(rows, chdman, cli, file, insp, battle, mChd, true);
        if (rs is { ExitCode: 0 })
            CrossVerify(rows, chdman, cli, file, insp, battle, sChd, false);

        if (!_corpus.KeepTemp)
        {
            TryDeleteFile(mChd);
            TryDeleteFile(sChd);
        }
    }

    private void CreateBattle(
        ChdmanRunner chdman, CliRunner cli, string file, CorpusInfo insp, string work, List<BattleRow> rows)
    {
        var (cmd, extension) = insp.Kind switch
        {
            MediaKind.Cd => ("createcd", ".cue"),
            MediaKind.GdRom => ("createcd", ".gdi"),
            MediaKind.Dvd => ("createdvd", ".iso"),
            MediaKind.Hdd => ("createhd", ".img"),
            MediaKind.LaserDisc when _corpus.IncludeAv => ("createld", ".avi"),
            MediaKind.LaserDisc => ("", ""),
            _ => ("createraw", "")
        };

        if (!_corpus.IncludeAv && insp.Kind == MediaKind.LaserDisc)
        {
            Console.WriteLine("      create battle: laserdisc skipped (enable --include-av)");
            return;
        }

        string? input;
        if (insp.Kind is MediaKind.Cd or MediaKind.GdRom or MediaKind.Dvd or MediaKind.Hdd or MediaKind.LaserDisc)
        {
            input = FindStructuredArtifact(work, extension);
            if (input is null)
            {
                Console.WriteLine($"      create battle: SKIPPED - no decoded artifact available for {cmd}");
                return;
            }
        }
        else
        {
            input = FindRawArtifact(work);
            if (input is null)
            {
                Console.WriteLine("      create battle: SKIPPED - raw decode artifact missing for createraw");
                return;
            }
        }

        var codec = _corpus.CodecFor(insp.Kind);
        var battle = cmd + ":" + codec;
        var mChd = Path.Combine(work, "create_m.chd");
        var sChd = Path.Combine(work, "create_s.chd");

        // createraw requires an explicit unit size (both tools reject a bare create);
        // preserve the source CHD's geometry so the re-encode matches the original hunks
        var geometry = string.Equals(cmd, "createraw"
            , StringComparison.OrdinalIgnoreCase)
            ? new[] { "-hs", insp.HunkBytes.ToString(), "-us", insp.UnitBytes.ToString() }
            : Array.Empty<string>();

        var (rm, rmIdx) = RunBattleTool("chdman", (c, a) => chdman.Run(c, a), battle, file, insp, rows, cmd,
            new[] { "-i", input, "-o", mChd, "-c", codec }.Concat(geometry)
                .Concat(new[] { "-f", "-np", _corpus.Workers.ToString(CultureInfo.InvariantCulture) }).ToArray());
        var (rs, rsIdx) = RunBattleTool("chdsharp", (c, a) => cli.Run(c, a), battle, file, insp, rows, cmd,
            new[] { "-i", input, "-o", sChd, "-c", codec }.Concat(geometry)
                .Concat(new[] { "-f", "-np", _corpus.Workers.ToString(CultureInfo.InvariantCulture) }).ToArray());

        FillProductRows(rows, file, insp, battle, rm, rs, rmIdx, rsIdx, mChd, sChd);

        if (rm is { ExitCode: 0 })
            CrossVerify(rows, chdman, cli, file, insp, battle, mChd, true);
        if (rs is { ExitCode: 0 })
            CrossVerify(rows, chdman, cli, file, insp, battle, sChd, false);

        if (!_corpus.KeepTemp)
        {
            TryDeleteFile(mChd);
            TryDeleteFile(sChd);
        }
    }

    /// <summary>
    ///     Fills the pending chdman/chdsharp product rows (hash, out bytes, ratio) for an
    ///     encode battle and appends the byte-parity cross row. Failed runs keep the error
    ///     row that <see cref="RunBattleTool" /> wrote.
    /// </summary>
    private static void FillProductRows(
        List<BattleRow> rows, string file, CorpusInfo insp, string battle,
        RunResult? rm, RunResult? rs, int rmIdx, int rsIdx, string mChd, string sChd)
    {
        string? mh = null, sh = null;
        long mBytes = 0, sBytes = 0;
        if (rm is { ExitCode: 0 })
        {
            (mh, mBytes) = Hashing.Sha256File(mChd);
            rows[rmIdx] = Row(file, insp, battle, "chdman", true, rm.Seconds, mBytes, mh, 0,
                insp.LogicalBytes,
                insp.LogicalBytes > 0 ? (double)mBytes / insp.LogicalBytes : null, null);
        }

        if (rs is { ExitCode: 0 })
        {
            (sh, sBytes) = Hashing.Sha256File(sChd);
            rows[rsIdx] = Row(file, insp, battle, "chdsharp", true, rs.Seconds, sBytes, sh, 0,
                insp.LogicalBytes,
                insp.LogicalBytes > 0 ? (double)sBytes / insp.LogicalBytes : null, null);
        }

        var parity = mh is not null && sh is not null &&
                     string.Equals(mh, sh, StringComparison.OrdinalIgnoreCase);
        var sizeMatch = rm is { ExitCode: 0 } && rs is { ExitCode: 0 } && mBytes == sBytes;
        rows.Add(Row(file, insp, battle + "-parity", "cross", parity, 0, 0, parity ? mh : null, 0, 0, null,
            parity ? null : sizeMatch ? "same size, different bytes" : "products differ"));
    }

    /// <summary>Verifies one battle product with the OPPOSITE tool (the producer already deep-verifies its own output).</summary>
    private static void CrossVerify(
        List<BattleRow> rows, ChdmanRunner chdman, CliRunner cli, string file, CorpusInfo insp,
        string battle, string product, bool verifyWithCli)
    {
        try
        {
            if (verifyWithCli)
            {
                var r = cli.Run("verify", "-i", product);
                rows.Add(Row(file, insp, $"{battle}:verify", "chdsharp", r.ExitCode == 0, r.Seconds, 0, null,
                    r.ExitCode, 0, null, r.ExitCode == 0 ? null : r.Combined.Trim()));
            }
            else
            {
                var r = chdman.Run("verify", "-i", product);
                rows.Add(Row(file, insp, $"{battle}:verify", "chdman", r.ExitCode == 0, r.Seconds, 0, null,
                    r.ExitCode, 0, null, r.ExitCode == 0 ? null : r.Combined.Trim()));
            }
        }
        catch (Exception ex)
        {
            rows.Add(Row(file, insp, $"{battle}:verify", verifyWithCli ? "chdsharp" : "chdman", false, 0, 0,
                null, -1, 0, null, ex.Message));
        }
    }

    // ----- helpers -----

    /// <summary>
    ///     Runs one tool invocation as a battle step: appends a pending row, replaces it with
    ///     the outcome (or an error row), and returns the result plus the row index.
    /// </summary>
    private static (RunResult? Result, int Index) RunBattleTool(
        string toolName, Func<string, string[], RunResult> run, string battle, string file, CorpusInfo insp,
        List<BattleRow> rows, string command, params string[] args)
    {
        var idx = rows.Count;
        rows.Add(Row(file, insp, battle, toolName, false, 0, 0, null, -9, 0, null, null));
        try
        {
            var r = run(command, args);
            if (r.ExitCode != 0)
                rows[idx] = Row(file, insp, battle, toolName, false, r.Seconds, 0, null, r.ExitCode, 0, null,
                    ToolError(r));

            return (r, idx);
        }
        catch (Exception ex)
        {
            rows[idx] = Row(file, insp, battle, toolName, false, 0, 0, null, -9, 0, null, ex.Message);
            return (null, idx);
        }
    }

    private static string ToolError(RunResult r)
    {
        var tail = r.Combined.Trim();
        var last = tail.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault();
        return
            $"exit {r.ExitCode}: {(string.IsNullOrEmpty(last) ? "(no output)" : last[..Math.Min(300, last.Length)])}";
    }

    private static BattleRow Row(
        string file, CorpusInfo insp, string battle, string tool, bool ok, double seconds,
        long outBytes, string? hash, int exit, ulong mibsDenominator, double? ratio, string? error)
    {
        double? mibs =
            seconds > 0.001 && mibsDenominator > 0 ? mibsDenominator / 1048576.0 / seconds : null;
        return new BattleRow(file, insp.Kind.ToString(), insp.Version, FileLen(file),
            insp.LogicalBytes, battle, tool, ok, seconds, outBytes, hash, exit, mibs, ratio, error);
    }

    private static BattleRow SkipRow(string file, CorpusInfo insp, string reason)
    {
        return new BattleRow(file, insp.Kind.ToString(), insp.Version, 0, insp.LogicalBytes,
            "(skipped)", "-", false, 0, 0, null, 0, null, null, reason);
    }

    private static void AddParityRow(
        List<BattleRow> rows, string file, CorpusInfo insp, string battle, string? mHash, string? sHash,
        string mismatchReason)
    {
        var parity = mHash is not null && sHash is not null &&
                     string.Equals(mHash, sHash, StringComparison.OrdinalIgnoreCase);
        rows.Add(Row(file, insp, battle, "cross", parity, 0, 0, parity ? mHash : null, 0, 0, null,
            parity ? null : mismatchReason));
    }

    private static long FileLen(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch
        {
            return 0;
        }
    }

    private static string ShortHash(string? hash)
    {
        return string.IsNullOrEmpty(hash) ? "-" : hash[..Math.Min(12, hash.Length)];
    }

    private static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name.Length > 80 ? name[..80] : name;
    }

    private static bool EnoughSpaceFor(string driveRoot, long neededBytes)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(driveRoot)) ?? driveRoot;
            var di = new DriveInfo(root);
            return di.AvailableFreeSpace > neededBytes * 2 + 1_000_000_000L;
        }
        catch
        {
            return true;
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // ignore
        }
    }

    private static void TryDeleteDir(string path)
    {
        try
        {
            Directory.Delete(path, true);
        }
        catch
        {
            // ignore
        }
    }

    private static string? FindStructuredArtifact(string work, string extension)
    {
        foreach (var sub in new[] { "m_struct", "s_struct" })
        {
            var dir = Path.Combine(work, sub);
            if (!Directory.Exists(dir)) continue;
            var hit = Directory.EnumerateFiles(dir, "*" + extension, SearchOption.AllDirectories).FirstOrDefault();
            if (hit is not null) return hit;
        }

        return null;
    }

    private static string? FindRawArtifact(string work)
    {
        foreach (var sub in new[] { "m_raw", "s_raw" })
        {
            var candidate = Path.Combine(work, sub, "raw.bin");
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }
}