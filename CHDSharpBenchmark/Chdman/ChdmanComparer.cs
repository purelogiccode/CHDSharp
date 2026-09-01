using System.Diagnostics;
using System.Globalization;
using CHDSharp;
using CHDSharp.Encoder;

namespace CHDSharpBenchmark.Chdman;

/// <summary>
///     External-tool comparison harness (Phase 7.4): runs the stock <c>chdman.exe</c> from MAME
///     side-by-side with the library on the same corpus and the same synthetic inputs, then
///     prints a wall-clock comparison table (median of N runs). Usage:
///     <code>
///         CHDSharpBenchmark --chdman &lt;path-to-chdman.exe&gt; [--corpus &lt;dir&gt;] [--codecs zlib,zstd,...] [--size-mb N]
///         [--runs N]
///     </code>
///     .
///     Every codec the library supports is compared in the pass that fits its device type:
///     HD codecs (zlib/zstd/lzma/huff/flac/none) via <c>createhd</c> on a synthetic image,
///     CD codecs (cdzl/cdlz/cdzs/cdfl) via <c>createcd</c> on a synthetic cue/bin disc, and
///     avhu via <c>createld</c> on a synthetic YUY2/PCM AVI. BenchmarkDotNet covers precise
///     in-process timings; this harness adds the cross-tool comparison the doc asks for
///     (chdman vs. CHDSharp on identical inputs).
/// </summary>
public static class ChdmanComparer
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(30);

    private static readonly string[] HdCodecs = ["zlib", "zstd", "lzma", "huff", "flac", "none"];
    private static readonly string[] CdCodecs = ["cdzl", "cdlz", "cdzs", "cdfl"];
    private static readonly string[] LdCodecs = ["avhu"];

    private static readonly string[] AllCodecs = [.. HdCodecs, .. CdCodecs, .. LdCodecs];

    // Synthetic CD size: ~6000 mode1/2048 data + 4000 audio sectors ≈ 21.7 MB (10k frames).
    private const int CdDataSectors = 6000;
    private const int CdAudioSectors = 4000;

    public static void Run(string chdmanExe, string corpusDir, IReadOnlyList<string> args)
    {
        Corpus.Configure(corpusDir);

        var codecs = ParseCodecs(args);
        var sizeMb = ParseIntArg(args, "--size-mb=", "--size-mb", 64);
        var runs = ParseIntArg(args, "--runs=", "--runs", 3);

        var outDir = Path.Combine(Path.GetTempPath(), $"chdbench_cmp_{Guid.NewGuid():N}");
        Directory.CreateDirectory(outDir);
        try
        {
            Console.WriteLine($"chdman      : {Path.GetFullPath(chdmanExe)}");
            Console.WriteLine($"corpus      : {Corpus.Dir}");
            Console.WriteLine(
                $"synthetic   : {sizeMb} MiB hd image, {CdDataSectors}+{CdAudioSectors}-sector cd, "
                + $"320x240@30 laserdisc, codecs [{string.Join(", ", codecs)}], median of {runs} runs"
            );
            Console.WriteLine();

            var failures = new List<string>();
            var rows = new List<(string Target, string ChdmanMs, string LibMs)>();

            Console.WriteLine("-- VERIFY pass (corpus CHDs; median ms) --");
            foreach (var file in Corpus.ChdFiles().Where(Corpus.IsExpectedOk))
            {
                var name = Path.GetFileName(file);
                var parent = Corpus.ParentFor(file);
                var argsChild = parent != null ? " -ip \"" + parent + "\"" : "";

                var chdmanMs = Time(() => RunVerifyChd(chdmanExe, file, argsChild), runs, failures);
                var libMs = Time(() => RunLibVerify(file, parent), runs, failures);
                rows.Add((name, Ms(chdmanMs), Ms(libMs)));
            }

            RunHdEncodePass(chdmanExe, codecs, outDir, sizeMb, runs, failures, rows);
            RunCdEncodePass(chdmanExe, codecs, outDir, runs, failures, rows);
            RunLdEncodePass(chdmanExe, codecs, outDir, runs, failures, rows);

            Console.WriteLine();
            PrintTable(rows);
            Console.WriteLine();
            if (failures.Count > 0)
            {
                Console.WriteLine("Warnings:");
                foreach (var f in failures.Distinct(StringComparer.Ordinal))
                    Console.WriteLine("  ! " + f);
            }
        }
        finally
        {
            try
            {
                Directory.Delete(outDir, true);
            }
            catch (IOException)
            {
            }
        }
    }

    // ----- encode passes ----------------------------------------------------

    private static void RunHdEncodePass(
        string chdmanExe,
        string[] codecs,
        string outDir,
        int sizeMb,
        int runs,
        List<string> failures,
        List<(string Target, string ChdmanMs, string LibMs)> rows
    )
    {
        var selected = codecs
            .Where(c => HdCodecs.Contains(c, StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (selected.Count == 0)
            return;

        Console.WriteLine();
        Console.WriteLine($"-- ENCODE HD pass (synthetic {sizeMb} MiB image; ms) --");
        var bin = Path.Combine(outDir, "image.bin");
        File.WriteAllBytes(bin, CreateImage(sizeMb * 1024 * 1024));
        foreach (var codec in selected)
        {
            var chdmanOut = Path.Combine(outDir, $"chdman_{codec}.chd");
            var libOut = Path.Combine(outDir, $"lib_{codec}.chd");

            var chdmanMs = Time(
                () => RunChdmanCreateHd(chdmanExe, codec, bin, chdmanOut),
                runs,
                failures
            );
            var libMs = Time(() => RunLibEncode(bin, libOut, codec), runs, failures);
            rows.Add(("createhd -c " + codec, Ms(chdmanMs), Ms(libMs)));
        }
    }

    private static void RunCdEncodePass(
        string chdmanExe,
        string[] codecs,
        string outDir,
        int runs,
        List<string> failures,
        List<(string Target, string ChdmanMs, string LibMs)> rows
    )
    {
        var selected = codecs
            .Where(c => CdCodecs.Contains(c, StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (selected.Count == 0)
            return;

        Console.WriteLine();
        Console.WriteLine(
            $"-- ENCODE CD pass (synthetic {CdDataSectors}+{CdAudioSectors}-sector cue/bin; ms) --"
        );
        var cue = CreateCdSynthetic(outDir);
        foreach (var codec in selected)
        {
            var chdmanOut = Path.Combine(outDir, $"chdman_cd_{codec}.chd");
            var libOut = Path.Combine(outDir, $"lib_cd_{codec}.chd");

            var chdmanMs = Time(
                () => RunChdmanCreateCd(chdmanExe, codec, cue, chdmanOut),
                runs,
                failures
            );
            var libMs = Time(() => RunLibEncodeCd(cue, libOut, codec), runs, failures);
            rows.Add(("createcd -c " + codec, Ms(chdmanMs), Ms(libMs)));
        }
    }

    private static void RunLdEncodePass(
        string chdmanExe,
        string[] codecs,
        string outDir,
        int runs,
        List<string> failures,
        List<(string Target, string ChdmanMs, string LibMs)> rows
    )
    {
        var selected = codecs
            .Where(c => LdCodecs.Contains(c, StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (selected.Count == 0)
            return;

        Console.WriteLine();
        Console.WriteLine("-- ENCODE LD pass (synthetic AVI 320x240@30, 48 frames; ms) --");
        var avi = Path.Combine(outDir, "bench.avi");
        SyntheticAvi.Write(avi, 320, 240, 30, 48, 44100);
        foreach (var _ in selected)
        {
            var chdmanOut = Path.Combine(outDir, "chdman_ld.chd");
            var libOut = Path.Combine(outDir, "lib_ld.chd");

            var chdmanMs = Time(
                () => RunChdmanCreateLd(chdmanExe, avi, chdmanOut),
                runs,
                failures
            );
            var libMs = Time(() => RunLibEncodeLd(avi, libOut), runs, failures);
            rows.Add(("createld (avhu)", Ms(chdmanMs), Ms(libMs)));
        }
    }

    /// <summary>
    ///     Runs an action <c>runs</c> times; returns the median elapsed ms. Exceptions are recorded
    ///     in <paramref name="failures" /> and the median of whatever completed is returned.
    /// </summary>
    private static double Time(Action action, int runs, List<string> failures)
    {
        var samples = new List<double>(runs);
        for (var i = 0; i < runs; i++)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failures.Add(ex.Message.Split('\n', 2)[0]);
                samples.Add(double.NaN);
                continue;
            }
            finally
            {
                sw.Stop();
            }

            samples.Add(sw.Elapsed.TotalMilliseconds);
        }

        var valid = samples.Where(d => !double.IsNaN(d)).ToList();
        if (valid.Count == 0)
            return 0;

        valid.Sort();
        return valid.Count % 2 == 1
            ? valid[valid.Count / 2]
            : (valid[(valid.Count / 2) - 1] + valid[valid.Count / 2]) / 2.0;
    }

    // ----- chdman side --------------------------------------------------------

    private static void RunVerifyChd(string chdmanExe, string file, string extraArgs)
    {
        var rc = RunProcess(chdmanExe, $"verify -i \"{file}\"{extraArgs}");
        if (rc != 0)
        {
            throw new InvalidOperationException(
                $"chdman verify {Path.GetFileName(file)} exit {rc}"
            );
        }
    }

    private static void RunChdmanCreateHd(string chdmanExe, string codec, string bin, string outChd)
    {
        var rc = RunProcess(chdmanExe, $"createhd -c {codec} -i \"{bin}\" -o \"{outChd}\" -f");
        if (rc != 0)
            throw new InvalidOperationException($"chdman createhd -c {codec} exit {rc}");
    }

    private static void RunChdmanCreateCd(string chdmanExe, string codec, string cue, string outChd)
    {
        var rc = RunProcess(chdmanExe, $"createcd -f -i \"{cue}\" -o \"{outChd}\" -c {codec}");
        if (rc != 0)
            throw new InvalidOperationException($"chdman createcd -c {codec} exit {rc}");
    }

    private static void RunChdmanCreateLd(string chdmanExe, string avi, string outChd)
    {
        var rc = RunProcess(chdmanExe, $"createld -f -i \"{avi}\" -o \"{outChd}\"");
        if (rc != 0)
            throw new InvalidOperationException($"chdman createld exit {rc}");
    }

    private static int RunProcess(string exePath, string arguments)
    {
        var psi = new ProcessStartInfo(exePath, arguments)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var proc =
            Process.Start(psi) ?? throw new InvalidOperationException("process start failed");
        proc.OutputDataReceived += (_, _) => { };
        proc.ErrorDataReceived += (_, _) => { };
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        if (!proc.WaitForExit((int)Timeout.TotalMilliseconds))
        {
            try
            {
                proc.Kill(true);
            }
            catch
            {
                // ignored
            }

            throw new InvalidOperationException(
                $"'{Path.GetFileName(exePath)} {arguments}' timed out after {Timeout}"
            );
        }

        return proc.ExitCode;
    }

    // ----- library side -------------------------------------------------------

    private static void RunLibVerify(string file, string? parent)
    {
        var result = Chd.CheckFileWithParent(file, parent);
        if (result.Error != ChdError.Chderrnone)
        {
            throw new InvalidOperationException(
                $"library verify {Path.GetFileName(file)}: {result.Error}"
            );
        }
    }

    private static void RunLibEncode(string bin, string outChd, string codec)
    {
        var tags = ChdCodecs.ParseCodecTags(codec);
        using var src = File.OpenRead(bin);
        ChdEncoder.EncodeRaw(src, outChd, 4096, 512, tags);
    }

    private static void RunLibEncodeCd(string cue, string outChd, string codec)
    {
        var tags = ChdCodecs.ParseCodecTags(codec);
        ChdEncoder.EncodeCd(cue, outChd, codecTags: tags);
    }

    private static void RunLibEncodeLd(string avi, string outChd)
    {
        ChdEncoder.EncodeLaserDisc(avi, outChd);
    }

    // ----- synthetic inputs ---------------------------------------------------

    /// <summary>
    ///     Deterministic mixed-content image (same generator the encode benchmarks use):
    ///     ~50% pseudo-random, ~50% arithmetic runs.
    /// </summary>
    private static byte[] CreateImage(int sizeBytes)
    {
        var rng = new Random(0x5EED);
        var data = new byte[sizeBytes];
        rng.NextBytes(data);
        var half = sizeBytes / 2;
        for (var i = half; i < sizeBytes; i++)
            data[i] = (byte)((i / 97) & 0xFF);

        return data;
    }

    /// <summary>Writes a synthetic cue/bin disc (mode1/2048 data track + audio track).</summary>
    private static string CreateCdSynthetic(string dir)
    {
        var dataBin = Path.Combine(dir, "cd_data.bin");
        var audioBin = Path.Combine(dir, "cd_audio.bin");
        File.WriteAllBytes(dataBin, CreateCdData(CdDataSectors));
        File.WriteAllBytes(audioBin, CreateCdAudio(CdAudioSectors));

        var cue = Path.Combine(dir, "disc.cue");
        File.WriteAllText(
            cue,
            "FILE \"cd_data.bin\" BINARY\n"
            + "  TRACK 01 MODE1/2048\n"
            + "    INDEX 01 00:00:00\n"
            + "FILE \"cd_audio.bin\" BINARY\n"
            + "  TRACK 02 AUDIO\n"
            + "    INDEX 01 00:00:00\n"
        );
        return cue;
    }

    private static byte[] CreateCdData(int sectors)
    {
        var data = new byte[sectors * 2048];
        var rng = new Random(0xC0DECDD);
        rng.NextBytes(data);
        var half = data.Length / 2;
        for (var i = half; i < data.Length; i++)
            data[i] = (byte)((i / 97) & 0xFF);

        return data;
    }

    private static byte[] CreateCdAudio(int sectors)
    {
        var data = new byte[sectors * 2352];
        const int sampleRate = 44100;
        double phase = 0;
        for (var i = 0; i + 1 < data.Length; i += 2)
        {
            // Layered sines sweep the frequency a little so the FLAC audio path has real work.
            var freq = 440 + (i % sampleRate / 100.0);
            phase += 2 * Math.PI * freq / sampleRate;
            var s = (short)((Math.Sin(phase) * 20000) + (Math.Sin(phase * 3) * 3000));
            data[i] = (byte)s;
            data[i + 1] = (byte)(s >> 8);
        }

        return data;
    }

    // ----- output / parsing ---------------------------------------------------

    private static string Ms(double ms)
    {
        return ms >= 999 ? $"{ms / 1000.0:0.000} s" : $"{ms:0.0} ms";
    }

    private static void PrintTable(List<(string name, string chdman, string lib)> rows)
    {
        var widthName = Math.Max(rows.Count == 0 ? 12 : rows.Max(r => r.name.Length), 12);
        var widthCm = Math.Max(rows.Count == 0 ? 12 : rows.Max(r => r.chdman.Length), 12);
        var widthLib = Math.Max(rows.Count == 0 ? 12 : rows.Max(r => r.lib.Length), 12);
        var rule = new string('-', widthName + widthCm + widthLib + 12);
        Console.WriteLine(rule);
        Console.WriteLine(
            $"  {"target".PadRight(widthName)}{"chdman".PadLeft(widthCm + 2)}{"library".PadLeft(widthLib + 2)}"
        );
        Console.WriteLine(rule);
        foreach (var (name, chdman, lib) in rows)
        {
            Console.WriteLine(
                $"  {name.PadRight(widthName)}{(" " + chdman).PadLeft(widthCm + 2)}{(" " + lib).PadLeft(widthLib + 2)}"
            );
        }

        Console.WriteLine(rule);
    }

    private static string[] ParseCodecs(IReadOnlyList<string> args)
    {
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i].StartsWith("--codec=", StringComparison.Ordinal))
            {
                return args[i]
                    ["--codec=".Length..]
                    .Split(
                        ',',
                        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                    );
            }

            if (string.Equals(args[i], "--codecs", StringComparison.Ordinal) && i + 1 < args.Count)
            {
                return args[++i]
                    .Split(
                        ',',
                        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                    );
            }
        }

        return AllCodecs;
    }

    private static int ParseIntArg(IReadOnlyList<string> args, string prefix, string flag, int dflt)
    {
        for (var i = 0; i < args.Count; i++)
        {
            if (
                (
                    args[i].StartsWith(prefix, StringComparison.Ordinal)
                    && int.TryParse(args[i][prefix.Length..], CultureInfo.InvariantCulture, out var n) && n > 0
                )
                || (
                    string.Equals(args[i], flag, StringComparison.Ordinal)
                    && i + 1 < args.Count
                    && int.TryParse(args[i + 1], CultureInfo.InvariantCulture, out n) && n > 0
                )
            )
            {
                return n;
            }
        }

        return dflt;
    }
}
