using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using CHDSharp;
using CHDSharp.Encoder;
using CHDSharp.Utils;

namespace CHDSharpBattleTest;

/// <summary>Thrown by <see cref="BattleHarness.Assert" /> to record a failed check without unwinding the whole run.</summary>
internal sealed class CheckFailedException : Exception
{
    public CheckFailedException(string message)
        : base(message)
    {
    }

    public CheckFailedException()
    {
    }

    public CheckFailedException(string? message, Exception? innerException) : base(message, innerException)
    {
    }
}

/// <summary>Thrown to skip a check (e.g. chdman rejects a configuration).</summary>
internal sealed class CheckSkippedException : Exception
{
    public CheckSkippedException(string message)
        : base(message)
    {
    }

    public CheckSkippedException()
    {
    }

    public CheckSkippedException(string? message, Exception? innerException) : base(message, innerException)
    {
    }
}

/// <summary>
///     The battle harness: cross-checks CHDSharpLib (decode) and CHDSharp.Encoder (encode)
///     against chdman.exe. Every check is recorded, reported, and summed into an exit code.
/// </summary>
internal sealed partial class BattleHarness
{
    private static readonly string[] CdCodecMatrix =
    [
        "cdzl",
        "cdlz",
        "cdzs",
        "cdfl",
        "zlib",
        "none"
    ];

    private readonly List<Asset> _assets = [];
    private readonly ChdmanRunner _chdman;

    private readonly List<CheckResult> _checks = [];
    private readonly CliRunner? _cli;
    private readonly bool _quick;
    private readonly List<string> _realDirs;
    private readonly int _realTimeoutMs;
    private readonly int _seed;
    private readonly string _workDir;

    internal BattleHarness(
        string chdmanPath,
        string? cliPath,
        string? outDir,
        int seed,
        bool quick,
        List<string> realDirs,
        int realTimeoutMs = 900_000
    )
    {
        _chdman = new ChdmanRunner(chdmanPath);
        _cli = cliPath != null ? new CliRunner(cliPath) : null;
        _seed = seed;
        _quick = quick;
        _realDirs = realDirs;
        _realTimeoutMs = realTimeoutMs;
        outDir ??= FindRepoRoot();
        OutDir = Path.Combine(outDir, "battle", $"battle-{DateTime.Now:yyyyMMdd-HHmmss}");
        _workDir = Path.Combine(OutDir, "artifacts");
        Directory.CreateDirectory(_workDir);
    }

