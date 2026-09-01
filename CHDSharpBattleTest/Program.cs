using System.Diagnostics;

namespace CHDSharpBattleTest;

/// <summary>
///     CHDSharp battle test: exhaustively cross-checks the CHDSharpLib decoder and the
///     CHDSharp.Encoder encoder against MAME's chdman.exe on a deterministic corpus of raw
///     and CD images. Produces a report and an exit code (0 = all passed).
/// </summary>
internal static class Program
{
    private static void PrintUsage()
    {
        Console.WriteLine(
            """
            CHDSharpBattleTest — battle-test CHDSharp decode/encode against chdman.exe

            Usage: CHDSharpBattleTest [options]

              --chdman <path>   chdman executable (default: repo-root chdman.exe or PATH)
              --cli <path>      CHDSharpCli executable (default: auto-resolve from repo)
              --out <dir>       artifact + report root (default: <repo>/TestResults/battle)
              --real <dir>      scan a folder recursively for real *.chd files and battle-test
                                each one (chdman vs CLI vs library). Repeatable.
              --real-timeout <secs>
                                per-command timeout for real-file checks (default 900s; large
                                CHDs need more time for verify/extract than synthetic ones)
              --quick           reduced corpus (faster smoke battle)
              --seed <n>        RNG seed for the corpus (default 1337)
              --no-keep         delete artifacts at the end when everything passed
              --help            show this help

            Real-corpus battle options (merged from the former CHDBattleTest harness):

              --no-battles      skip the timed decode/encode corpus battles (parity checks only)
              --codec-raw <c>   codec for copy/dvd/hd/raw battles            (default zstd)
              --codec-cd <c>    codec for cd/gd battles                      (default cdzl)
              --workers <n>     -np passed to both tools in encode battles   (default cores)
              --include-av      enable laserdisc extractld/createld battles
              --lib-decode      extra in-process CHDSharpLib decode timing row
              --filter <glob>   corpus file filter                           (default *.chd)
              --min-mb <n>      skip files smaller than n MiB
              --max-mb <n>      skip files larger than n MiB
              --max-files <n>   limit number of corpus files (smallest first)
              --list            classify corpus files only, run no battles
              --resume          skip corpus files already present in results.csv
              --keep-temp       keep per-file corpus battle work directories

            Exit code: 0 when every check passed, 1 when any failed, 2 on usage errors.
            """
        );
    }

    private static int Main(string[] args)
    {
        string? chdmanPath = null;
        string? cliPath = null;
        string? outDir = null;
        var quick = false;
        var noKeep = false;
        var seed = 1337;
        var realDirs = new List<string>();
        var realTimeoutMs = 900_000;
        var corpus = new CorpusOptions();

        for (var i = 0; i < args.Length; i++)
            switch (args[i])
            {
                case "--chdman" when i + 1 < args.Length:
                    chdmanPath = args[++i];
                    break;
                case "--cli" when i + 1 < args.Length:
                    cliPath = args[++i];
                    break;
                case "--out" when i + 1 < args.Length:
                    outDir = args[++i];
                    break;
                case "--real" when i + 1 < args.Length:
                    realDirs.Add(args[++i]);
                    break;
                case "--real-timeout" when i + 1 < args.Length:
                    if (!int.TryParse(args[++i], System.Globalization.CultureInfo.InvariantCulture, out var rt) || rt <= 0)
                    {
                        Console.Error.WriteLine($"Invalid real timeout: {args[i]}");
                        return 2;
                    }

                    realTimeoutMs = rt * 1000;
                    break;
                case "--quick":
                    quick = true;
                    break;
                case "--no-keep":
                    noKeep = true;
                    break;
                case "--seed" when i + 1 < args.Length:
                    if (!int.TryParse(args[++i], System.Globalization.CultureInfo.InvariantCulture, out seed))
                    {
                        Console.Error.WriteLine($"Invalid seed: {args[i]}");
                        return 2;
                    }

                    break;
                case "--no-battles":
                    corpus.Battles = false;
                    break;
                case "--codec-raw" when i + 1 < args.Length:
                    corpus.CodecRaw = args[++i];
                    break;
                case "--codec-cd" when i + 1 < args.Length:
                    corpus.CodecCd = args[++i];
                    break;
                case "--workers" when i + 1 < args.Length:
                    if (!int.TryParse(args[++i], System.Globalization.CultureInfo.InvariantCulture, out var workers) || workers <= 0)
                    {
                        Console.Error.WriteLine($"Invalid workers: {args[i]}");
                        return 2;
                    }

                    corpus.Workers = workers;
                    break;
                case "--include-av":
                    corpus.IncludeAv = true;
                    break;
                case "--lib-decode":
                    corpus.LibDecode = true;
                    break;
                case "--filter" when i + 1 < args.Length:
                    corpus.Filter = args[++i];
                    break;
                case "--min-mb" when i + 1 < args.Length:
                    if (!double.TryParse(args[++i], System.Globalization.CultureInfo.InvariantCulture, out var minMb) || minMb < 0)
                    {
                        Console.Error.WriteLine($"Invalid min-mb: {args[i]}");
                        return 2;
                    }

                    corpus.MinMb = minMb;
                    break;
                case "--max-mb" when i + 1 < args.Length:
                    if (!double.TryParse(args[++i], System.Globalization.CultureInfo.InvariantCulture, out var maxMb) || maxMb <= 0)
                    {
                        Console.Error.WriteLine($"Invalid max-mb: {args[i]}");
                        return 2;
                    }

                    corpus.MaxMb = maxMb;
                    break;
                case "--max-files" when i + 1 < args.Length:
                    if (!int.TryParse(args[++i], System.Globalization.CultureInfo.InvariantCulture, out var maxFiles) || maxFiles <= 0)
                    {
                        Console.Error.WriteLine($"Invalid max-files: {args[i]}");
                        return 2;
                    }

                    corpus.MaxFiles = maxFiles;
                    break;
                case "--list":
                    corpus.ListOnly = true;
                    break;
                case "--resume":
                    corpus.Resume = true;
                    break;
                case "--keep-temp":
                    corpus.KeepTemp = true;
                    break;
                case "--help" or "-h":
                    PrintUsage();
                    return 0;
                default:
                    Console.Error.WriteLine($"Unknown option: {args[i]}");
                    PrintUsage();
                    return 2;
            }

        chdmanPath ??= ResolveChdmanPath();
        if (chdmanPath == null || !File.Exists(chdmanPath))
        {
            Console.Error.WriteLine(
                "chdman.exe not found. Pass --chdman <path> or put chdman.exe in the repo root."
            );
            return 2;
        }

        cliPath ??= ResolveCliPath();
        if (cliPath != null && !File.Exists(cliPath))
        {
            Console.Error.WriteLine($"CHDSharpCli not found at: {cliPath}");
            cliPath = null;
        }

        var sw = Stopwatch.StartNew();
        var harness = new BattleHarness(
            chdmanPath,
            cliPath,
            outDir,
            seed,
            quick,
            realDirs,
            realTimeoutMs,
            corpus
        );
        try
        {
            var failed = harness.Run();
            sw.Stop();
            Console.WriteLine();
            harness.PrintSummary();
            Console.WriteLine(
                $"Battle finished in {sw.Elapsed.TotalSeconds:N1}s. Result: {(failed == 0 ? "ALL PASSED" : $"{failed} FAILED")}"
            );

            if (failed == 0 && noKeep)
                harness.Cleanup();

            return failed == 0 ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Battle aborted: {ex}");
            return 1;
        }
    }

    private static string? ResolveChdmanPath()
    {
        var exeName = OperatingSystem.IsWindows() ? "chdman.exe" : "chdman";

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, exeName);
            if (File.Exists(candidate))
                return candidate;

            dir = dir.Parent;
        }

        var fromEnv = Environment.GetEnvironmentVariable("CHDMAN_PATH");
        if (!string.IsNullOrEmpty(fromEnv) && File.Exists(fromEnv))
            return fromEnv;

        var fromPath = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var p in fromPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(p, exeName);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static string? ResolveCliPath()
    {
        var exeName = OperatingSystem.IsWindows() ? "CHDSharp.exe" : "CHDSharp";

        // Check common build output locations
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            // Check in sibling CHDSharpCli project build output
            var cliDebug = Path.Combine(
                dir.FullName,
                "CHDSharpCli",
                "bin",
                "Debug",
                "net8.0",
                exeName
            );
            if (File.Exists(cliDebug))
                return cliDebug;

            var cliRelease = Path.Combine(
                dir.FullName,
                "CHDSharpCli",
                "bin",
                "Release",
                "net8.0",
                exeName
            );
            if (File.Exists(cliRelease))
                return cliRelease;

            // Check in the same directory (self-contained publish)
            var candidate = Path.Combine(dir.FullName, exeName);
            if (File.Exists(candidate))
                return candidate;

            dir = dir.Parent;
        }

        var fromEnv = Environment.GetEnvironmentVariable("CHDSHARP_CLI_PATH");
        if (!string.IsNullOrEmpty(fromEnv) && File.Exists(fromEnv))
            return fromEnv;

        return null;
    }
}