    internal string OutDir { get; }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "TestResults")))
            dir = dir.Parent;

        return dir?.FullName ?? AppContext.BaseDirectory;
    }

    // ----- reporting -----

    private void Check(string suite, string name, Action test)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            test();
            Add(suite, name, "ok", true, false, sw.Elapsed.TotalSeconds);
        }
        catch (CheckSkippedException e)
        {
            Add(suite, name, e.Message, false, true, sw.Elapsed.TotalSeconds);
        }
        catch (CheckFailedException e)
        {
            Add(suite, name, e.Message, false, false, sw.Elapsed.TotalSeconds);
        }
        catch (Exception e)
        {
            Add(suite, name, $"exception: {e.Message}", false, false, sw.Elapsed.TotalSeconds);
        }
    }

    private void Add(
        string suite,
        string name,
        string detail,
        bool passed,
        bool skipped,
        double seconds
    )
    {
        _checks.Add(new CheckResult(suite, name, detail, passed, skipped, seconds));
        var status =
            skipped ? "SKIP"
            : passed ? "PASS"
            : "FAIL";
        Console.WriteLine($"[{status}] {suite,-24} {name,-58} {detail}  ({seconds,6:N1}s)");
    }

    private static void Assert([DoesNotReturnIf(false)] bool condition, string message)
    {
        if (!condition)
            throw new CheckFailedException(message);
    }

    private static void AssertEqual(byte[] expected, byte[] actual, string what)
    {
        if (expected.Length != actual.Length)
            throw new CheckFailedException(
                $"{what}: length {actual.Length} != expected {expected.Length}"
            );

        for (var i = 0; i < expected.Length; i++)
            if (expected[i] != actual[i])
                throw new CheckFailedException(
                    $"{what}: first diff at byte {i} (0x{expected[i]:X2} != 0x{actual[i]:X2})"
                );
    }

    // ----- entry point -----

    internal int Run()
    {
        Console.WriteLine($"== CHDSharp battle test vs {_chdman.VersionBanner()}");
        Console.WriteLine($"== seed={_seed} quick={_quick} out={OutDir}");
        if (_cli != null)
            Console.WriteLine($"== CLI: {_cli.ExePath}");
        Console.WriteLine();

        RunRawEncodeSuite();
        RunCdEncodeSuite();
        RunDeltaSuite();
        RunCopySuite();
        RunDecodeSuite();
        RunInfoSuite();

        if (_cli != null)
            RunCliSuite();

        if (_realDirs.Count > 0)
            RunRealSuites();

        WriteReport();
        return _checks.Count(c => c is { Passed: false, Skipped: false });
    }

    private void WriteReport()
    {
        var path = Path.Combine(OutDir, "report.txt");
        var sb = new StringBuilder();
        sb.AppendLine($"CHDSharp battle test report  ({_chdman.VersionBanner()})");
        sb.AppendLine($"seed={_seed} quick={_quick}  {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();
        foreach (
            var c in _checks
                .OrderBy(c => c.Suite, StringComparer.Ordinal)
                .ThenBy(c => c.Name, StringComparer.Ordinal)
        )
        {
            var status =
                c.Skipped ? "SKIP"
                : c.Passed ? "PASS"
                : "FAIL";
            sb.AppendLine($"[{status}] {c.Suite} | {c.Name} | {c.Detail} | {c.Seconds:N1}s");
        }

        sb.AppendLine();
        sb.AppendLine(SummaryText());
        File.WriteAllText(path, sb.ToString());
        Console.WriteLine($"Report: {path}");
    }

    internal void PrintSummary()
    {
        Console.WriteLine();
        Console.WriteLine("== Summary ==");
        Console.WriteLine(SummaryText());
    }

    private string SummaryText()
    {
        var sb = new StringBuilder();
        foreach (
            var group in _checks
                .GroupBy(c => c.Suite, StringComparer.Ordinal)
                .OrderBy(g => g.Key, StringComparer.Ordinal)
        )
        {
            var pass = group.Count(c => c.Passed);
            var fail = group.Count(c => c is { Passed: false, Skipped: false });
            var skip = group.Count(c => c.Skipped);
            sb.AppendLine(
                $"{group.Key,-28} {pass,4} passed  {fail,4} failed  {skip,4} skipped"
            );
        }

        sb.AppendLine(new string('-', 56));
        if (_checks.Count == 0)
            sb.AppendLine("No checks recorded.");
        else
            sb.AppendLine(
                $"TOTAL {(string.IsNullOrEmpty(_checks[0].Suite) ? 0 : _checks.Count),4} checks: "
                + $"{_checks.Count(c => c.Passed)} passed, {_checks.Count(c => c is { Passed: false, Skipped: false })} failed, {_checks.Count(c => c.Skipped)} skipped"
            );

        return sb.ToString();
    }

    internal void Cleanup()
    {
        try
        {
            Directory.Delete(OutDir, true);
        }
        catch
        {
            // ignore
        }
    }

    // ----- raw encode suite -----

    private void RunRawEncodeSuite()
    {
        const int m = 1024 * 1024;
        var full = new[]
        {
            new RawConfig("zlib", 4096, 512),
            new RawConfig("zstd", 4096, 512),
            new RawConfig("lzma", 4096, 512),
            new RawConfig("huff", 4096, 512),
            new RawConfig("flac", 4096, 512),
            new RawConfig("zlib,zstd,lzma", 4096, 512),
            new RawConfig("none", 4096, 512),
            new RawConfig("zlib", 65536, 512),
            new RawConfig("zlib", 4096, 4096)
        };
        var core = new[]
        {
            new RawConfig("zlib", 4096, 512),
            new RawConfig("zstd", 4096, 512),
            new RawConfig("lzma", 4096, 512),
            new RawConfig("zlib,zstd,lzma", 4096, 512),
            new RawConfig("none", 4096, 512),
            new RawConfig("zlib", 65536, 512)
        };
        var aligned512 = new[]
        {
            new RawConfig("zlib", 4096, 512),
            new RawConfig("none", 4096, 512),
            new RawConfig("zlib", 65536, 512)
        };
        var oursOnly = new[]
        {
            new RawConfig("zlib", 4096, 512),
            new RawConfig("none", 4096, 512),
            new RawConfig("zlib", 4096, 4096)
        };

        List<(string Name, byte[] Data, RawConfig[] Configs)> inputs;
        if (_quick)
            inputs =
            [
                (
                    "zeros",
                    TestDataGenerator.Zeros(64 * 1024),
                    [new RawConfig("zlib", 4096, 512), new RawConfig("none", 4096, 512)]
                ),
                (
                    "random",
                    TestDataGenerator.Random(128 * 1024, _seed),
                    [
                        new RawConfig("zlib", 4096, 512),
                        new RawConfig("zstd", 4096, 512),
                        new RawConfig("zlib", 65536, 512)
                    ]
                ),
                (
                    "mixed",
                    TestDataGenerator.Mixed(256 * 1024, _seed),
                    [
                        new RawConfig("zlib", 4096, 512),
                        new RawConfig("zstd", 4096, 512),
                        new RawConfig("lzma", 4096, 512),
                        new RawConfig("none", 4096, 512)
                    ]
                ),
                (
                    "pcm16",
                    TestDataGenerator.Pcm16(64 * 1024, _seed),
                    [new RawConfig("flac", 4096, 512)]
                ),
                (
                    "tiny100",
                    TestDataGenerator.Random(100, _seed),
                    [new RawConfig("zlib", 4096, 512)]
                )
            ];
        else
            inputs =
            [
                ("zeros", TestDataGenerator.Zeros(512 * 1024), full),
                ("random", TestDataGenerator.Random(1 * m, _seed), full),
                ("pattern", TestDataGenerator.Pattern(1 * m, _seed), core),
                ("mixed", TestDataGenerator.Mixed(2 * m, _seed), full),
                ("repeated", TestDataGenerator.RepeatedHunks(32, 8, 4096, _seed), full),
                ("text", TestDataGenerator.Text(512 * 1024, _seed), full),
                ("pcm16", TestDataGenerator.Pcm16(512 * 1024, _seed), full),
                ("unaligned", TestDataGenerator.Random(1_000_448, _seed), aligned512),
                // > 256 hunks with a partial final hunk: exercises chdman's compressor
                // work-buffer stale-tail quirk that the aligned small corpora can't catch.
                // Must ALSO be >= 513 hunks (2,103,296 B = 514 hunks, 2,048-byte tail): the
                // stale-ring corruption that produced tiny self-consistent CHDs (fixed in
                // ChdEncoder.ApplyChdmanWorkBufferTail) only manifests from 513 hunks up —
                // at 257 hunks the eager stale-hunk pre-read prefilled the ring in order and
                // every hunk was still served correctly, so the old 1_050_112-byte case
                // passed against the buggy build. See FailingParity.md in the app repo.
                (
                    "long-tail",
                    TestDataGenerator.Random(2_103_296, _seed),
                    [
                        new RawConfig("zlib", 4096, 512),
                        new RawConfig("zstd", 4096, 512),
                        new RawConfig("flac", 4096, 512)
                    ]
                ),
                ("tiny1", TestDataGenerator.Random(1, _seed), oursOnly),
                ("tiny100", TestDataGenerator.Random(100, _seed), oursOnly)
            ];

        foreach (var (name, data, configs) in inputs)
        foreach (var cfg in configs)
            RawEncodeCase(name, data, cfg);
    }

    private void RawEncodeCase(string inputName, byte[] data, RawConfig cfg)
    {
        var tag = $"{inputName} x {cfg.Label}";
        var suite = $"raw-encode {tag}";
        var dir = Path.Combine(_workDir, "raw", inputName);
        Directory.CreateDirectory(dir);

        var src = Path.Combine(dir, "src.bin");
        File.WriteAllBytes(src, data);

        var slug = cfg.Codecs.Replace(',', '-');
        // include the unit size so zlib(4096/512) and zlib(4096/4096) don't collide on
        // the same {slug}-{hunk} filename (the later case used to overwrite the earlier one)
        var ourChd = Path.Combine(dir, $"{slug}-{cfg.HunkBytes}-{cfg.UnitBytes}.ours.chd");
        var refChd = Path.Combine(dir, $"{slug}-{cfg.HunkBytes}-{cfg.UnitBytes}.ref.chd");

        Check(
            suite,
            "encode (ours)",
            () =>
            {
                ChdEncoder.EncodeRaw(
                    src,
                    ourChd,
                    cfg.HunkBytes,
                    cfg.UnitBytes,
                    ChdCodecs.ParseCodecTags(cfg.Codecs)
                );
                Assert(File.Exists(ourChd), "output file missing");
            }
        );

        var refCreated = false;
        if (data.Length % cfg.UnitBytes == 0)
            Check(
                suite,
                "chdman createraw",
                () =>
                {
                    var r = _chdman.Run(
                        "createraw",
                        "-i",
                        src,
                        "-o",
                        refChd,
                        "-c",
                        cfg.Codecs,
                        "-hs",
                        cfg.HunkBytes.ToString(),
                        "-us",
                        cfg.UnitBytes.ToString(),
                        "-f"
                    );
                    if (r.ExitCode != 0)
                        throw new CheckSkippedException(
                            $"chdman rejected config: {r.Combined.Trim()}"
                        );

                    refCreated = true;
                }
            );

        if (refCreated)
            Check(
                suite,
                "encode byte-identical",
                () =>
                {
                    var ours = File.ReadAllBytes(ourChd);
                    var refBytes = File.ReadAllBytes(refChd);
                    AssertEqual(refBytes, ours, "chd file bytes");
                }
            );

        Check(suite, "chdman verify (ours)", () => VerifyChdman(ourChd));
        if (refCreated)
            Check(suite, "chdman verify (ref)", () => VerifyChdman(refChd));

        Check(
            suite,
            "deep CheckFile (ours)",
            () =>
            {
                using var fs = File.OpenRead(ourChd);
                var result = Chd.CheckFile(fs, Path.GetFileName(ourChd), true);
                Assert(
                    result.IsSuccess,
                    $"CheckFile: {result.Error} ({result.Error.GetMessage()})"
                );
            }
        );

        Check(
            suite,
            "extract (ours)",
            () =>
            {
                var extracted = ExtractRaw(ourChd);
                AssertEqual(data, extracted, "extracted data");
            }
        );
        if (refCreated)
            Check(
                suite,
                "extract (ref)",
                () =>
                {
                    var extracted = ExtractRaw(refChd);
                    AssertEqual(data, extracted, "extracted data");
                }
            );

        Check(
            suite,
            "decode (ours)",
            () =>
            {
                var read = ReadAllBytes(ourChd);
                AssertEqual(data, read, "decoded data");
            }
        );
        if (refCreated)
            Check(
                suite,
                "decode (ref)",
                () =>
                {
                    var read = ReadAllBytes(refChd);
                    AssertEqual(data, read, "decoded data");
                }
            );

        if (refCreated)
            Check(suite, "info parity", () => InfoParity(ourChd, refChd));

        AddAsset(
            new Asset
            {
                Key = $"{inputName}|{cfg.Label}|ours",
                Name = $"{inputName} x {cfg.Label} (ours)",
                ChdPath = ourChd,
                Expected = data,
                IsCd = false,
                CodecLabel = cfg.Codecs
            }
        );
        if (refCreated)
            AddAsset(
                new Asset
                {
                    Key = $"{inputName}|{cfg.Label}|ref",
                    Name = $"{inputName} x {cfg.Label} (chdman)",
                    ChdPath = refChd,
                    Expected = data,
                    IsCd = false,
                    CodecLabel = cfg.Codecs
                }
            );
    }

    // ----- CD encode suite -----

    private void RunCdEncodeSuite()
    {
        var dir = Path.Combine(_workDir, "cd");
        Directory.CreateDirectory(dir);

        TestDataGenerator.CreateMixedCd(dir, _seed, out var mixedCue, out _);
        TestDataGenerator.CreateAudioOnlyCd(dir, _seed, out var audioCue, out _);
        TestDataGenerator.CreateIso(dir, _seed, out var isoPath);
        TestDataGenerator.CreateLongMixedCd(dir, _seed, out var longCue, out _);

        var configs = _quick
            ? new[]
            {
                new RawConfig("cdzl", 19584, 2448),
                new RawConfig("cdfl", 19584, 2448),
                new RawConfig("none", 19584, 2448)
            }
            : CdCodecMatrix
                .Select(c => new RawConfig(c, 19584, 2448))
                .Concat([new RawConfig("cdzl", 39168, 2448)])
                .ToArray();

        foreach (
            var (label, input) in new[]
            {
                ("cd-mixed", mixedCue),
                ("cd-audio", audioCue),
                ("disc-iso", isoPath),
                ("cd-long", longCue)
            }
        )
        foreach (var cfg in configs)
            CdEncodeCase(label, input, cfg);
    }

    private void CdEncodeCase(string inputName, string inputPath, RawConfig cfg)
    {
        var tag = $"{inputName} x {cfg.Label}";
        var suite = $"cd-encode {tag}";
        var dir = Path.Combine(_workDir, "cd", inputName);
        Directory.CreateDirectory(dir);

        var slug = cfg.Codecs.Replace(',', '-');
        // include the unit size (keeps CD filenames unambiguous across hunk sizes)
        var ourChd = Path.Combine(dir, $"{slug}-{cfg.HunkBytes}-{cfg.UnitBytes}.ours.chd");
        var refChd = Path.Combine(dir, $"{slug}-{cfg.HunkBytes}-{cfg.UnitBytes}.ref.chd");

        Check(
            suite,
            "encode (ours)",
            () =>
            {
                ChdEncoder.EncodeCd(
                    inputPath,
                    ourChd,
                    cfg.HunkBytes,
                    cfg.UnitBytes,
                    ChdCodecs.ParseCodecTags(cfg.Codecs)
                );
                Assert(File.Exists(ourChd), "output file missing");
            }
        );

        var refCreated = false;
        Check(
            suite,
            "chdman createcd",
            () =>
            {
                var r = _chdman.Run(
                    "createcd",
                    "-i",
                    inputPath,
                    "-o",
                    refChd,
                    "-c",
                    cfg.Codecs,
                    "-hs",
                    cfg.HunkBytes.ToString(),
                    "-f"
                );
                if (r.ExitCode != 0)
                    throw new CheckSkippedException($"chdman rejected config: {r.Combined.Trim()}");

                refCreated = true;
            }
        );

        if (refCreated)
            Check(
                suite,
                "encode byte-identical",
                () =>
                {
                    var ours = File.ReadAllBytes(ourChd);
                    var refBytes = File.ReadAllBytes(refChd);
                    AssertEqual(refBytes, ours, "chd file bytes");
                }
            );

        Check(suite, "chdman verify (ours)", () => VerifyChdman(ourChd));
        if (refCreated)
            Check(suite, "chdman verify (ref)", () => VerifyChdman(refChd));

        Check(
            suite,
            "deep CheckFile (ours)",
            () =>
            {
                using var fs = File.OpenRead(ourChd);
                var result = Chd.CheckFile(fs, Path.GetFileName(ourChd), true);
                Assert(
                    result.IsSuccess,
                    $"CheckFile: {result.Error} ({result.Error.GetMessage()})"
                );
            }
        );

        if (refCreated)
        {
            byte[]? refExtract = null;
            Check(
                suite,
                "extract parity (ours vs ref)",
                () =>
                {
                    refExtract = ExtractRaw(refChd);
                    var extracted = ExtractRaw(ourChd);
                    AssertEqual(refExtract, extracted, "extracted data");
                }
            );

            Check(
                suite,
                "decode (ours)",
                () =>
                {
                    if (refExtract == null)
                        return;

                    var read = ReadAllBytes(ourChd);
                    AssertEqual(refExtract, read, "decoded data");
                }
            );
            Check(
                suite,
                "decode (ref)",
                () =>
                {
                    if (refExtract == null)
                        return;

                    var read = ReadAllBytes(refChd);
                    AssertEqual(refExtract, read, "decoded data");
                }
            );

            Check(suite, "info parity", () => InfoParity(ourChd, refChd));

            AddAsset(
                new Asset
                {
                    Key = $"{inputName}|{cfg.Label}|ours",
                    Name = $"{inputName} x {cfg.Label} (ours)",
                    ChdPath = ourChd,
                    Expected = refExtract!,
                    IsCd = true,
                    CodecLabel = cfg.Codecs
                }
            );
            AddAsset(
                new Asset
                {
                    Key = $"{inputName}|{cfg.Label}|ref",
                    Name = $"{inputName} x {cfg.Label} (chdman)",
                    ChdPath = refChd,
                    Expected = refExtract!,
                    IsCd = true,
                    CodecLabel = cfg.Codecs
                }
            );
        }
        else
        {
            Check(
                suite,
                "extract/decode parity",
                () => throw new CheckSkippedException("no chdman reference available")
            );
        }
    }

    // ----- delta (parent/child) suite -----

    private void RunDeltaSuite()
    {
        const string suite = "delta";
        var dir = Path.Combine(_workDir, "delta");
        Directory.CreateDirectory(dir);

        var parentRef = _assets.FirstOrDefault(a =>
            string.Equals(a.Key, "mixed|zlib(4096/512)|ref", StringComparison.Ordinal)
        );
        var parentOurs = _assets.FirstOrDefault(a =>
            string.Equals(a.Key, "mixed|zlib(4096/512)|ours", StringComparison.Ordinal)
        );
        var mixedSrc = parentRef?.Expected ?? parentOurs?.Expected;
        if (parentRef == null || parentOurs == null || mixedSrc == null)
        {
            Console.WriteLine($"[SKIP] {suite} — 'mixed' assets missing, skipping delta suite");
            return;
        }

        var src = Path.Combine(_workDir, "raw", "mixed", "src.bin");
        if (!File.Exists(src))
            File.WriteAllBytes(src, mixedSrc);

        // the child must match the parent's stored hunk/unit sizes (both chdman and our
        // ParentMap reject a mismatch), so read them from the actual parent CHD rather than
        // hardcoding them
        uint hunkBytes = 4096,
            unitBytes = 512;
        var parentOpen = ChdFile.Open(parentRef.ChdPath, out var parentChd);
        if (parentOpen == ChdError.Chderrnone && parentChd != null)
            using (parentChd)
            {
                hunkBytes = parentChd.HunkBytes;
                unitBytes = parentChd.UnitBytes;
            }

        // --- child of chdman parent: byte-compare ours vs chdman ---
        var childOurs = Path.Combine(dir, "child-of-chdman.ours.chd");
        var childRef = Path.Combine(dir, "child-of-chdman.ref.chd");

        Check(
            suite,
            "child encode (ours, parent=chdman)",
            () =>
            {
                ChdEncoder.EncodeRaw(
                    src,
                    childOurs,
                    hunkBytes,
                    unitBytes,
                    [CodecTags.Zlib],
                    new ChdEncodeOptions { ParentPath = parentRef.ChdPath }
                );
                Assert(File.Exists(childOurs), "output file missing");
            }
        );

        var refCreated = false;
        Check(
            suite,
            "chdman createraw -op",
            () =>
            {
                var r = _chdman.Run(
                    "createraw",
                    "-i",
                    src,
                    "-o",
                    childRef,
                    "-c",
                    "zlib",
                    "-hs",
                    hunkBytes.ToString(),
                    "-us",
                    unitBytes.ToString(),
                    "-op",
                    parentRef.ChdPath,
                    "-f"
                );
                if (r.ExitCode != 0)
                    throw new CheckSkippedException($"chdman rejected config: {r.Combined.Trim()}");

                refCreated = true;
            }
        );

        if (refCreated)
            Check(
                suite,
                "child encode byte-identical",
                () =>
                {
                    var ours = File.ReadAllBytes(childOurs);
                    var refBytes = File.ReadAllBytes(childRef);
                    AssertEqual(refBytes, ours, "child chd file bytes");
                }
            );

        Check(
            suite,
            "chdman verify child (ours)",
            () => VerifyChdman(childOurs, parentRef.ChdPath)
        );
        Check(
            suite,
            "deep CheckFileWithParent",
            () =>
            {
                var result = Chd.CheckFileWithParent(childOurs, parentRef.ChdPath);
                Assert(
                    result.IsSuccess,
                    $"CheckFileWithParent: {result.Error} ({result.Error.GetMessage()})"
                );
            }
        );
        if (refCreated)
            Check(
                suite,
                "chdman verify child (ref)",
                () => VerifyChdman(childRef, parentRef.ChdPath)
            );

        Check(
            suite,
            "extract child (ours, -ip)",
            () =>
            {
                var extracted = ExtractRaw(childOurs, parentRef.ChdPath);
                AssertEqual(mixedSrc, extracted, "extracted data");
            }
        );
        if (refCreated)
            Check(
                suite,
                "extract child (ref, -ip)",
                () =>
                {
                    var extracted = ExtractRaw(childRef, parentRef.ChdPath);
                    AssertEqual(mixedSrc, extracted, "extracted data");
                }
            );

        Check(
            suite,
            "decode child (ours)",
            () =>
            {
                var read = ReadAllBytes(childOurs, parentRef.ChdPath);
                AssertEqual(mixedSrc, read, "decoded data");
            }
        );
        if (refCreated)
            Check(
                suite,
                "decode child (ref)",
                () =>
                {
                    var read = ReadAllBytes(childRef, parentRef.ChdPath);
                    AssertEqual(mixedSrc, read, "decoded data");
                }
            );

        // a parent with different content must be rejected
        var wrongParent =
            _assets.FirstOrDefault(a =>
                string.Equals(a.Key, "random|zlib(4096/512)|ref", StringComparison.Ordinal)
            )
            ?? _assets.FirstOrDefault(a =>
                string.Equals(a.Key, "pattern|zlib(4096/512)|ref", StringComparison.Ordinal)
            );
        if (wrongParent != null)
        {
            Check(
                suite,
                "wrong parent rejected (ours)",
                () =>
                {
                    var result = Chd.CheckFileWithParent(childOurs, wrongParent.ChdPath);
                    Assert(!result.IsSuccess, $"wrong parent accepted: {result.Error}");
                }
            );
            if (refCreated)
                Check(
                    suite,
                    "chdman verify with wrong parent fails",
                    () =>
                    {
                        var r = _chdman.Run("verify", "-i", childOurs, "-ip", wrongParent.ChdPath);
                        Assert(
                            r.ExitCode != 0,
                            $"chdman verify accepted a wrong parent (exit={r.ExitCode})"
                        );
                    }
                );
        }

        // --- child of our parent: chdman reads our parent ---
        var child2 = Path.Combine(dir, "child-of-ours.ref.chd");
        var refCreated2 = false;
        Check(
            suite,
            "chdman createraw -op (parent=ours)",
            () =>
            {
                var r = _chdman.Run(
                    "createraw",
                    "-i",
                    src,
                    "-o",
                    child2,
                    "-c",
                    "zlib",
                    "-hs",
                    hunkBytes.ToString(),
                    "-us",
                    unitBytes.ToString(),
                    "-op",
                    parentOurs.ChdPath,
                    "-f"
                );
                if (r.ExitCode != 0)
                    throw new CheckSkippedException($"chdman rejected config: {r.Combined.Trim()}");

                refCreated2 = true;
            }
        );

        var child3 = Path.Combine(dir, "child-of-ours.ours.chd");
        Check(
            suite,
            "child encode (ours, parent=ours)",
            () =>
            {
                ChdEncoder.EncodeRaw(
                    src,
                    child3,
                    hunkBytes,
                    unitBytes,
                    [CodecTags.Zlib],
                    new ChdEncodeOptions { ParentPath = parentOurs.ChdPath }
                );
            }
        );

        if (refCreated2)
        {
            Check(
                suite,
                "child encode byte-identical (parent=ours)",
                () =>
                {
                    var ours = File.ReadAllBytes(child3);
                    var refBytes = File.ReadAllBytes(child2);
                    AssertEqual(refBytes, ours, "child chd file bytes");
                }
            );
            Check(
                suite,
                "chdman verify child of ours",
                () => VerifyChdman(child2, parentOurs.ChdPath)
            );
            Check(
                suite,
                "chdman verify our child of ours",
                () => VerifyChdman(child3, parentOurs.ChdPath)
            );
            Check(
                suite,
                "extract child of ours (ref)",
                () =>
                {
                    var extracted = ExtractRaw(child2, parentOurs.ChdPath);
                    AssertEqual(mixedSrc, extracted, "extracted data");
                }
            );
        }

        Check(
            suite,
            "decode child of ours (ours)",
            () =>
            {
                var read = ReadAllBytes(child3, parentOurs.ChdPath);
                AssertEqual(mixedSrc, read, "decoded data");
            }
        );

        AddAsset(
            new Asset
            {
                Key = "delta|child-of-chdman|ours",
                Name = "delta child (ours, parent=chdman)",
                ChdPath = childOurs,
                ParentPath = parentRef.ChdPath,
                Expected = mixedSrc,
                IsCd = false,
                CodecLabel = "zlib"
            }
        );
        if (refCreated)
            AddAsset(
                new Asset
                {
                    Key = "delta|child-of-chdman|ref",
                    Name = "delta child (chdman, parent=chdman)",
                    ChdPath = childRef,
                    ParentPath = parentRef.ChdPath,
                    Expected = mixedSrc,
                    IsCd = false,
                    CodecLabel = "zlib"
                }
            );

        AddAsset(
            new Asset
            {
                Key = "delta|child-of-ours|ours",
                Name = "delta child (ours, parent=ours)",
                ChdPath = child3,
                ParentPath = parentOurs.ChdPath,
                Expected = mixedSrc,
                IsCd = false,
                CodecLabel = "zlib"
            }
        );
        if (refCreated2)
            AddAsset(
                new Asset
                {
                    Key = "delta|child-of-ours|ref",
                    Name = "delta child (chdman, parent=ours)",
                    ChdPath = child2,
                    ParentPath = parentOurs.ChdPath,
                    Expected = mixedSrc,
                    IsCd = false,
                    CodecLabel = "zlib"
                }
            );
    }

    // ----- copy suite -----

    private void RunCopySuite()
    {
        const string suite = "copy";
        var dir = Path.Combine(_workDir, "copy");
        Directory.CreateDirectory(dir);

        var srcAsset = _assets.FirstOrDefault(a =>
            string.Equals(a.Key, "mixed|zlib(4096/512)|ours", StringComparison.Ordinal)
        );
        if (srcAsset == null)
        {
            Console.WriteLine(
                $"[SKIP] {suite} — 'mixed x zlib(4096/512)' asset missing, skipping copy suite"
            );
            return;
        }

        var targets = _quick
            ? new[] { "zstd", "none" }
            : new[] { "zstd", "lzma", "huff", "flac", "none" };
        foreach (var target in targets)
        {
            var tag = $"zlib -> {target}";
            var suite2 = $"{suite} {tag}";
            var ourCopy = Path.Combine(dir, $"{target}.ours.chd");
            var refCopy = Path.Combine(dir, $"{target}.ref.chd");

            Check(
                suite2,
                "copy (ours)",
                () =>
                {
                    ChdEncoder.Copy(srcAsset.ChdPath, ourCopy, [CodecTags.FromName(target)]);
                    Assert(File.Exists(ourCopy), "output file missing");
                }
            );

            Check(suite2, "chdman verify (ours)", () => VerifyChdman(ourCopy));
            Check(
                suite2,
                "extract (ours)",
                () =>
                {
                    var extracted = ExtractRaw(ourCopy);
                    AssertEqual(srcAsset.Expected, extracted, "extracted data");
                }
            );
            Check(
                suite2,
                "decode (ours)",
                () =>
                {
                    var read = ReadAllBytes(ourCopy);
                    AssertEqual(srcAsset.Expected, read, "decoded data");
                }
            );

            var refCreated = false;
            Check(
                suite2,
                "chdman copy",
                () =>
                {
                    var r = _chdman.Run(
                        "copy",
                        "-i",
                        srcAsset.ChdPath,
                        "-o",
                        refCopy,
                        "-c",
                        target,
                        "-f"
                    );
                    if (r.ExitCode != 0)
                        throw new CheckSkippedException(
                            $"chdman rejected config: {r.Combined.Trim()}"
                        );

                    refCreated = true;
                }
            );
            if (refCreated)
            {
                Check(suite2, "chdman verify (ref)", () => VerifyChdman(refCopy));
                Check(
                    suite2,
                    "copy content identical",
                    () =>
                    {
                        var ours = ExtractRaw(ourCopy);
                        var refBytes = ExtractRaw(refCopy);
                        AssertEqual(refBytes, ours, "copied content");
                    }
                );
            }

            AddAsset(
                new Asset
                {
                    Key = $"copy|{target}|ours",
                    Name = $"copy zlib->{target} (ours)",
                    ChdPath = ourCopy,
                    Expected = srcAsset.Expected,
                    IsCd = false,
                    CodecLabel = target
                }
            );
            if (refCreated)
                AddAsset(
                    new Asset
                    {
                        Key = $"copy|{target}|ref",
                        Name = $"copy zlib->{target} (chdman)",
                        ChdPath = refCopy,
                        Expected = srcAsset.Expected,
                        IsCd = false,
                        CodecLabel = target
                    }
                );
        }

        // CD copy: cdzl -> cdfl
        var cdSrc = _assets.FirstOrDefault(a =>
            string.Equals(a.Key, "cd-mixed|cdzl(19584/2448)|ours", StringComparison.Ordinal)
        );
        if (cdSrc != null && !_quick)
        {
            const string suite2 = $"{suite} cd cdzl -> cdfl";
            var ourCdCopy = Path.Combine(dir, "cd-cdfl.ours.chd");
            var refCdCopy = Path.Combine(dir, "cd-cdfl.ref.chd");

            Check(
                suite2,
                "copy (ours)",
                () => { ChdEncoder.Copy(cdSrc.ChdPath, ourCdCopy, [CodecTags.Cdfl]); }
            );
            Check(suite2, "chdman verify (ours)", () => VerifyChdman(ourCdCopy));
            Check(
                suite2,
                "extract (ours)",
                () =>
                {
                    var extracted = ExtractRaw(ourCdCopy);
                    AssertEqual(cdSrc.Expected, extracted, "extracted data");
                }
            );

            var refCreated = false;
            Check(
                suite2,
                "chdman copy",
                () =>
                {
                    var r = _chdman.Run(
                        "copy",
                        "-i",
                        cdSrc.ChdPath,
                        "-o",
                        refCdCopy,
                        "-c",
                        "cdfl",
                        "-f"
                    );
                    if (r.ExitCode != 0)
                        throw new CheckSkippedException(
                            $"chdman rejected config: {r.Combined.Trim()}"
                        );

                    refCreated = true;
                }
            );
            if (refCreated)
                Check(
                    suite2,
                    "copy content identical",
                    () =>
                    {
                        var ours = ExtractRaw(ourCdCopy);
                        var refBytes = ExtractRaw(refCdCopy);
                        AssertEqual(refBytes, ours, "copied content");
                    }
                );

            AddAsset(
                new Asset
                {
                    Key = "copy|cd-cdfl|ours",
                    Name = "copy cd cdzl->cdfl (ours)",
                    ChdPath = ourCdCopy,
                    Expected = cdSrc.Expected,
                    IsCd = true,
                    CodecLabel = "cdfl"
                }
            );
        }

        // child copy: delta child -> zlib standalone, then a copy of the child
        var child = _assets.FirstOrDefault(a =>
            string.Equals(a.Key, "delta|child-of-chdman|ours", StringComparison.Ordinal)
        );
        if (child is { ParentPath: not null })
        {
            const string suite2 = $"{suite} child";
            var childCopy = Path.Combine(dir, "child-copy.ours.chd");
            Check(
                suite2,
                "copy child (ours, SourceParentPath)",
                () =>
                {
                    ChdEncoder.Copy(
                        child.ChdPath,
                        childCopy,
                        [CodecTags.Zlib],
                        new ChdEncodeOptions { SourceParentPath = child.ParentPath }
                    );
                }
            );
            Check(suite2, "chdman verify child copy", () => VerifyChdman(childCopy));
            Check(
                suite2,
                "extract child copy",
                () =>
                {
                    var extracted = ExtractRaw(childCopy);
                    AssertEqual(child.Expected, extracted, "extracted data");
                }
            );

            AddAsset(
                new Asset
                {
                    Key = "copy|child|ours",
                    Name = "copy delta child (ours)",
                    ChdPath = childCopy,
                    Expected = child.Expected,
                    IsCd = false,
                    CodecLabel = "zlib"
                }
            );
        }
    }

    // ----- decode suite (runs over every CHD produced in the run) -----

    private void RunDecodeSuite()
    {
        foreach (var asset in _assets)
            DecodeCase(asset);
    }

    private void DecodeCase(Asset asset)
    {
        var suite = $"decode {asset.Name}";

        Check(suite, "chdman verify", () => VerifyChdman(asset.ChdPath, asset.ParentPath));

        Check(
            suite,
            "deep CheckFile",
            () =>
            {
                if (asset.ParentPath != null)
                {
                    var result = Chd.CheckFileWithParent(asset.ChdPath, asset.ParentPath);
                    Assert(
                        result.IsSuccess,
                        $"CheckFileWithParent: {result.Error} ({result.Error.GetMessage()})"
                    );
                }
                else
                {
                    using var fs = File.OpenRead(asset.ChdPath);
                    var result = Chd.CheckFile(fs, Path.GetFileName(asset.ChdPath), true);
                    Assert(
                        result.IsSuccess,
                        $"CheckFile: {result.Error} ({result.Error.GetMessage()})"
                    );
                }
            }
        );

        Check(
            suite,
            "ReadAllBytes == chdman extract",
            () =>
            {
                var read = ReadAllBytes(asset.ChdPath, asset.ParentPath);
                AssertEqual(asset.Expected, read, "decoded data");
            }
        );

        Check(
            suite,
            "random access == chdman extract",
            () =>
            {
                var err = ChdFile.Open(asset.ChdPath, asset.ParentPath, out var chd);
                Assert(err == ChdError.Chderrnone && chd != null, $"Open: {err}");
                using (chd)
                {
                    var len = (ulong)asset.Expected.Length;
                    var hunk = (ulong)chd.HunkBytes;
                    var offsets = new List<ulong>
                    {
                        0,
                        1,
                        hunk - 1,
                        hunk,
                        hunk + 1,
                        hunk * 2 + 137,
                        len / 2,
                        len - 100,
                        len - 1
                    };
                    foreach (var o in offsets.Where(o => o < len).Distinct())
                    {
                        var count = (int)Math.Min(512, len - o);
                        var buf = new byte[count];
                        var r = chd.Read(o, buf, 0, count);
                        Assert(r == ChdError.Chderrnone, $"Read(offset={o}): {r}");
                        for (var i = 0; i < count; i++)
                            Assert(
                                buf[i] == asset.Expected[(int)(o + (ulong)i)],
                                $"byte {o + (ulong)i} mismatch"
                            );
                    }
                }
            }
        );

        Check(
            suite,
            "ReadHunk == chdman extract",
            () =>
            {
                var err = ChdFile.Open(asset.ChdPath, asset.ParentPath, out var chd);
                Assert(err == ChdError.Chderrnone && chd != null, $"Open: {err}");
                using (chd)
                {
                    var hunks = new List<uint> { 0, chd.HunkCount / 2, chd.HunkCount - 1 };
                    var buf = new byte[chd.HunkBytes];
                    foreach (var h in hunks.Distinct())
                    {
                        var r = chd.ReadHunk(h, buf);
                        Assert(r == ChdError.Chderrnone, $"ReadHunk({h}): {r}");
                        var expectedOffset = (int)((ulong)h * chd.HunkBytes);
                        for (
                            var i = 0;
                            i < buf.Length && expectedOffset + i < asset.Expected.Length;
                            i++
                        )
                            Assert(
                                buf[i] == asset.Expected[expectedOffset + i],
                                $"hunk {h} byte {i} mismatch"
                            );
                    }
                }
            }
        );

        Check(
            suite,
            "Read past end -> error",
            () =>
            {
                var err = ChdFile.Open(asset.ChdPath, asset.ParentPath, out var chd);
                Assert(err == ChdError.Chderrnone && chd != null, $"Open: {err}");
                using (chd)
                {
                    var buf = new byte[8];
                    var r = chd.Read(chd.TotalBytes, buf, 0, 1);
                    Assert(r != ChdError.Chderrnone, $"Read(past end) returned {r}");
                    if (chd.TotalBytes > 0)
                    {
                        r = chd.Read(chd.TotalBytes - 1, buf, 0, 2);
                        Assert(r != ChdError.Chderrnone, $"Read(overlapping end) returned {r}");
                    }
                }
            }
        );
    }

    // ----- info parity suite -----

    private void RunInfoSuite()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var asset in _assets)
        {
            if (!seen.Add(asset.CodecLabel + (asset.IsCd ? "|cd" : "|raw")))
                continue;

            var suite = $"info {asset.Name}";
            Check(
                suite,
                "ReadHeader == chdman info",
                () => InfoParity(asset.ChdPath, asset.ChdPath)
            );
        }
    }

    // ----- CLI battle suite (runs CHDSharpCli vs chdman.exe) -----

    private void RunCliSuite()
    {
        if (_cli == null)
            return;

        Console.WriteLine();
        Console.WriteLine("== CLI Battle Suite: CHDSharpCli vs chdman.exe ==");

        RunCliInfoSuite();
        RunCliVerifySuite();
        RunCliCreateRawSuite();
        RunCliCreateHdSuite();
        RunCliCreateCdSuite();
        RunCliCopySuite();
        RunCliExtractRawSuite();
        RunCliExtractCdSuite();
        RunCliAddMetaSuite();

        // Full parity: exhaustively test every CLI command and every documented arg
        // (aliases, size suffixes, parent variants, slice windows, force/verbose,
        //  error paths). Runs CHDSharp.exe vs chdman.exe and checks exit-code/content parity.
        RunCliFullParitySuites();
    }

    private void RunCliInfoSuite()
    {
        const string suite = "cli-info";
        foreach (var asset in _assets.Take(_quick ? 3 : _assets.Count))
        {
            var tag = asset.Name;
            Check(
                suite,
                $"info {tag}",
                () =>
                {
                    var chdmanInfo = _chdman.Info(asset.ChdPath);
                    Assert(chdmanInfo != null, "chdman info failed");

                    var cliR = _cli!.Run("info", "-i", asset.ChdPath);
                    Assert(
                        cliR.ExitCode == 0,
                        $"CLI info failed (exit={cliR.ExitCode}): {cliR.Combined.Trim()}"
                    );

                    var cliInfo = ChdmanRunner.ParseInfo(cliR.Combined);
                    Assert(cliInfo != null, "CLI info output not parseable");

                    Assert(
                        chdmanInfo.Version == cliInfo.Version,
                        $"version {cliInfo.Version} != chdman {chdmanInfo.Version}"
                    );
                    Assert(
                        chdmanInfo.LogicalBytes == cliInfo.LogicalBytes,
                        $"logical size {cliInfo.LogicalBytes} != chdman {chdmanInfo.LogicalBytes}"
                    );
                    Assert(
                        chdmanInfo.HunkBytes == cliInfo.HunkBytes,
                        $"hunk size {cliInfo.HunkBytes} != chdman {chdmanInfo.HunkBytes}"
                    );
                    Assert(
                        chdmanInfo.TotalHunks == cliInfo.TotalHunks,
                        $"hunks {cliInfo.TotalHunks} != chdman {chdmanInfo.TotalHunks}"
                    );
                    Assert(
                        chdmanInfo.UnitBytes == cliInfo.UnitBytes,
                        $"unit size {cliInfo.UnitBytes} != chdman {chdmanInfo.UnitBytes}"
                    );
                    Assert(
                        chdmanInfo.TotalUnits == cliInfo.TotalUnits,
                        $"units {cliInfo.TotalUnits} != chdman {chdmanInfo.TotalUnits}"
                    );

                    var normChdman = NormalizeChdmanCodec(chdmanInfo.Compression);
                    var normCli = NormalizeChdmanCodec(cliInfo.Compression);
                    Assert(
                        string.Equals(normChdman, normCli, StringComparison.Ordinal),
                        $"compression '{normCli}' != chdman '{normChdman}'"
                    );

                    if (chdmanInfo.Sha1 != null)
                        Assert(
                            string.Equals(chdmanInfo.Sha1, cliInfo.Sha1, StringComparison.Ordinal),
                            $"SHA1 {cliInfo.Sha1} != chdman {chdmanInfo.Sha1}"
                        );

                    if (chdmanInfo.DataSha1 != null)
                        Assert(
                            string.Equals(
                                chdmanInfo.DataSha1,
                                cliInfo.DataSha1,
                                StringComparison.Ordinal
                            ),
                            $"Data SHA1 {cliInfo.DataSha1} != chdman {chdmanInfo.DataSha1}"
                        );
                }
            );
        }
    }

    private void RunCliVerifySuite()
    {
        const string suite = "cli-verify";
        foreach (var asset in _assets.Take(_quick ? 3 : _assets.Count))
        {
            var tag = asset.Name;
            Check(
                suite,
                $"verify {tag}",
                () =>
                {
                    var chdmanR = _chdman.Run("verify", "-i", asset.ChdPath);
                    var cliR = _cli!.Run("verify", "-i", asset.ChdPath);

                    Assert(
                        chdmanR.ExitCode == cliR.ExitCode,
                        $"exit code {cliR.ExitCode} != chdman {chdmanR.ExitCode}"
                    );
                }
            );
        }
    }

    private void RunCliCreateRawSuite()
    {
        const string suite = "cli-createraw";
        var dir = Path.Combine(_workDir, "cli-raw");
        Directory.CreateDirectory(dir);

        var inputs = _quick
            ? new[]
            {
                ("zeros", TestDataGenerator.Zeros(64 * 1024)),
                ("random", TestDataGenerator.Random(128 * 1024, _seed))
            }
            : new[]
            {
                ("zeros", TestDataGenerator.Zeros(256 * 1024)),
                ("random", TestDataGenerator.Random(512 * 1024, _seed)),
                ("mixed", TestDataGenerator.Mixed(512 * 1024, _seed))
            };

        var configs = _quick
            ? new[] { new RawConfig("zlib", 4096, 512) }
            : new[]
            {
                new RawConfig("zlib", 4096, 512),
                new RawConfig("lzma", 4096, 512),
                new RawConfig("none", 4096, 512)
            };

        foreach (var (name, data) in inputs)
        foreach (var cfg in configs)
        {
            var tag = $"{name} x {cfg.Label}";
            var inputPath = Path.Combine(dir, $"{name}.bin");
            File.WriteAllBytes(inputPath, data);

            var cliChd = Path.Combine(dir, $"{name}-{cfg.Codecs}.cli.chd");
            var refChd = Path.Combine(dir, $"{name}-{cfg.Codecs}.ref.chd");

            Check(
                suite,
                $"createraw {tag}",
                () =>
                {
                    var cliR = _cli!.Run(
                        "createraw",
                        "-i",
                        inputPath,
                        "-o",
                        cliChd,
                        "-c",
                        cfg.Codecs,
                        "-hs",
                        cfg.HunkBytes.ToString(),
                        "-us",
                        cfg.UnitBytes.ToString(),
                        "-f"
                    );
                    Assert(
                        cliR.ExitCode == 0,
                        $"CLI createraw failed (exit={cliR.ExitCode}): {cliR.Combined.Trim()}"
                    );
                    Assert(File.Exists(cliChd), "CLI output file missing");
                }
            );

            var refCreated = false;
            if (data.Length % cfg.UnitBytes == 0)
                Check(
                    suite,
                    $"chdman createraw {tag}",
                    () =>
                    {
                        var r = _chdman.Run(
                            "createraw",
                            "-i",
                            inputPath,
                            "-o",
                            refChd,
                            "-c",
                            cfg.Codecs,
                            "-hs",
                            cfg.HunkBytes.ToString(),
                            "-us",
                            cfg.UnitBytes.ToString(),
                            "-f"
                        );
                        if (r.ExitCode != 0)
                            throw new CheckSkippedException(
                                $"chdman rejected config: {r.Combined.Trim()}"
                            );

                        refCreated = true;
                    }
                );

            if (refCreated)
            {
                Check(
                    suite,
                    $"createraw byte-identical {tag}",
                    () =>
                    {
                        var ours = File.ReadAllBytes(cliChd);
                        var refBytes = File.ReadAllBytes(refChd);
                        AssertEqual(refBytes, ours, "chd file bytes");
                    }
                );

                Check(
                    suite,
                    $"createraw content parity {tag}",
                    () =>
                    {
                        var cliExtract = ExtractRaw(cliChd);
                        var refExtract = ExtractRaw(refChd);
                        AssertEqual(refExtract, cliExtract, "extracted data");
                    }
                );
            }

            Check(suite, $"createraw verify {tag}", () => VerifyChdman(cliChd));
        }
    }

    private void RunCliCreateHdSuite()
    {
        const string suite = "cli-createhd";
        var dir = Path.Combine(_workDir, "cli-hd");
        Directory.CreateDirectory(dir);

        var sizes = _quick
            ? new[] { 4096L, 32 * 1024L }
            : new[] { 4096L, 32 * 1024L, 1024 * 1024L };

        foreach (var size in sizes)
        {
            var tag = $"size-{size}";
            var cliChd = Path.Combine(dir, $"hd-{size}.cli.chd");
            var refChd = Path.Combine(dir, $"hd-{size}.ref.chd");

            Check(
                suite,
                $"createhd {tag}",
                () =>
                {
                    var cliR = _cli!.Run("createhd", "-o", cliChd, "-s", size.ToString(), "-f");
                    Assert(
                        cliR.ExitCode == 0,
                        $"CLI createhd failed (exit={cliR.ExitCode}): {cliR.Combined.Trim()}"
                    );
                    Assert(File.Exists(cliChd), "CLI output file missing");
                }
            );

            Check(
                suite,
                $"chdman createhd {tag}",
                () =>
                {
                    var r = _chdman.Run("createhd", "-o", refChd, "-s", size.ToString(), "-f");
                    Assert(r.ExitCode == 0, $"chdman createhd failed: {r.Combined.Trim()}");
                }
            );

            Check(
                suite,
                $"createhd byte-identical {tag}",
                () =>
                {
                    var ours = File.ReadAllBytes(cliChd);
                    var refBytes = File.ReadAllBytes(refChd);
                    AssertEqual(refBytes, ours, "chd file bytes");
                }
            );

            Check(suite, $"createhd verify {tag}", () => VerifyChdman(cliChd));
        }
    }

    private void RunCliCreateCdSuite()
    {
        const string suite = "cli-createcd";
        var dir = Path.Combine(_workDir, "cli-cd");
        Directory.CreateDirectory(dir);

        TestDataGenerator.CreateMixedCd(dir, _seed, out var mixedCue, out _);

        var cliChd = Path.Combine(dir, "cd-mixed.cli.chd");
        var refChd = Path.Combine(dir, "cd-mixed.ref.chd");

        Check(
            suite,
            "createcd (CLI)",
            () =>
            {
                var cliR = _cli!.Run("createcd", "-i", mixedCue, "-o", cliChd, "-f");
                Assert(
                    cliR.ExitCode == 0,
                    $"CLI createcd failed (exit={cliR.ExitCode}): {cliR.Combined.Trim()}"
                );
                Assert(File.Exists(cliChd), "CLI output file missing");
            }
        );

        Check(
            suite,
            "createcd (chdman)",
            () =>
            {
                var r = _chdman.Run("createcd", "-i", mixedCue, "-o", refChd, "-f");
                Assert(r.ExitCode == 0, $"chdman createcd failed: {r.Combined.Trim()}");
            }
        );

        Check(
            suite,
            "createcd content parity",
            () =>
            {
                var cliExtract = ExtractRaw(cliChd);
                var refExtract = ExtractRaw(refChd);
                AssertEqual(refExtract, cliExtract, "extracted data");
            }
        );

        Check(suite, "createcd verify (CLI)", () => VerifyChdman(cliChd));
    }

    private void RunCliCopySuite()
    {
        const string suite = "cli-copy";
        var dir = Path.Combine(_workDir, "cli-copy");
        Directory.CreateDirectory(dir);

        // Use a raw asset from the library test — prefer the >256-hunk partial-tail one so the
        // copy path also exercises chdman's work-buffer stale-tail quirk
        var srcAsset =
            _assets.FirstOrDefault(a =>
                string.Equals(a.Key, "long-tail|zlib(4096/512)|ours", StringComparison.Ordinal)
            )
            ?? _assets.FirstOrDefault(a =>
                string.Equals(a.Key, "mixed|zlib(4096/512)|ours", StringComparison.Ordinal)
            )
            ?? _assets.FirstOrDefault(a =>
                a.Key.Contains("zlib", StringComparison.Ordinal) && !a.IsCd
            );
        if (srcAsset == null)
        {
            Console.WriteLine($"[SKIP] {suite} — no raw zlib asset available");
            return;
        }

        var cliCopy = Path.Combine(dir, "copy.cli.chd");
        var refCopy = Path.Combine(dir, "copy.ref.chd");

        Check(
            suite,
            "copy (CLI)",
            () =>
            {
                var cliR = _cli!.Run(
                    "copy",
                    "-i",
                    srcAsset.ChdPath,
                    "-o",
                    cliCopy,
                    "-c",
                    "lzma",
                    "-f"
                );
                Assert(
                    cliR.ExitCode == 0,
                    $"CLI copy failed (exit={cliR.ExitCode}): {cliR.Combined.Trim()}"
                );
                Assert(File.Exists(cliCopy), "CLI output file missing");
            }
        );

        Check(
            suite,
            "copy (chdman)",
            () =>
            {
                var r = _chdman.Run(
                    "copy",
                    "-i",
                    srcAsset.ChdPath,
                    "-o",
                    refCopy,
                    "-c",
                    "lzma",
                    "-f"
                );
                Assert(r.ExitCode == 0, $"chdman copy failed: {r.Combined.Trim()}");
            }
        );

        Check(
            suite,
            "copy content parity",
            () =>
            {
                var cliExtract = ExtractRaw(cliCopy);
                var refExtract = ExtractRaw(refCopy);
                AssertEqual(refExtract, cliExtract, "copied content");
            }
        );

        Check(
            suite,
            "copy byte-identical",
            () =>
            {
                var cliBytes = File.ReadAllBytes(cliCopy);
                var refBytes = File.ReadAllBytes(refCopy);
                AssertEqual(refBytes, cliBytes, "chd file bytes");
            }
        );

        Check(suite, "copy verify (CLI)", () => VerifyChdman(cliCopy));
    }

    private void RunCliExtractRawSuite()
    {
        const string suite = "cli-extractraw";
        var dir = Path.Combine(_workDir, "cli-extract");
        Directory.CreateDirectory(dir);

        // Use a raw asset
        var srcAsset =
            _assets.FirstOrDefault(a =>
                string.Equals(a.Key, "mixed|zlib(4096/512)|ours", StringComparison.Ordinal)
            )
            ?? _assets.FirstOrDefault(a =>
                a.Key.Contains("zlib", StringComparison.Ordinal) && !a.IsCd
            );
        if (srcAsset == null)
        {
            Console.WriteLine($"[SKIP] {suite} — no raw zlib asset available");
            return;
        }

        var cliOut = Path.Combine(dir, "extract.cli.raw");
        var refOut = Path.Combine(dir, "extract.ref.raw");

        Check(
            suite,
            "extractraw (CLI)",
            () =>
            {
                var cliR = _cli!.Run("extractraw", "-i", srcAsset.ChdPath, "-o", cliOut, "-f");
                Assert(
                    cliR.ExitCode == 0,
                    $"CLI extractraw failed (exit={cliR.ExitCode}): {cliR.Combined.Trim()}"
                );
                Assert(File.Exists(cliOut), "CLI output file missing");
            }
        );

        Check(
            suite,
            "extractraw (chdman)",
            () =>
            {
                var r = _chdman.Run("extractraw", "-i", srcAsset.ChdPath, "-o", refOut, "-f");
                Assert(r.ExitCode == 0, $"chdman extractraw failed: {r.Combined.Trim()}");
            }
        );

        Check(
            suite,
            "extractraw byte-identical",
            () =>
            {
                var cliBytes = File.ReadAllBytes(cliOut);
                var refBytes = File.ReadAllBytes(refOut);
                AssertEqual(refBytes, cliBytes, "extracted bytes");
            }
        );

        Check(
            suite,
            "extractraw matches expected",
            () =>
            {
                var cliBytes = File.ReadAllBytes(cliOut);
                AssertEqual(srcAsset.Expected, cliBytes, "extracted data");
            }
        );
    }

    private void RunCliExtractCdSuite()
    {
        const string suite = "cli-extractcd";
        var dir = Path.Combine(_workDir, "cli-extractcd");
        Directory.CreateDirectory(dir);

        // Use a CD asset
        var cdAsset = _assets.FirstOrDefault(a =>
            a.IsCd && a.Key.Contains("cdzl", StringComparison.Ordinal)
        );
        if (cdAsset == null)
        {
            Console.WriteLine($"[SKIP] {suite} — no CD asset available");
            return;
        }

        var cliCue = Path.Combine(dir, "extract.cli.cue");
        var refCue = Path.Combine(dir, "extract.ref.cue");

        Check(
            suite,
            "extractcd (CLI)",
            () =>
            {
                var cliR = _cli!.Run("extractcd", "-i", cdAsset.ChdPath, "-o", cliCue, "-f");
                Assert(
                    cliR.ExitCode == 0,
                    $"CLI extractcd failed (exit={cliR.ExitCode}): {cliR.Combined.Trim()}"
                );
                Assert(File.Exists(cliCue), "CLI output file missing");
            }
        );

        Check(
            suite,
            "extractcd (chdman)",
            () =>
            {
                var r = _chdman.Run("extractcd", "-i", cdAsset.ChdPath, "-o", refCue, "-f");
                Assert(r.ExitCode == 0, $"chdman extractcd failed: {r.Combined.Trim()}");
            }
        );

        Check(
            suite,
            "extractcd CUE parity",
            () =>
            {
                var cliCueText = NormalizeCueBinName(File.ReadAllText(cliCue).Trim());
                var refCueText = NormalizeCueBinName(File.ReadAllText(refCue).Trim());
                Assert(
                    string.Equals(cliCueText, refCueText, StringComparison.Ordinal),
                    $"CUE sheets differ:\nCLI: {cliCueText[..Math.Min(200, cliCueText.Length)]}\nref: {refCueText[..Math.Min(200, refCueText.Length)]}"
                );
            }
        );
    }

    /// <summary>
    ///     Normalizes the FILE line of a CUE sheet to a common bin name so two CUE sheets
    ///     written to different paths/names can be compared structurally.
    /// </summary>
    private static string NormalizeCueBinName(string cueText)
    {
        var lines = new List<string>();
        foreach (var line in cueText.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            if (trimmed.StartsWith("FILE ", StringComparison.Ordinal))
            {
                var q1 = trimmed.IndexOf('"');
                var q2 = trimmed.LastIndexOf('"');
                if (q1 >= 0 && q1 != q2)
                    trimmed = trimmed[..q1] + "\"disc.bin\"" + trimmed[(q2 + 1)..];
            }

            lines.Add(trimmed);
        }

        return string.Join("\n", lines);
    }

    private void RunCliAddMetaSuite()
    {
        const string suite = "cli-addmeta";
        var dir = Path.Combine(_workDir, "cli-meta");
        Directory.CreateDirectory(dir);

        // Create a small CHD for metadata testing. chdman can only modify (addmeta/delmeta)
        // UNCOMPRESSED CHDs (its V5 reader sets m_allow_writes = !compressed()), so use
        // "-c none" to keep both sides able to rewrite the file.
        var srcData = TestDataGenerator.Zeros(64 * 1024);
        var srcPath = Path.Combine(dir, "meta-src.bin");
        File.WriteAllBytes(srcPath, srcData);

        var cliChd = Path.Combine(dir, "meta.cli.chd");
        var refChd = Path.Combine(dir, "meta.ref.chd");

        // Create CHDs
        var cliCreated = false;
        Check(
            suite,
            "create for meta (CLI)",
            () =>
            {
                var cliR = _cli!.Run(
                    "createraw",
                    "-i",
                    srcPath,
                    "-o",
                    cliChd,
                    "-hs",
                    "4096",
                    "-us",
                    "512",
                    "-c",
                    "none",
                    "-f"
                );
                Assert(cliR.ExitCode == 0, $"CLI createraw failed: {cliR.Combined.Trim()}");
                cliCreated = true;
            }
        );
        Check(
            suite,
            "create for meta (chdman)",
            () =>
            {
                var r = _chdman.Run(
                    "createraw",
                    "-i",
                    srcPath,
                    "-o",
                    refChd,
                    "-hs",
                    "4096",
                    "-us",
                    "512",
                    "-c",
                    "none",
                    "-f"
                );
                Assert(r.ExitCode == 0, $"chdman createraw failed: {r.Combined.Trim()}");
            }
        );

        if (cliCreated)
        {
            // Add metadata
            Check(
                suite,
                "addmeta (CLI)",
                () =>
                {
                    var cliR = _cli!.Run(
                        "addmeta",
                        "-i",
                        cliChd,
                        "-t",
                        "TEST",
                        "-vt",
                        "hello world"
                    );
                    Assert(
                        cliR.ExitCode == 0,
                        $"CLI addmeta failed (exit={cliR.ExitCode}): {cliR.Combined.Trim()}"
                    );
                }
            );

            // Dump metadata and compare
            var cliMetaOut = Path.Combine(dir, "meta.cli.bin");
            Check(
                suite,
                "dumpmeta (CLI)",
                () =>
                {
                    var cliR = _cli!.Run(
                        "dumpmeta",
                        "-i",
                        cliChd,
                        "-t",
                        "TEST",
                        "-o",
                        cliMetaOut,
                        "-f"
                    );
                    Assert(cliR.ExitCode == 0, $"CLI dumpmeta failed: {cliR.Combined.Trim()}");
                }
            );

            // Delete metadata
            var cliChd2 = Path.Combine(dir, "meta2.cli.chd");
            File.Copy(cliChd, cliChd2, true);

            Check(
                suite,
                "delmeta (CLI)",
                () =>
                {
                    var cliR = _cli!.Run("delmeta", "-i", cliChd2, "-t", "TEST");
                    Assert(
                        cliR.ExitCode == 0,
                        $"CLI delmeta failed (exit={cliR.ExitCode}): {cliR.Combined.Trim()}"
                    );
                }
            );

            Check(suite, "verify after meta ops (CLI)", () => VerifyChdman(cliChd2));
        }

        // chdman side
        Check(
            suite,
            "addmeta (chdman)",
            () =>
            {
                var r = _chdman.Run("addmeta", "-i", refChd, "-t", "TEST", "-vt", "hello world");
                Assert(r.ExitCode == 0, $"chdman addmeta failed: {r.Combined.Trim()}");
            }
        );

        var refMetaOut = Path.Combine(dir, "meta.ref.bin");
        Check(
            suite,
            "dumpmeta (chdman)",
            () =>
            {
                var r = _chdman.Run("dumpmeta", "-i", refChd, "-t", "TEST", "-o", refMetaOut, "-f");
                Assert(r.ExitCode == 0, $"chdman dumpmeta failed: {r.Combined.Trim()}");
            }
        );

        if (cliCreated && File.Exists(Path.Combine(dir, "meta.cli.bin")))
            Check(
                suite,
                "metadata content parity",
                () =>
                {
                    var cliBytes = File.ReadAllBytes(Path.Combine(dir, "meta.cli.bin"));
                    var refBytes = File.ReadAllBytes(refMetaOut);
                    AssertEqual(refBytes, cliBytes, "metadata bytes");
                }
            );

        var refChd2 = Path.Combine(dir, "meta2.ref.chd");
        File.Copy(refChd, refChd2, true);

        Check(
            suite,
            "delmeta (chdman)",
            () =>
            {
                var r = _chdman.Run("delmeta", "-i", refChd2, "-t", "TEST");
                Assert(r.ExitCode == 0, $"chdman delmeta failed: {r.Combined.Trim()}");
            }
        );

        Check(suite, "verify after meta ops (chdman)", () => VerifyChdman(refChd2));
    }

    // ----- real-file suite (battle-test user CHDs from real folders) -----

    private void RunRealSuites()
    {
        foreach (var rawRoot in _realDirs)
        {
            var root = Path.GetFullPath(rawRoot);
            if (!Directory.Exists(root))
            {
                Console.WriteLine($"[SKIP] real-corpus — folder not found: {rawRoot}");
                continue;
            }

            var files = new List<string>();
            CollectChdFiles(root, files);
            if (files.Count == 0)
            {
                Console.WriteLine($"[SKIP] real-corpus — no *.chd files under: {root}");
                continue;
            }

            Console.WriteLine();
            Console.WriteLine($"== Real-file corpus suite: {root}  ({files.Count} CHD files) ==");

            // Real CHDs can be far larger than the synthetic corpus; give verify/extract a
            // longer per-command timeout (configurable via --real-timeout).
            var chdman = new ChdmanRunner(_chdman.ExePath, _realTimeoutMs);
            var cli = _cli != null ? new CliRunner(_cli.ExePath, _realTimeoutMs) : null;

            RunRealSuite(chdman, cli, root, files);
        }
    }

    private static void CollectChdFiles(string rootDir, List<string> files)
    {
        var di = new DirectoryInfo(rootDir);
        try
        {
            foreach (
                var f in di.GetFiles(
                    "*.chd",
                    new EnumerationOptions
                    {
                        IgnoreInaccessible = true,
                        RecurseSubdirectories = false,
                        AttributesToSkip = FileAttributes.ReparsePoint
                    }
                )
            )
                if (f.Extension.Equals(".chd", StringComparison.OrdinalIgnoreCase))
                    files.Add(f.FullName);

            foreach (var d in di.GetDirectories())
                CollectChdFiles(d.FullName, files);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or DirectoryNotFoundException)
        {
            // ignore inaccessible subtrees
        }
    }

    private void RunRealSuite(ChdmanRunner chdman, CliRunner? cli, string root, List<string> files)
    {
        // Phase 1: read every header via the library (fast, header-only) and index parents by
        // their combined SHA1 so differential children can resolve their parent from the set.
        var parentsBySha1 = new Dictionary<string, string>(StringComparer.Ordinal);
        var parentsByMd5 = new Dictionary<string, string>(StringComparer.Ordinal);
        var headers = new Dictionary<string, ChdHeaderInfo?>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            var err = Chd.ReadHeader(file, out var h);
            headers[file] = err == ChdError.Chderrnone ? h : null;
            if (h == null)
                continue;

            if (h.Sha1 != null && !Util.IsAllZeroArray(h.Sha1))
                parentsBySha1[Util.ToHex(h.Sha1)] = file;

            if (h.Md5 != null && !Util.IsAllZeroArray(h.Md5))
                parentsByMd5[Util.ToHex(h.Md5)] = file;
        }

        var childCount = 0;
        foreach (var file in files)
        {
            var rel = file.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                ? file[root.Length..].TrimStart(Path.DirectorySeparatorChar)
                : file;
            var name = Path.GetFileName(file);
            var suite = $"real {rel}";
            var header = headers[file];

            Check(
                suite,
                "ReadHeader (library)",
                () => Assert(header != null, "library header read failed")
            );

            if (header == null)
                continue;

            string? parentPath = null;
            if (header.HasParent)
            {
                if (header.ParentSha1 != null && !Util.IsAllZeroArray(header.ParentSha1))
                {
                    var parentKey = Util.ToHex(header.ParentSha1);
                    parentPath = parentsBySha1.GetValueOrDefault(parentKey);
                }

                if (
                    parentPath == null
                    && header.ParentMd5 != null
                    && !Util.IsAllZeroArray(header.ParentMd5)
                )
                {
                    var parentKey = Util.ToHex(header.ParentMd5);
                    parentPath = parentsByMd5.GetValueOrDefault(parentKey);
                }

                if (parentPath == null)
                {
                    childCount++;
                    Console.WriteLine(
                        "      (child; parent not found in scanned set — content checks SKIP)"
                    );
                }
            }

            ChdmanInfo? chdmanInfo = null;
            Check(
                suite,
                "chdman info",
                () =>
                {
                    chdmanInfo = chdman.Info(file);
                    Assert(chdmanInfo != null, "chdman info failed");
                }
            );

            if (cli != null)
            {
                ChdmanInfo? cliInfo = null;
                Check(
                    suite,
                    "CLI info",
                    () =>
                    {
                        var r = cli.Run("info", "-i", file);
                        Assert(
                            r.ExitCode == 0,
                            $"CLI info failed (exit={r.ExitCode}): {r.Combined.Trim()}"
                        );
                        cliInfo = ChdmanRunner.ParseInfo(r.Combined);
                        Assert(cliInfo != null, "CLI info output not parseable");
                    }
                );

                if (chdmanInfo != null && cliInfo != null)
                    Check(
                        suite,
                        "info parity (chdman vs CLI)",
                        () =>
                        {
                            Assert(
                                chdmanInfo.Version == cliInfo.Version,
                                $"info version {cliInfo.Version} != chdman {chdmanInfo.Version}"
                            );
                            Assert(
                                chdmanInfo.LogicalBytes == cliInfo.LogicalBytes,
                                $"logical size {cliInfo.LogicalBytes} != chdman {chdmanInfo.LogicalBytes}"
                            );
                            Assert(
                                chdmanInfo.HunkBytes == cliInfo.HunkBytes,
                                $"hunk size {cliInfo.HunkBytes} != chdman {chdmanInfo.HunkBytes}"
                            );
                            Assert(
                                chdmanInfo.TotalHunks == cliInfo.TotalHunks,
                                $"hunks {cliInfo.TotalHunks} != chdman {chdmanInfo.TotalHunks}"
                            );
                            Assert(
                                chdmanInfo.UnitBytes == cliInfo.UnitBytes,
                                $"unit size {cliInfo.UnitBytes} != chdman {chdmanInfo.UnitBytes}"
                            );
                            Assert(
                                chdmanInfo.TotalUnits == cliInfo.TotalUnits,
                                $"units {cliInfo.TotalUnits} != chdman {chdmanInfo.TotalUnits}"
                            );
                            Assert(
                                string.Equals(
                                    NormalizeChdmanCodec(chdmanInfo.Compression),
                                    NormalizeChdmanCodec(cliInfo.Compression),
                                    StringComparison.Ordinal
                                ),
                                $"compression '{cliInfo.Compression}' != chdman '{chdmanInfo.Compression}'"
                            );
                            if (chdmanInfo.Sha1 != null)
                                Assert(
                                    string.Equals(
                                        chdmanInfo.Sha1,
                                        cliInfo.Sha1,
                                        StringComparison.Ordinal
                                    ),
                                    $"SHA1 {cliInfo.Sha1} != chdman {chdmanInfo.Sha1}"
                                );

                            if (chdmanInfo.DataSha1 != null)
                                Assert(
                                    string.Equals(
                                        chdmanInfo.DataSha1,
                                        cliInfo.DataSha1,
                                        StringComparison.Ordinal
                                    ),
                                    $"Data SHA1 {cliInfo.DataSha1} != chdman {chdmanInfo.DataSha1}"
                                );

                            if (chdmanInfo.ParentSha1 != null)
                                Assert(
                                    string.Equals(
                                        chdmanInfo.ParentSha1,
                                        cliInfo.ParentSha1,
                                        StringComparison.Ordinal
                                    ),
                                    $"Parent SHA1 {cliInfo.ParentSha1} != chdman {chdmanInfo.ParentSha1}"
                                );
                        }
                    );
            }

            if (chdmanInfo != null)
                Check(
                    suite,
                    "header parity (library vs chdman)",
                    () => HeaderParity(header, chdmanInfo)
                );

            var canReadContent = !header.HasParent || parentPath != null;
            if (canReadContent)
            {
                if (cli != null)
                    Check(
                        suite,
                        "verify parity (chdman vs CLI)",
                        () =>
                        {
                            var args = new List<string> { "-i", file };
                            if (parentPath != null)
                                args.AddRange(["-ip", parentPath]);
                            var chdmanR = chdman.Run("verify", args.ToArray());
                            var cliR = cli.Run("verify", args.ToArray());
                            Assert(
                                chdmanR.ExitCode == cliR.ExitCode,
                                $"verify exit {cliR.ExitCode} != chdman {chdmanR.ExitCode}: {cliR.Combined.Trim()}"
                            );
                        }
                    );

                Check(
                    suite,
                    "library deep CheckFile",
                    () =>
                    {
                        var result =
                            parentPath != null
                                ? Chd.CheckFileWithParent(file, parentPath)
                                : Chd.CheckFile(File.OpenRead(file), name, true);
                        Assert(
                            result.IsSuccess,
                            $"CheckFile: {result.Error} ({result.Error.GetMessage()})"
                        );
                    }
                );
            }
        }

        if (childCount > 0)
            Console.WriteLine(
                $"      ({childCount} child CHD(s) could not be resolved to a parent in the scanned set)"
            );
    }

    private static void HeaderParity(ChdHeaderInfo header, ChdmanInfo info)
    {
        Assert(
            header.Version == (uint)info.Version,
            $"version {header.Version} != chdman {info.Version}"
        );
        Assert(
            header.TotalBytes == info.LogicalBytes,
            $"logical size {header.TotalBytes} != chdman {info.LogicalBytes}"
        );
        Assert(
            header.HunkBytes == info.HunkBytes,
            $"hunk size {header.HunkBytes} != chdman {info.HunkBytes}"
        );
        Assert(
            header.TotalHunks == info.TotalHunks,
            $"hunks {header.TotalHunks} != chdman {info.TotalHunks}"
        );
        Assert(
            header.UnitBytes == info.UnitBytes,
            $"unit size {header.UnitBytes} != chdman {info.UnitBytes}"
        );
        Assert(
            header.UnitCount == info.TotalUnits,
            $"units {header.UnitCount} != chdman {info.TotalUnits}"
        );

        var expectedCompression = ChdmanCodecLabel(header.Compression);
        Assert(
            string.Equals(
                NormalizeChdmanCodec(info.Compression),
                expectedCompression,
                StringComparison.Ordinal
            ),
            $"compression '{info.Compression}' (normalized '{NormalizeChdmanCodec(info.Compression)}') != ours '{expectedCompression}'"
        );

        if (info.Sha1 != null)
        {
            var sha1 =
                header.Sha1 != null && !Util.IsAllZeroArray(header.Sha1)
                    ? Util.ToHex(header.Sha1)
                    : null;
            Assert(
                sha1 != null && string.Equals(sha1, info.Sha1, StringComparison.Ordinal),
                $"combined SHA1 {sha1 ?? "(none)"} != chdman {info.Sha1}"
            );
        }

        if (info.DataSha1 != null)
        {
            var rawSha1 =
                header.RawSha1 != null && !Util.IsAllZeroArray(header.RawSha1)
                    ? Util.ToHex(header.RawSha1)
                    : null;
            Assert(
                rawSha1 != null && string.Equals(rawSha1, info.DataSha1, StringComparison.Ordinal),
                $"raw SHA1 {rawSha1 ?? "(none)"} != chdman {info.DataSha1}"
            );
        }

        if (info.ParentSha1 != null)
        {
            var parentSha1 =
                header.ParentSha1 != null && !Util.IsAllZeroArray(header.ParentSha1)
                    ? Util.ToHex(header.ParentSha1)
                    : null;
            Assert(
                parentSha1 != null
                && string.Equals(parentSha1, info.ParentSha1, StringComparison.Ordinal),
                $"parent SHA1 {parentSha1 ?? "(none)"} != chdman {info.ParentSha1}"
            );
        }
    }

    // ----- shared helpers -----

    private void VerifyChdman(string chdPath, string? parentPath = null)
    {
        var args = new List<string> { "-i", chdPath };
        if (parentPath != null)
            args.AddRange(["-ip", parentPath]);
        var r = _chdman.Run("verify", args.ToArray());
        Assert(r.ExitCode == 0, $"chdman verify failed (exit={r.ExitCode}): {r.Combined.Trim()}");
    }

    private byte[] ExtractRaw(string chdPath, string? parentPath = null)
    {
        var outPath = chdPath + ".raw";
        var args = new List<string> { "-i", chdPath, "-o", outPath, "-f" };
        if (parentPath != null)
            args.AddRange(["-ip", parentPath]);
        var r = _chdman.Run("extractraw", args.ToArray());
        Assert(
            r.ExitCode == 0,
            $"chdman extractraw failed (exit={r.ExitCode}): {r.Combined.Trim()}"
        );
        return File.ReadAllBytes(outPath);
    }

    private static byte[] ReadAllBytes(string chdPath, string? parentPath = null)
    {
        var err = ChdFile.Open(chdPath, parentPath, out var chd);
        Assert(err == ChdError.Chderrnone && chd != null, $"Open: {err}");
        using (chd)
        {
            var r = chd.ReadAllBytes(out var data);
            Assert(r == ChdError.Chderrnone, $"ReadAllBytes: {r}");
            return data;
        }
    }

    private static ChdHeaderInfo ReadHeaderOrThrow(string chdPath)
    {
        var err = Chd.ReadHeader(chdPath, out var header);
        Assert(err == ChdError.Chderrnone && header != null, $"ReadHeader: {err}");
        return header;
    }

    private void InfoParity(string ourChd, string refChd)
    {
        var header = ReadHeaderOrThrow(ourChd);
        var info = _chdman.Info(refChd);
        Assert(info != null, "chdman info failed");

        Assert(
            info != null && header.Version == (uint)info.Version,
            $"version {header.Version} != chdman {info.Version}"
        );
        Assert(
            header.TotalBytes == info.LogicalBytes,
            $"logical size {header.TotalBytes} != chdman {info.LogicalBytes}"
        );
        Assert(
            header.HunkBytes == info.HunkBytes,
            $"hunk size {header.HunkBytes} != chdman {info.HunkBytes}"
        );
        Assert(
            header.TotalHunks == info.TotalHunks,
            $"hunks {header.TotalHunks} != chdman {info.TotalHunks}"
        );
        Assert(
            header.UnitBytes == info.UnitBytes,
            $"unit size {header.UnitBytes} != chdman {info.UnitBytes}"
        );
        Assert(
            header.UnitCount == info.TotalUnits,
            $"units {header.UnitCount} != chdman {info.TotalUnits}"
        );

        var expectedCompression = ChdmanCodecLabel(header.Compression);
        Assert(
            string.Equals(
                NormalizeChdmanCodec(info.Compression),
                expectedCompression,
                StringComparison.Ordinal
            ),
            $"compression '{info.Compression}' (normalized '{NormalizeChdmanCodec(info.Compression)}') != ours '{expectedCompression}'"
        );

        if (info.Sha1 != null)
        {
            var sha1 =
                header.Sha1 != null && !Util.IsAllZeroArray(header.Sha1)
                    ? Util.ToHex(header.Sha1)
                    : null;
            Assert(
                sha1 != null && string.Equals(sha1, info.Sha1, StringComparison.Ordinal),
                $"combined SHA1 {sha1 ?? "(none)"} != chdman {info.Sha1}"
            );
        }

        if (info.DataSha1 != null)
        {
            var rawSha1 =
                header.RawSha1 != null && !Util.IsAllZeroArray(header.RawSha1)
                    ? Util.ToHex(header.RawSha1)
                    : null;
            Assert(
                rawSha1 != null && string.Equals(rawSha1, info.DataSha1, StringComparison.Ordinal),
                $"raw SHA1 {rawSha1 ?? "(none)"} != chdman {info.DataSha1}"
            );
        }

        if (info.ParentSha1 != null)
        {
            var parentSha1 =
                header.ParentSha1 != null && !Util.IsAllZeroArray(header.ParentSha1)
                    ? Util.ToHex(header.ParentSha1)
                    : null;
            Assert(
                parentSha1 != null
                && string.Equals(parentSha1, info.ParentSha1, StringComparison.Ordinal),
                $"parent SHA1 {parentSha1 ?? "(none)"} != chdman {info.ParentSha1}"
            );
        }
    }

    /// <summary>
    ///     Maps our codec tag names to chdman's info names: chdman prints the tag itself
    ///     ("cdzl (CD Deflate)", "huff (Huffman)"), so the tag string ("cdzl", "huff") is the
    ///     normalized short name. An all-zero slot list (uncompressed CHD) is "none".
    /// </summary>
    internal static string ChdmanCodecLabel(IReadOnlyList<ChdCodec> tags)
    {
        var names = tags.Where(t => (uint)t != 0).Select(t => CodecTags.ToString((uint)t)).ToList();
        if (names.Count == 0)
            return "none";

        return string.Join(",", names);
    }

    /// <summary>Normalizes chdman's info compression text ("zlib (Deflate), zstd (Zstandard)" → "zlib,zstd").</summary>
    internal static string NormalizeChdmanCodec(string text)
    {
        return string.Join(
            ",",
            text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(part => part.Split('(', 2)[0].Trim().ToLowerInvariant())
        );
    }

    private void AddAsset(Asset asset)
    {
        _assets.Add(asset);
        Console.WriteLine($"      asset: {asset.Name} -> {Path.GetFileName(asset.ChdPath)}");
    }
}