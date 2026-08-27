using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CHDSharp.Encoder;
using CHDSharp.Utils;
using Serilog;
using Serilog.Extensions.Logging;

// ReSharper disable once CheckNamespace
namespace CHDSharp.Cli;

/// <summary>
///     Command-line entry point for CHDSharp. Provides file verification, random-access testing,
///     CD TOC inspection, CUE sheet generation, CHD classification, parent/child CHD validation,
///     and CHD creation (raw and CUE/BIN CD images).
///     Uses Serilog for console logging throughout.
/// </summary>
internal static class Program
{
    private static int _exitPrompted;

    /// <summary>
    ///     Application entry point. Parses command-line arguments and dispatches to the
    ///     appropriate operation: directory scanning, random-access test, list-based verification,
    ///     parent/child test, TOC dump, CUE sheet generation, CHD classification, or CHD creation.
    /// </summary>
    /// <param name="args">Command-line arguments defining the operation and its parameters.</param>
    private static int Main(string[] args)
    {
        var serilogLogger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console(
                formatProvider: null,
                outputTemplate: "{Message:lj}{NewLine}{Exception}"
            )
            .WriteTo.Sink(new BugReportSink(new EnvironmentSnapshot("CHDSharp")))
            .CreateLogger();

        Log.Logger = serilogLogger;
        Chd.LoggerFactory = new SerilogLoggerFactory(serilogLogger);

        ApplicationStatsService.TrackLaunch("chdsharp");
        VersionCheckService.CheckAndNotify();

        try
        {
            var sw = new Stopwatch();
            sw.Start();

            // Normalize command: support both chdman-style ("info") and legacy-style ("--info")
            var rawCommand = args.Length > 0 ? args[0] : null;
            var command = NormalizeCommand(rawCommand);
            var cmdArgs = args.Length > 1 ? args[1..] : Array.Empty<string>();

            if (command is "help" or null)
            {
                if (cmdArgs.Length > 0)
                    PrintCommandHelp(cmdArgs[0]);
                else
                    PrintUsage();

                WaitForExitIfDoubleClicked();
                return 0;
            }

            switch (command)
            {
                case "random" when cmdArgs.Length < 1:
                    serilogLogger.Warning("random requires a .chd file path");
                    return 1;
                case "random":
                    RandomAccessTest(ParseInput(cmdArgs, 0));
                    serilogLogger.Information("Done:  Time = {Time}", sw.Elapsed.TotalSeconds);
                    return 0;
                case "list" when cmdArgs.Length < 1:
                    serilogLogger.Warning("list requires a text file of .chd paths");
                    return 1;
                case "list":
                    VerifyList(ParseInput(cmdArgs, 0));
                    serilogLogger.Information("Done:  Time = {Time}", sw.Elapsed.TotalSeconds);
                    return 0;
                case "parent" when cmdArgs.Length < 2:
                    serilogLogger.Warning("parent requires <child.chd> <parent.chd>");
                    return 1;
                case "parent":
                    ParentTest(ParseInput(cmdArgs, 0), ParseInput(cmdArgs, 1));
                    serilogLogger.Information("Done:  Time = {Time}", sw.Elapsed.TotalSeconds);
                    return 0;
                case "toc" when cmdArgs.Length < 1:
                    serilogLogger.Warning("toc requires a .chd file path");
                    return 1;
                case "toc":
                    TocTest(ParseInput(cmdArgs, 0));
                    serilogLogger.Information("Done:  Time = {Time}", sw.Elapsed.TotalSeconds);
                    return 0;
                case "cue" when cmdArgs.Length < 1:
                    serilogLogger.Warning("cue requires a .chd file path");
                    return 1;
                case "cue":
                    CueTest(
                        ParseInput(cmdArgs, 0),
                        cmdArgs.Length >= 2 ? ParseInput(cmdArgs, 1) : null
                    );
                    serilogLogger.Information("Done:  Time = {Time}", sw.Elapsed.TotalSeconds);
                    return 0;
                case "classify" when cmdArgs.Length < 1:
                    serilogLogger.Warning("classify requires a .chd file path");
                    return 1;
                case "classify":
                    ClassifyTest(ParseInput(cmdArgs, 0));
                    serilogLogger.Information("Done:  Time = {Time}", sw.Elapsed.TotalSeconds);
                    return 0;
                case "createraw" or "create":
                {
                    var (inp, outp, rest) = ParseCreateArgs(cmdArgs);
                    if (inp == null || outp == null)
                    {
                        serilogLogger.Warning(
                            "createraw requires --input <file> --output <file> (or positional args)"
                        );
                        return 1;
                    }

                    CreateRawTest(inp, outp, rest);
                    serilogLogger.Information("Done:  Time = {Time}", sw.Elapsed.TotalSeconds);
                    return 0;
                }
                case "createcd":
                {
                    var (inp, outp, rest) = ParseCreateArgs(cmdArgs);
                    if (inp == null || outp == null)
                    {
                        serilogLogger.Warning(
                            "createcd requires --input <file> --output <file> (or positional args)"
                        );
                        return 1;
                    }

                    CreateCdTest(inp, outp, rest);
                    serilogLogger.Information("Done:  Time = {Time}", sw.Elapsed.TotalSeconds);
                    return 0;
                }
                case "createhd":
                {
                    var (inp, outp, rest) = ParseCreateArgs(cmdArgs);
                    if (outp == null)
                    {
                        serilogLogger.Warning("createhd requires --output <file>");
                        return 1;
                    }

                    CreateHdTest(inp, outp, rest);
                    serilogLogger.Information("Done:  Time = {Time}", sw.Elapsed.TotalSeconds);
                    return 0;
                }
                case "createdvd":
                {
                    var (inp, outp, rest) = ParseCreateArgs(cmdArgs);
                    if (inp == null || outp == null)
                    {
                        serilogLogger.Warning(
                            "createdvd requires --input <file> --output <file> (or positional args)"
                        );
                        return 1;
                    }

                    CreateDvdTest(inp, outp, rest);
                    serilogLogger.Information("Done:  Time = {Time}", sw.Elapsed.TotalSeconds);
                    return 0;
                }
                case "createld":
                {
                    var (inp, outp, rest) = ParseCreateArgs(cmdArgs);
                    if (inp == null || outp == null)
                    {
                        serilogLogger.Warning(
                            "createld requires --input <file> --output <file> (or positional args)"
                        );
                        return 1;
                    }

                    CreateLdTest(inp, outp, rest);
                    serilogLogger.Information("Done:  Time = {Time}", sw.Elapsed.TotalSeconds);
                    return 0;
                }
                case "extractraw" or "extracthd" or "extractdvd":
                {
                    var (inp, outp, rest) = ParseCreateArgs(cmdArgs);
                    if (inp == null || outp == null)
                    {
                        serilogLogger.Warning(
                            "{Command} requires --input <file> --output <file>",
                            command
                        );
                        return 1;
                    }

                    ExtractRawTest(inp, outp, rest);
                    serilogLogger.Information("Done:  Time = {Time}", sw.Elapsed.TotalSeconds);
                    return 0;
                }
                case "extractcd":
                {
                    var (inp, outp, rest) = ParseCreateArgs(cmdArgs);
                    if (inp == null || outp == null)
                    {
                        serilogLogger.Warning("extractcd requires --input <file> --output <file>");
                        return 1;
                    }

                    ExtractCdTest(inp, outp, rest);
                    serilogLogger.Information("Done:  Time = {Time}", sw.Elapsed.TotalSeconds);
                    return 0;
                }
                case "extractld":
                {
                    var (inp, outp, rest) = ParseCreateArgs(cmdArgs);
                    if (inp == null || outp == null)
                    {
                        serilogLogger.Warning("extractld requires --input <file> --output <file>");
                        return 1;
                    }

                    ExtractLdTest(inp, outp, rest);
                    serilogLogger.Information("Done:  Time = {Time}", sw.Elapsed.TotalSeconds);
                    return 0;
                }
                case "listtemplates":
                    ListTemplates();
                    return 0;
                case "copy":
                {
                    var (inp, outp, rest) = ParseCreateArgs(cmdArgs);
                    if (inp == null || outp == null)
                    {
                        serilogLogger.Warning(
                            "copy requires --input <file> --output <file> (or positional args)"
                        );
                        return 1;
                    }

                    CopyTest(inp, outp, rest);
                    serilogLogger.Information("Done:  Time = {Time}", sw.Elapsed.TotalSeconds);
                    return 0;
                }
                case "verify" when cmdArgs.Length < 1:
                    serilogLogger.Warning("verify requires a .chd file path");
                    return 1;
                case "verify":
                    var verified = VerifyTest(ParseInput(cmdArgs, 0), cmdArgs.Skip(1).ToArray());
                    serilogLogger.Information("Done:  Time = {Time}", sw.Elapsed.TotalSeconds);
                    return verified ? 0 : 1;
                case "info" when cmdArgs.Length < 1:
                    serilogLogger.Warning("info requires a .chd file path");
                    return 1;
                case "info":
                    InfoTest(ParseInput(cmdArgs, 0), cmdArgs.Skip(1).ToArray());
                    serilogLogger.Information("Done:  Time = {Time}", sw.Elapsed.TotalSeconds);
                    return 0;
                case "detect" when cmdArgs.Length < 1:
                    serilogLogger.Warning("detect requires a file path");
                    return 1;
                case "detect":
                    DetectTest(ParseInput(cmdArgs, 0));
                    serilogLogger.Information("Done:  Time = {Time}", sw.Elapsed.TotalSeconds);
                    return 0;
                case "dumpmeta":
                    DumpMetaTest(cmdArgs);
                    serilogLogger.Information("Done:  Time = {Time}", sw.Elapsed.TotalSeconds);
                    return 0;
                case "hash":
                    HashTest(cmdArgs);
                    serilogLogger.Information("Done:  Time = {Time}", sw.Elapsed.TotalSeconds);
                    return 0;
                case "batch":
                {
                    var (inp, outp, rest) = ParseCreateArgs(cmdArgs);
                    if (inp == null || outp == null)
                    {
                        serilogLogger.Warning("batch requires --input <dir> --output <dir>");
                        return 1;
                    }

                    BatchTest(inp, outp, rest);
                    serilogLogger.Information("Done:  Time = {Time}", sw.Elapsed.TotalSeconds);
                    return 0;
                }
                case "addmeta":
                    AddMetaTest(cmdArgs);
                    serilogLogger.Information("Done:  Time = {Time}", sw.Elapsed.TotalSeconds);
                    return 0;
                case "delmeta":
                    DeleteMetaTest(cmdArgs);
                    serilogLogger.Information("Done:  Time = {Time}", sw.Elapsed.TotalSeconds);
                    return 0;
            }

            // Fallback: treat args as directories for recursive verification
            var dirArgs = new[] { command }.Concat(cmdArgs);
            foreach (var arg in dirArgs)
            {
                var sDir = arg.Replace("\"", "");
                if (!Directory.Exists(sDir))
                {
                    serilogLogger.Warning("Directory not found: {Path}", sDir);
                    continue;
                }

                var di = new DirectoryInfo(sDir);
                Checkdir(di);
            }

            serilogLogger.Information("Done:  Time = {Time}", sw.Elapsed.TotalSeconds);
            return 0;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Unhandled exception in CHDSharp");
            return 3;
        }
        finally
        {
            Log.CloseAndFlush();
            WaitForExitIfDoubleClicked();
        }
    }

    /// <summary>
    ///     Verifies a child (differential) CHD file against its parent.
    ///     Opens the child with its parent, reads sample hunks, and runs
    ///     <see
    ///         cref="Chd.CheckFileWithParent(string, string?, IProgress{CHDSharp.Models.ChdProgress}?, System.Threading.CancellationToken)" />
    ///     .
    /// </summary>
    /// <param name="childPath">Path to the child CHD file.</param>
    /// <param name="parentPath">Path to the parent CHD file.</param>
    private static void ParentTest(string childPath, string parentPath)
    {
        var log = Log.Logger;
        log.Information("Child:  {Name}", Path.GetFileName(childPath));
        log.Information("Parent: {Name}", Path.GetFileName(parentPath));

        var err = ChdFile.Open(childPath, parentPath, out var chd);
        if (err != ChdError.Chderrnone)
        {
            log.Warning("  Open(child, parent) => {Error}", err);
            return;
        }

        using (chd)
        {
            if (chd != null)
            {
                log.Information("  Opened {Info}", chd.ToString());
                log.Information(
                    "  IsChild={IsChild}, Metadata entries={Count}",
                    chd.IsChild,
                    chd.Metadata.Count
                );
                foreach (var meta in chd.Metadata)
                    log.Information("    {Meta}", meta.ToString());

                var hbuf = new byte[chd.HunkBytes];
                var probes =
                    chd.HunkCount <= 1
                        ? new uint[] { 0 }
                        : new uint[] { 0, chd.HunkCount / 2, chd.HunkCount - 1 };
                foreach (var h in probes)
                {
                    err = chd.ReadHunk(h, hbuf);
                    log.Information("  ReadHunk({Hunk}) => {Error}", h, err);
                    if (err != ChdError.Chderrnone)
                        return;
                }
            }
        }

        var result = Chd.CheckFileWithParent(childPath, parentPath);
        log.Information(
            "  CheckFileWithParent => {Error}  (V{Version}, sha1={Sha1})",
            result.Error,
            result.Version,
            result.Sha1Hex
        );

        var noParent = ChdFile.Open(childPath, out var tmp);
        tmp?.Dispose();
        log.Information(
            "  Open(child, no parent) => {Error}  (expected CHDERR_REQUIRES_PARENT if this is a child)",
            noParent
        );
    }

    /// <summary>
    ///     Verifies all CHD files listed in a text file (one path per line).
    ///     Each file is fully decompressed and verified using
    ///     <see
    ///         cref="Chd.CheckFile(Stream, string, bool, IProgress{CHDSharp.Models.ChdProgress}?, System.Threading.CancellationToken)" />
    ///     .
    /// </summary>
    /// <param name="listFile">Path to a text file containing one CHD path per line.</param>
    private static void VerifyList(string listFile)
    {
        var log = Log.Logger;
        string[] lines;
        try
        {
            lines = File.ReadAllLines(listFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Warning("Cannot read list file {Path}: {Message}", listFile, ex.Message);
            return;
        }

        int pass = 0,
            fail = 0,
            skip = 0;
        var failures = new List<string>();

        foreach (var raw in lines)
        {
            var path = raw.Trim().Trim('"');
            if (path.Length == 0)
                continue;

            var name = Path.GetFileName(path);
            if (!File.Exists(path))
            {
                log.Information("[SKIP] {Name}  (not found)", name);
                skip++;
                continue;
            }

            var fileSw = Stopwatch.StartNew();
            ChdResult result;
            var lastPercent = -1;
            try
            {
                using Stream s = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    128 * 4096
                );
                var progress = new Progress<ChdProgress>(p =>
                {
                    var pct = (int)p.Percent / 10 * 10;
                    var prev = Interlocked.Exchange(ref lastPercent, pct);
                    if (pct != prev)
                        log.Information(
                            "   {Pct,3}% {Name}  ({Bytes:N0} / {Total:N0} bytes, {Elapsed:N1}s)",
                            pct,
                            name,
                            p.BytesProcessed,
                            p.TotalBytes,
                            p.Elapsed.TotalSeconds
                        );
                });
                result = Chd.CheckFile(s, name, true, progress);
            }
            catch (Exception ex)
            {
                result = new ChdResult(ChdError.Chderrdecompressionerror, null, null, null);
                log.Warning("       exception ({Type}): {Message}", ex.GetType().Name, ex.Message);
            }

            fileSw.Stop();

            if (result.IsSuccess)
            {
                log.Information(
                    "[PASS] V{Version} {Name}  sha1={Sha1}  ({Time:N1}s)",
                    result.Version,
                    name,
                    result.Sha1Hex,
                    fileSw.Elapsed.TotalSeconds
                );
                pass++;
            }
            else
            {
                log.Information(
                    "[FAIL] {Name}  {Result}  ({Time:N1}s)",
                    name,
                    result.Error,
                    fileSw.Elapsed.TotalSeconds
                );
                failures.Add($"{name}: {result.Error.GetMessage()}");
                fail++;
            }
        }

        log.Information("");
        log.Information(
            "==== Summary: {Pass} passed, {Fail} failed, {Skip} skipped, {Total} total ====",
            pass,
            fail,
            skip,
            pass + fail + skip
        );
        foreach (var f in failures)
            log.Information("  FAIL: {Failure}", f);
    }

    /// <summary>
    ///     Performs a random-access read test on a single CHD file.
    ///     Reads sample hunks (first, middle, last) and computes the full-image raw SHA1 and MD5
    ///     to compare against the hashes stored in the CHD header.
    /// </summary>
    /// <param name="file">Path to the CHD file to test.</param>
    private static void RandomAccessTest(string file)
    {
        var log = Log.Logger;
        var err = ChdFile.Open(file, out var chd);
        if (err != ChdError.Chderrnone)
        {
            log.Warning("Open failed: {Error}", err);
            return;
        }

        using (chd)
        {
            if (chd == null)
                return;

            log.Information("Opened {Info}", chd.ToString());
            log.Information(
                "  IsChild={IsChild}, Metadata entries={Count}",
                chd.IsChild,
                chd.Metadata.Count
            );
            foreach (var meta in chd.Metadata)
                log.Information("    {Meta}", meta.ToString());

            var hbuf = new byte[chd.HunkBytes];
            var probes =
                chd.HunkCount <= 1
                    ? new uint[] { 0 }
                    : new uint[] { 0, chd.HunkCount / 2, chd.HunkCount - 1 };
            foreach (var h in probes)
            {
                err = chd.ReadHunk(h, hbuf);
                log.Information("  ReadHunk({Hunk}) => {Error}", h, err);
                if (err != ChdError.Chderrnone)
                    return;
            }

            var expectedSha1 = chd.RawSha1;
            var expectedMd5 = chd.Md5;
            var haveSha1 = !Util.IsAllZeroArray(expectedSha1);
            var haveMd5 = !Util.IsAllZeroArray(expectedMd5);

            if (!haveSha1 && !haveMd5)
            {
                log.Information(
                    "  No raw-data hash stored in header; skipping full-image validation."
                );
                return;
            }

            using var sha1 = haveSha1 ? SHA1.Create() : null;
            using var md5 = haveMd5 ? MD5.Create() : null;
            var buf = new byte[chd.HunkBytes];
            var remaining = chd.TotalBytes;
            ulong offset = 0;
            while (remaining > 0)
            {
                var chunk = (int)Math.Min((ulong)buf.Length, remaining);
                err = chd.Read(offset, buf, 0, chunk);
                if (err != ChdError.Chderrnone)
                {
                    log.Warning("  Read(offset={Offset}) => {Error}", offset, err);
                    return;
                }

                sha1?.TransformBlock(buf, 0, chunk, null, 0);
                md5?.TransformBlock(buf, 0, chunk, null, 0);
                offset += (ulong)chunk;
                remaining -= (ulong)chunk;
            }

            sha1?.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            md5?.TransformFinalBlock(Array.Empty<byte>(), 0, 0);

            if (haveSha1)
            {
                var match = sha1 is { Hash: not null } && ByteEquals(sha1.Hash, expectedSha1);
                log.Information(
                    "  Full-image raw SHA1 {Result} header raw SHA1",
                    match ? "MATCHES" : "DIFFERS from"
                );
                if (sha1 is { Hash: not null })
                    log.Information("    computed: {Hash}", Util.ToHex(sha1.Hash));
                log.Information("    header:   {Hash}", Util.ToHex(expectedSha1));
            }

            if (haveMd5)
            {
                var match = md5 is { Hash: not null } && ByteEquals(md5.Hash, expectedMd5);
                log.Information(
                    "  Full-image MD5 {Result} header MD5",
                    match ? "MATCHES" : "DIFFERS from"
                );
                if (md5 is { Hash: not null })
                    log.Information("    computed: {Hash}", Util.ToHex(md5.Hash));
                log.Information("    header:   {Hash}", Util.ToHex(expectedMd5));
            }
        }
    }

    /// <summary>
    ///     Compares two byte arrays for equality.
    /// </summary>
    /// <param name="a">The first byte array.</param>
    /// <param name="b">The second byte array.</param>
    /// <returns><c>true</c> if the arrays have identical length and content; otherwise <c>false</c>.</returns>
    private static bool ByteEquals(byte[]? a, byte[]? b)
    {
        if (a == null && b == null)
            return true;
        if (a == null || b == null)
            return false;
        if (a.Length != b.Length)
            return false;

        for (var i = 0; i < a.Length; i++)
            if (a[i] != b[i])
                return false;

        return true;
    }

    /// <summary>
    ///     Recursively scans a directory for <c>*.chd</c> files and runs
    ///     <see
    ///         cref="Chd.CheckFile(Stream, string, bool, IProgress{CHDSharp.Models.ChdProgress}?, System.Threading.CancellationToken)" />
    ///     on each one found.
    /// </summary>
    /// <param name="di">The directory to scan.</param>
    private static void Checkdir(DirectoryInfo di)
    {
        FileInfo[] fi;
        try
        {
            fi = di.GetFiles(
                    "*.chd",
                    new EnumerationOptions
                    {
                        IgnoreInaccessible = true,
                        RecurseSubdirectories = false,
                        AttributesToSkip = FileAttributes.ReparsePoint
                    }
                )
                .Where(f => f.Extension.Equals(".chd", StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }
        catch (UnauthorizedAccessException ex)
        {
            Log.Logger.Warning("Access denied listing {Dir}: {Message}", di.FullName, ex.Message);
            return;
        }
        catch (DirectoryNotFoundException)
        {
            return;
        }

        foreach (var f in fi)
            try
            {
                var lastPercent = -1;
                using Stream s = new FileStream(
                    f.FullName,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    128 * 4096
                );
                var progress = new Progress<ChdProgress>(p =>
                {
                    var pct = (int)p.Percent / 10 * 10;
                    var prev = Interlocked.Exchange(ref lastPercent, pct);
                    if (pct != prev)
                        Log.Logger.Information(
                            "   {Pct,3}% {Name}  ({Bytes:N0} / {Total:N0} bytes, {Elapsed:N1}s)",
                            pct,
                            f.Name,
                            p.BytesProcessed,
                            p.TotalBytes,
                            p.Elapsed.TotalSeconds
                        );
                });
                Chd.CheckFile(s, f.Name, true, progress);
            }
            catch (Exception ex)
            {
                Log.Logger.Warning("[FAIL] {Name}: {Message}", f.Name, ex.Message);
            }

        DirectoryInfo[] arrdi;
        try
        {
            arrdi = di.GetDirectories();
        }
        catch (UnauthorizedAccessException ex)
        {
            Log.Logger.Warning(
                "Access denied listing subdirs of {Dir}: {Message}",
                di.FullName,
                ex.Message
            );
            return;
        }

        foreach (var d in arrdi)
            Checkdir(d);
    }

    /// <summary>
    ///     Opens a CHD file and prints its table of contents (track layout) to the console.
    /// </summary>
    /// <param name="file">Path to the CHD file.</param>
    private static void TocTest(string file)
    {
        var log = Log.Logger;
        var err = ChdFile.Open(file, out var chd);
        if (err != ChdError.Chderrnone)
        {
            log.Warning("Open failed: {Error}", err);
            return;
        }

        using (chd)
        {
            if (chd == null)
                return;

            log.Information("{Toc}", chd.ExportToc());
        }
    }

    /// <summary>
    ///     Opens a CD CHD file and generates a CUE sheet, printing it to the console.
    /// </summary>
    /// <param name="file">Path to the CHD file.</param>
    /// <param name="binFileName">
    ///     Optional target bin file name for the CUE sheet. Defaults to the CHD filename with a .bin
    ///     extension.
    /// </param>
    private static void CueTest(string file, string? binFileName)
    {
        var log = Log.Logger;
        var err = ChdFile.Open(file, out var chd);
        if (err != ChdError.Chderrnone)
        {
            log.Warning("Open failed: {Error}", err);
            return;
        }

        using (chd)
        {
            if (chd == null)
                return;

            binFileName ??= Path.GetFileNameWithoutExtension(file) + ".bin";
            try
            {
                log.Information("{Cue}", chd.GenerateCueSheet(binFileName));
            }
            catch (InvalidOperationException ex)
            {
                log.Warning("CUE generation failed: {Message}", ex.Message);
            }
        }
    }

    /// <summary>
    ///     Opens a CHD file and classifies its media type (cd, dvd, hdd, or gd-rom).
    ///     Prints the classification to the console.
    /// </summary>
    /// <param name="file">Path to the CHD file.</param>
    private static void ClassifyTest(string file)
    {
        var log = Log.Logger;
        var err = Chd.Classify(file, out var classification);
        if (err != ChdError.Chderrnone)
        {
            log.Warning("Classify failed: {Error}", err);
            return;
        }

        log.Information(
            "{File}: {Classification}",
            Path.GetFileName(file),
            classification ?? "unknown/raw"
        );
    }

    /// <summary>
    ///     Creates a CHD from a raw binary file and verifies the result with a deep
    ///     CHDSharpLib check.
    /// </summary>
    /// <param name="inputPath">Path to the raw input file.</param>
    /// <param name="outputPath">Path of the output .chd file.</param>
    /// <param name="options">Optional <c>-c</c> codec list, <c>-hs</c> hunk size and <c>-us</c> unit size arguments.</param>
    private static void CreateRawTest(string inputPath, string outputPath, string[] options)
    {
        var log = Log.Logger;
        if (!File.Exists(inputPath))
        {
            log.Warning("--create: input file not found: {Path}", inputPath);
            return;
        }

        var hunkBytes = 4096u;
        var unitBytes = 512u;
        string? codecs = null;
        string? parentPath = null;
        var verbose = false;
        var dvd = false;
        var force = false;
        int? taskCount = null;
        int? templateId = null;
        long? inputStartBytes = null;
        long? inputLengthBytes = null;
        long? inputStartHunk = null;
        long? inputLengthHunks = null;
        long? inputStartFrame = null;
        long? inputLengthFrames = null;
        if (
            !TryParseOptions(
                options,
                ref hunkBytes,
                ref unitBytes,
                ref codecs,
                ref parentPath,
                ref verbose,
                ref taskCount,
                ref dvd,
                ref templateId,
                ref inputStartBytes,
                ref inputLengthBytes,
                ref force,
                ref inputStartHunk,
                ref inputLengthHunks,
                ref inputStartFrame,
                ref inputLengthFrames
            )
        )
            return;

        if (File.Exists(outputPath) && !force)
        {
            log.Warning(
                "Output file already exists: {Path} (use --force to overwrite)",
                outputPath
            );
            return;
        }

        if (templateId.HasValue)
        {
            if (dvd)
            {
                log.Warning("--create: -tp and -d are mutually exclusive");
                return;
            }

            var tpl = HardDiskTemplates.GetTemplate(templateId.Value);
            if (unitBytes != 512u)
                log.Warning(
                    "  --unitsize/-us overridden by template {Id} (was {Old}, now {New})",
                    templateId.Value,
                    unitBytes,
                    tpl.SectorSize
                );
            if (hunkBytes != 4096u)
                log.Warning(
                    "  --hunksize/-hs overridden by template {Id} (was {Old}, now {New})",
                    templateId.Value,
                    hunkBytes,
                    Math.Max(4096u / tpl.SectorSize * tpl.SectorSize, tpl.SectorSize)
                );
            unitBytes = tpl.SectorSize;
            hunkBytes = Math.Max(4096u / tpl.SectorSize * tpl.SectorSize, tpl.SectorSize);
            log.Information(
                "  Using template {Id}: {Manufacturer} {Model} ({Cylinders}C/{Heads}H/{Sectors}S, {Size} MB)",
                templateId.Value,
                tpl.Manufacturer,
                tpl.Model,
                tpl.Cylinders,
                tpl.Heads,
                tpl.Sectors,
                tpl.TotalMb
            );
        }

        // chdman.cpp:1892 — createraw requires unitsize when no parent
        var hunkExplicit = options.Contains("--hunksize") || options.Contains("-hs");
        var unitExplicit = options.Contains("--unitsize") || options.Contains("-us");
        // --dvd forces 2048-byte units (like createdvd), so unitsize is considered supplied when -d is set
        if (dvd && !unitExplicit)
            unitExplicit = true;

        if (!unitExplicit && parentPath == null)
        {
            log.Warning("createraw: unit size must be specified if no output parent is supplied (--unitsize/-us)");
            return;
        }

        // dvd overrides unit size before hunk default is computed (parse_hunk_size granularity)
        if (dvd && unitBytes == 512u)
            unitBytes = 2048u;

        // chdman.cpp:1331 parse_hunk_size — default = max(4096/unit*unit, unit); parent inherits when omitted
        if (!hunkExplicit)
        {
            if (parentPath != null && File.Exists(parentPath))
            {
                var perr = Chd.ReadHeader(parentPath, out var phdr);
                if (perr == ChdError.Chderrnone && phdr != null && phdr.HunkBytes != 0)
                    hunkBytes = phdr.HunkBytes;
                else
                    hunkBytes = Math.Max(4096u / unitBytes * unitBytes, unitBytes);
            }
            else
            {
                hunkBytes = Math.Max(4096u / unitBytes * unitBytes, unitBytes);
            }
        }

        // chdman.cpp:61 HUNK_SIZE_MIN/MAX
        if (hunkBytes < 16)
        {
            log.Warning("Invalid hunk size {Hunk} (minimum 16)", hunkBytes);
            return;
        }

        if (hunkBytes > 1024 * 1024)
        {
            log.Warning("Invalid hunk size {Hunk} (maximum 1048576)", hunkBytes);
            return;
        }

        // chdman.cpp:1354 granularity check is done by ChdEncoder.ValidateHunkSize; keep message parity here
        if (hunkBytes % unitBytes != 0)
        {
            log.Warning("Hunk size {Hunk} bytes is not a whole multiple of {Unit}", hunkBytes, unitBytes);
            return;
        }

        // -c auto: detect the platform and pick the smart codec preset (CHDlite parity).
        if (string.Equals(codecs, "auto", StringComparison.OrdinalIgnoreCase))
        {
            var detected = PlatformDetector.Detect(inputPath);
            // 2048-byte-sector images (.iso / raw DVD) use the DVD presets; CD images use the CD presets.
            var format =
                detected.Platform == DiscPlatform.Dvd
                || (
                    detected.Platform == DiscPlatform.Ps2
                    && inputPath.EndsWith(".iso", StringComparison.OrdinalIgnoreCase)
                )
                    ? "dvd"
                    : "cd";
            var preset = PlatformDetector.AutoCodecs(detected.Platform, format);
            codecs = preset != null ? string.Join(",", preset.Select(CodecTags.ToString)) : "zlib";
            log.Information("  Detected {Platform}; using codecs {Codecs}", detected, codecs);
        }

        // chdman.cpp:665 s_default_raw_compression = lzma,zlib,huff,flac (also for createdvd with input)
        codecs ??= "lzma,zlib,huff,flac";

        try
        {
            var codecTags = ChdCodecs.ParseCodecTags(codecs);
            log.Information(
                "Creating CHD: {Input} -> {Output}  (hunk {Hunk}B, unit {Unit}B, codecs {Codecs}{Parent}{Tasks})",
                Path.GetFileName(inputPath),
                outputPath,
                hunkBytes,
                unitBytes,
                string.Join(",", codecTags.Select(CodecTags.ToString)),
                parentPath != null ? $", parent {Path.GetFileName(parentPath)}" : "",
                taskCount.HasValue ? $", {taskCount} tasks" : ""
            );
            var logger = verbose ? new VerboseHunkLogger() : null;
            var encodeOptions = logger?.Options;
            if (
                encodeOptions == null
                && (
                    taskCount.HasValue
                    || parentPath != null
                    || dvd
                    || templateId.HasValue
                    || inputStartBytes.HasValue
                    || inputLengthBytes.HasValue
                )
            )
                encodeOptions = new ChdEncodeOptions();

            if (encodeOptions != null)
            {
                if (taskCount.HasValue)
                    encodeOptions.TaskCount = taskCount;

                if (parentPath != null)
                    encodeOptions.ParentPath = parentPath;

                if (inputStartBytes.HasValue)
                    encodeOptions.InputStartBytes = inputStartBytes.Value;

                if (inputLengthBytes.HasValue)
                    encodeOptions.InputLengthBytes = inputLengthBytes.Value;

                if (dvd)
                {
                    // --dvd (createdvd parity): force 'DVD ' metadata and a 2048-byte unit size.
                    encodeOptions.Metadata = [MetadataWriter.BuildDvdMetadata()];
                    if (unitBytes != 2048u && unitBytes != 512u)
                        log.Warning(
                            "  --unitsize/-us overridden by --dvd (was {Old}, now 2048)",
                            unitBytes
                        );
                    unitBytes = 2048;
                }
                else if (templateId.HasValue)
                {
                    var tpl = HardDiskTemplates.GetTemplate(templateId.Value);
                    encodeOptions.Metadata =
                    [
                        MetadataWriter.BuildHardDiskMetadata(
                            tpl.Cylinders,
                            tpl.Heads,
                            tpl.Sectors,
                            tpl.SectorSize
                        )
                    ];
                }
            }

            ChdEncoder.EncodeRaw(
                inputPath,
                outputPath,
                hunkBytes,
                unitBytes,
                codecTags,
                encodeOptions
            );
            logger?.LogSummary();
            log.Information("  Created {Size:N0} bytes", new FileInfo(outputPath).Length);
            VerifyResultChd(outputPath, parentPath);
        }
        catch (Exception ex)
            when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            log.Warning("--create failed: {Message}", ex.Message);
        }
    }

    /// <summary>
    ///     Creates a blank, zero-filled hard disk CHD without reading from an input file.
    ///     Equivalent to chdman <c>createhd --size</c>.
    /// </summary>
    /// <param name="inputPath">Path of the input raw file, or <c>null</c> to create a blank image.</param>
    /// <param name="outputPath">Path of the output .chd file.</param>
    /// <param name="options">
    ///     Command-line options: <c>--size N</c> (required), <c>-chs C,H,S</c>,
    ///     <c>-ss N</c> sector size, <c>-c</c> codecs, <c>-hs</c> hunk size, <c>-us</c> unit size,
    ///     <c>-np</c> task count, <c>-v</c> verbose, <c>-op</c> output parent, <c>-isb</c> input start byte,
    ///     <c>-ib</c> input bytes, <c>-ish</c> input start hunk, <c>-ih</c> input hunks.
    /// </param>
    private static void CreateHdTest(string? inputPath, string outputPath, string[] options)
    {
        var log = Log.Logger;

        // Parse --createhd-specific options
        ulong? sizeBytes = null;
        uint? chsCylinders = null;
        uint? chsHeads = null;
        uint? chsSectors = null;
        uint hunkBytes = 4096;
        uint unitBytes = 512;
        string? codecs = null;
        string? outputParentPath = null;
        var verbose = false;
        var force = false;
        int? taskCount = null;
        string? identPath = null;
        int? templateId = null;
        long? inputStartBytes = null;
        long? inputLengthBytes = null;
        long? inputStartHunk = null;
        long? inputLengthHunks = null;

        for (var i = 0; i < options.Length; i++)
            switch (options[i])
            {
                case "--size" or "-s" when i + 1 < options.Length:
                    if (!TryParseSizeWithSuffix(options[++i], out long sz) || sz <= 0)
                    {
                        log.Warning("--createhd: invalid size: {Value}", options[i]);
                        return;
                    }

                    sizeBytes = (ulong)sz;
                    break;
                case "--chs" or "-chs" when i + 1 < options.Length:
                    var chsParts = options[++i].Split(',');
                    if (
                        chsParts.Length != 3
                        || !uint.TryParse(chsParts[0], out var c)
                        || c == 0
                        || !uint.TryParse(chsParts[1], out var h)
                        || h == 0
                        || !uint.TryParse(chsParts[2], out var s)
                        || s == 0
                    )
                    {
                        log.Warning(
                            "createhd: invalid CHS geometry (expected C,H,S): {Value}",
                            options[i]
                        );
                        return;
                    }

                    chsCylinders = c;
                    chsHeads = h;
                    chsSectors = s;
                    break;
                case "--template" or "-tp" when i + 1 < options.Length:
                    if (
                        !int.TryParse(options[++i], out var tp)
                        || tp < 0
                        || tp >= HardDiskTemplates.Templates.Length
                    )
                    {
                        log.Warning(
                            "createhd: invalid template ID (0-{Max}): {Value}",
                            HardDiskTemplates.Templates.Length - 1,
                            options[i]
                        );
                        return;
                    }

                    templateId = tp;
                    break;
                case "--sectorsize" or "-ss" when i + 1 < options.Length:
                    if (!TryParseSizeWithSuffix(options[++i], out uint ss) || ss == 0)
                    {
                        log.Warning("--createhd: invalid sector size: {Value}", options[i]);
                        return;
                    }

                    unitBytes = ss;
                    break;
                case "--compression" or "-c" when i + 1 < options.Length:
                    codecs = options[++i];
                    break;
                case "--hunksize" or "-hs" when i + 1 < options.Length:
                    if (!TryParseSizeWithSuffix(options[++i], out uint hs) || hs == 0)
                    {
                        log.Warning("createhd: invalid hunk size: {Value}", options[i]);
                        return;
                    }

                    hunkBytes = hs;
                    break;
                case "--unitsize" or "-us" when i + 1 < options.Length:
                    if (!TryParseSizeWithSuffix(options[++i], out uint us) || us == 0)
                    {
                        log.Warning("createhd: invalid unit size: {Value}", options[i]);
                        return;
                    }

                    unitBytes = us;
                    break;
                case "--numprocessors" or "-np" when i + 1 < options.Length:
                    if (!int.TryParse(options[++i], out var t) || t < 1 || t > 64)
                    {
                        log.Warning("createhd: invalid task count (1-64): {Value}", options[i]);
                        return;
                    }

                    taskCount = t;
                    break;
                case "--ident" or "-id" when i + 1 < options.Length:
                    identPath = options[++i];
                    break;
                case "--outputparent" or "-op" when i + 1 < options.Length:
                    outputParentPath = options[++i].Replace("\"", "");
                    break;
                case "--inputstartbyte" or "-isb" when i + 1 < options.Length:
                    if (!long.TryParse(options[++i], out var isb) || isb < 0)
                    {
                        log.Warning("createhd: invalid input start byte: {Value}", options[i]);
                        return;
                    }

                    inputStartBytes = isb;
                    break;
                case "--inputstarthunk" or "-ish" when i + 1 < options.Length:
                    if (!long.TryParse(options[++i], out var ish) || ish < 0)
                    {
                        log.Warning("createhd: invalid input start hunk: {Value}", options[i]);
                        return;
                    }

                    inputStartHunk = ish;
                    break;
                case "--inputbytes" or "-ib" when i + 1 < options.Length:
                    if (!long.TryParse(options[++i], out var ib) || ib <= 0)
                    {
                        log.Warning("createhd: invalid input bytes: {Value}", options[i]);
                        return;
                    }

                    inputLengthBytes = ib;
                    break;
                case "--inputhunks" or "-ih" when i + 1 < options.Length:
                    if (!long.TryParse(options[++i], out var ih) || ih <= 0)
                    {
                        log.Warning("createhd: invalid input hunks: {Value}", options[i]);
                        return;
                    }

                    inputLengthHunks = ih;
                    break;
                case "--force" or "-f":
                    force = true;
                    break;
                case "--verbose" or "-v":
                    verbose = true;
                    break;
                default:
                    log.Warning("createhd: unknown option: {Option}", options[i]);
                    return;
            }

        if (File.Exists(outputPath) && !force)
        {
            log.Warning(
                "Output file already exists: {Path} (use --force to overwrite)",
                outputPath
            );
            return;
        }

        // Apply hard disk template: derive geometry/size and stamp GDDD metadata
        if (templateId.HasValue)
        {
            var tpl = HardDiskTemplates.GetTemplate(templateId.Value);
            if (chsCylinders.HasValue || sizeBytes.HasValue)
            {
                log.Warning("createhd: --template cannot be combined with --size or -chs");
                return;
            }

            unitBytes = tpl.SectorSize;
            chsCylinders = tpl.Cylinders;
            chsHeads = tpl.Heads;
            chsSectors = tpl.Sectors;
            log.Information(
                "  Using template {Id}: {Manufacturer} {Model} ({Cylinders}C/{Heads}H/{Sectors}S, {Size} MB)",
                templateId.Value,
                tpl.Manufacturer,
                tpl.Model,
                tpl.Cylinders,
                tpl.Heads,
                tpl.Sectors,
                tpl.TotalMb
            );
        }

        // Validate required options
        if (!sizeBytes.HasValue && !chsCylinders.HasValue && inputPath == null)
        {
            log.Warning("createhd: requires --size N, -chs C,H,S, -tp ID, or --input <file>");
            return;
        }

        // If --input is provided, convert raw input to CHD
        if (inputPath != null)
        {
            if (!File.Exists(inputPath))
            {
                log.Warning("createhd: input file not found: {Path}", inputPath);
                return;
            }

            if (sizeBytes.HasValue || chsCylinders.HasValue || templateId.HasValue)
            {
                log.Warning(
                    "createhd: --input cannot be combined with --size, -chs, or --template"
                );
                return;
            }

            try
            {
                // chdman.cpp:2044 s_default_hd_compression = lzma,zlib,huff,flac for input, s_no_compression for blank
                var codecTags = ChdCodecs.ParseCodecTags(codecs ?? "lzma,zlib,huff,flac");
                log.Information(
                    "Converting raw HD to CHD: {Input} -> {Output} (codecs {Codecs})",
                    inputPath,
                    outputPath,
                    string.Join(",", codecTags.Select(CodecTags.ToString))
                );
                var encodeOptions = new ChdEncodeOptions();
                if (outputParentPath != null)
                    encodeOptions.ParentPath = outputParentPath;

                if (inputStartBytes.HasValue)
                    encodeOptions.InputStartBytes = inputStartBytes.Value;
                else if (inputStartHunk.HasValue)
                    encodeOptions.InputStartBytes = inputStartHunk.Value * hunkBytes;

                if (inputLengthBytes.HasValue)
                    encodeOptions.InputLengthBytes = inputLengthBytes.Value;
                else if (inputLengthHunks.HasValue)
                    encodeOptions.InputLengthBytes = inputLengthHunks.Value * hunkBytes;

                if (taskCount.HasValue)
                    encodeOptions.TaskCount = taskCount;

                // --ident handling for createhd -i (chdman.cpp:2057): extract CHS and add IDENT metadata
                byte[]? inputIdentData = null;
                uint? identCyl = null;
                uint? identHeads = null;
                uint? identSectors = null;
                if (identPath != null)
                {
                    if (!File.Exists(identPath))
                    {
                        log.Warning("createhd: ident file not found: {Path}", identPath);
                        return;
                    }

                    try
                    {
                        inputIdentData = File.ReadAllBytes(identPath);
                        if (inputIdentData.Length < 14)
                        {
                            log.Warning(
                                "--createhd: ident file is invalid (too short, need >=14 bytes, got {Size})",
                                inputIdentData.Length
                            );
                            return;
                        }

                        var ic = (uint)(inputIdentData[2] | (inputIdentData[3] << 8));
                        var ih = (uint)(inputIdentData[6] | (inputIdentData[7] << 8));
                        var isect = (uint)(inputIdentData[12] | (inputIdentData[13] << 8));
                        if ((ulong)ic * ih * isect >= 16_514_064UL)
                            ic = 0;
                        identCyl = ic != 0 ? ic : null;
                        identHeads = ic != 0 ? ih : null;
                        identSectors = ic != 0 ? isect : null;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        log.Warning("createhd: cannot read ident file: {Message}", ex.Message);
                        return;
                    }
                }

                // D2 fix: synthesize GDDD hard-disk geometry for createhd from raw .img
                // (chdman createhd always writes GDDD, guessing CHS from file size)
                // Use file length honoring input start/length when present, otherwise whole file
                // When --ident provided, use its CHS (or guess if >8GB), and also store IDENT metadata
                if (identPath != null)
                {
                    encodeOptions.Metadata ??= new List<MetadataEntry>();
                    var list = (List<MetadataEntry>)encodeOptions.Metadata;
                    if (list.All(e => e.Tag != MetadataWriter.HardDiskMetadataTag))
                    {
                        if (identCyl.HasValue && identHeads.HasValue && identSectors.HasValue)
                        {
                            list.Add(
                                MetadataWriter.BuildHardDiskMetadata(
                                    identCyl.Value,
                                    identHeads.Value,
                                    identSectors.Value,
                                    unitBytes
                                )
                            );
                        }
                        else
                        {
                            // >8GB case: guess CHS from filesize (chdman sets cylinders=0 → guess_chs)
                            ulong logicalForGddd;
                            if (inputStartBytes.HasValue || inputStartHunk.HasValue || inputLengthBytes.HasValue || inputLengthHunks.HasValue)
                            {
                                var fiLen = new FileInfo(inputPath).Length;
                                long startBytes = 0;
                                if (inputStartBytes.HasValue)
                                    startBytes = inputStartBytes.Value;
                                else if (inputStartHunk.HasValue)
                                    startBytes = inputStartHunk.Value * hunkBytes;
                                ulong avail = fiLen > startBytes ? (ulong)(fiLen - startBytes) : 0;
                                logicalForGddd = avail;
                                if (inputLengthBytes.HasValue)
                                    logicalForGddd = Math.Min(logicalForGddd, (ulong)inputLengthBytes.Value);
                                else if (inputLengthHunks.HasValue)
                                    logicalForGddd = Math.Min(logicalForGddd, (ulong)inputLengthHunks.Value * hunkBytes);
                            }
                            else
                            {
                                logicalForGddd = (ulong)new FileInfo(inputPath).Length;
                            }

                            list.Add(MetadataWriter.BuildHardDiskMetadata(logicalForGddd, unitBytes));
                        }
                    }

                    // also store IDENT metadata (chdman writes it after GDDD)
                    if (inputIdentData != null)
                    {
                        encodeOptions.Metadata ??= new List<MetadataEntry>();
                        ((List<MetadataEntry>)encodeOptions.Metadata).Add(
                            MetadataWriter.BuildIdentMetadata(inputIdentData)
                        );
                    }
                }
                else if (
                    outputParentPath == null
                    && !chsCylinders.HasValue
                    && !templateId.HasValue
                )
                {
                    encodeOptions.Metadata ??= new List<MetadataEntry>();
                    var list = (List<MetadataEntry>)encodeOptions.Metadata;
                    if (list.All(e => e.Tag != MetadataWriter.HardDiskMetadataTag))
                    {
                        ulong logicalForGddd;
                        if (inputStartBytes.HasValue || inputStartHunk.HasValue || inputLengthBytes.HasValue || inputLengthHunks.HasValue)
                        {
                            var fiLen = new FileInfo(inputPath).Length;
                            long startBytes = 0;
                            if (inputStartBytes.HasValue)
                                startBytes = inputStartBytes.Value;
                            else if (inputStartHunk.HasValue)
                                startBytes = inputStartHunk.Value * hunkBytes;
                            ulong avail = fiLen > startBytes ? (ulong)(fiLen - startBytes) : 0;
                            logicalForGddd = avail;
                            if (inputLengthBytes.HasValue)
                                logicalForGddd = Math.Min(logicalForGddd, (ulong)inputLengthBytes.Value);
                            else if (inputLengthHunks.HasValue)
                                logicalForGddd = Math.Min(logicalForGddd, (ulong)inputLengthHunks.Value * hunkBytes);
                        }
                        else
                        {
                            logicalForGddd = (ulong)new FileInfo(inputPath).Length;
                        }

                        list.Add(MetadataWriter.BuildHardDiskMetadata(logicalForGddd, unitBytes));
                    }
                }

                ChdEncoder.EncodeRaw(
                    inputPath,
                    outputPath,
                    hunkBytes,
                    unitBytes,
                    codecTags,
                    encodeOptions
                );
                log.Information("  Created {Size:N0} bytes", new FileInfo(outputPath).Length);
                VerifyResultChd(outputPath, outputParentPath);
            }
            catch (Exception ex)
                when (ex is ArgumentException or IOException or UnauthorizedAccessException)
            {
                log.Warning("createhd failed: {Message}", ex.Message);
            }

            return;
        }

        // Calculate size from CHS if provided
        if (chsCylinders.HasValue && chsHeads.HasValue && chsSectors.HasValue)
        {
            ulong chsSize;
            try
            {
                chsSize = checked(
                    chsCylinders.Value * (ulong)chsHeads.Value * chsSectors.Value * unitBytes
                );
            }
            catch (OverflowException)
            {
                log.Warning(
                    "createhd: CHS geometry produces a size exceeding ulong.MaxValue; reduce C/H/S values"
                );
                return;
            }

            if (chsSize == 0)
            {
                log.Warning("createhd: CHS geometry produces zero-byte image");
                return;
            }

            if (sizeBytes.HasValue && sizeBytes.Value != chsSize)
            {
                log.Warning(
                    "createhd: --size ({Size}) conflicts with -chs geometry ({ChsSize}); use one or the other",
                    sizeBytes.Value,
                    chsSize
                );
                return;
            }

            sizeBytes = chsSize;
        }

        // chdman.cpp:2046 — blank hard disk images must be uncompressed
        if (codecs != null && !string.Equals(codecs, "none", StringComparison.OrdinalIgnoreCase))
        {
            log.Warning("createhd: blank hard disk images must be uncompressed (use -c none)");
            return;
        }

        codecs ??= "none";

        // Read ident file if provided — chdman.cpp:2057 extracts CHS from bytes 2/6/12
        byte[]? identData = null;
        if (identPath != null)
        {
            if (!File.Exists(identPath))
            {
                log.Warning("--createhd: ident file not found: {Path}", identPath);
                return;
            }

            try
            {
                identData = File.ReadAllBytes(identPath);
                if (identData.Length < 14)
                {
                    log.Warning(
                        "--createhd: ident file is invalid (too short, need >=14 bytes, got {Size})",
                        identData.Length
                    );
                    return;
                }

                // chdman: cylinders = get_u16le(&data[2]), heads = get_u16le(&data[6]), sectors = get_u16le(&data[12])
                var idCyl = (uint)(identData[2] | (identData[3] << 8));
                var idHeads = (uint)(identData[6] | (identData[7] << 8));
                var idSectors = (uint)(identData[12] | (identData[13] << 8));
                // ignore CHS for >8GB drives (chdman.cpp:2073)
                if ((ulong)idCyl * idHeads * idSectors >= 16_514_064UL)
                    idCyl = 0;

                // ident CHS overrides prior -chs/-tp, matching chdman ordering
                if (idCyl != 0)
                {
                    chsCylinders = idCyl;
                    chsHeads = idHeads;
                    chsSectors = idSectors;
                }
                else
                {
                    // chdman sets cylinders=0 to trigger guess_chs later
                    chsCylinders = null;
                    chsHeads = null;
                    chsSectors = null;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                log.Warning("--createhd: cannot read ident file: {Message}", ex.Message);
                return;
            }
        }

        try
        {
            var codecTags = ChdCodecs.ParseCodecTags(codecs);
            log.Information(
                "Creating blank HD CHD: {Output}  (size {Size:N0}B, hunk {Hunk}B, unit {Unit}B, codecs {Codecs}{Chs}{Tasks})",
                outputPath,
                sizeBytes!.Value,
                hunkBytes,
                unitBytes,
                string.Join(",", codecTags.Select(CodecTags.ToString)),
                chsCylinders.HasValue ? $", CHS {chsCylinders},{chsHeads},{chsSectors}" : "",
                taskCount.HasValue ? $", {taskCount} tasks" : ""
            );

            var logger = verbose ? new VerboseHunkLogger() : null;
            var encodeOptions = logger?.Options;
            if (
                encodeOptions == null
                && (
                    taskCount.HasValue
                    || outputParentPath != null
                    || inputStartBytes.HasValue
                    || inputLengthBytes.HasValue
                    || inputStartHunk.HasValue
                    || inputLengthHunks.HasValue
                )
            )
                encodeOptions = new ChdEncodeOptions();

            if (encodeOptions != null)
            {
                if (taskCount.HasValue)
                    encodeOptions.TaskCount = taskCount;

                if (outputParentPath != null)
                    encodeOptions.ParentPath = outputParentPath;

                if (inputStartBytes.HasValue)
                    encodeOptions.InputStartBytes = inputStartBytes.Value;
                else if (inputStartHunk.HasValue)
                    encodeOptions.InputStartBytes = inputStartHunk.Value * hunkBytes;

                if (inputLengthBytes.HasValue)
                    encodeOptions.InputLengthBytes = inputLengthBytes.Value;
                else if (inputLengthHunks.HasValue)
                    encodeOptions.InputLengthBytes = inputLengthHunks.Value * hunkBytes;
            }

            // Add ident metadata if provided
            if (identData != null)
            {
                encodeOptions ??= new ChdEncodeOptions();
                encodeOptions.Metadata ??= new List<MetadataEntry>();
                ((List<MetadataEntry>)encodeOptions.Metadata).Add(
                    MetadataWriter.BuildIdentMetadata(identData)
                );
            }

            if (chsCylinders.HasValue && chsHeads.HasValue && chsSectors.HasValue)
                ChdEncoder.CreateBlankWithChs(
                    outputPath,
                    chsCylinders.Value,
                    chsHeads.Value,
                    chsSectors.Value,
                    unitBytes,
                    hunkBytes,
                    codecTags,
                    encodeOptions
                );
            else
                ChdEncoder.CreateBlank(
                    outputPath,
                    sizeBytes.Value,
                    hunkBytes,
                    unitBytes,
                    codecTags,
                    encodeOptions
                );

            logger?.LogSummary();
            log.Information("  Created {Size:N0} bytes", new FileInfo(outputPath).Length);
            VerifyResultChd(outputPath);
        }
        catch (Exception ex)
            when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            log.Warning("--createhd failed: {Message}", ex.Message);
        }
    }

    /// <summary>
    ///     Creates a CD CHD from a CUE sheet using the CHDSharp.Encoder, then verifies
    ///     the file with a deep CHDSharpLib check.
    /// </summary>
    /// <param name="inputPath">Path of the .cue file.</param>
    /// <param name="outputPath">Path of the output .chd file.</param>
    /// <param name="options">Optional <c>-c</c> codec list, <c>-hs</c> hunk size and <c>-us</c> unit size arguments.</param>
    private static void CreateCdTest(string inputPath, string outputPath, string[] options)
    {
        var log = Log.Logger;
        if (!File.Exists(inputPath))
        {
            log.Warning("--createcd: input file not found: {Path}", inputPath);
            return;
        }

        uint hunkSize = CdConstants.FramesPerHunk * CdConstants.FrameSize;
        uint unitBytes = CdConstants.FrameSize;
        string? codecs = null;
        string? parentPath = null;
        var verbose = false;
        var dvd = false;
        var force = false;
        int? taskCount = null;
        int? templateId = null;
        long? inputStartBytes = null;
        long? inputLengthBytes = null;
        long? inputStartHunk = null;
        long? inputLengthHunks = null;
        long? inputStartFrame = null;
        long? inputLengthFrames = null;
        if (
            !TryParseOptions(
                options,
                ref hunkSize,
                ref unitBytes,
                ref codecs,
                ref parentPath,
                ref verbose,
                ref taskCount,
                ref dvd,
                ref templateId,
                ref inputStartBytes,
                ref inputLengthBytes,
                ref force,
                ref inputStartHunk,
                ref inputLengthHunks,
                ref inputStartFrame,
                ref inputLengthFrames
            )
        )
            return;

        if (File.Exists(outputPath) && !force)
        {
            log.Warning(
                "Output file already exists: {Path} (use --force to overwrite)",
                outputPath
            );
            return;
        }

        // Apply documented default codecs for CD when -c is omitted
        codecs ??= "cdlz,cdzl,cdfl";

        // -c auto: detect the platform and pick the smart codec preset (CHDlite parity).
        if (string.Equals(codecs, "auto", StringComparison.OrdinalIgnoreCase))
        {
            var detected = PlatformDetector.Detect(inputPath);
            var preset = PlatformDetector.AutoCodecs(detected.Platform, "cd");
            codecs = preset != null ? string.Join(",", preset.Select(CodecTags.ToString)) : "zlib";
            log.Information("  Detected {Platform}; using codecs {Codecs}", detected, codecs);
        }

        try
        {
            var codecTags = ChdCodecs.ParseCodecTags(codecs);
            log.Information(
                "Creating CD CHD: {Input} -> {Output}  (hunk {Hunk}B, unit {Unit}B, codecs {Codecs}{Parent}{Tasks})",
                Path.GetFileName(inputPath),
                outputPath,
                hunkSize,
                unitBytes,
                string.Join(",", codecTags.Select(CodecTags.ToString)),
                parentPath != null ? $", parent {Path.GetFileName(parentPath)}" : "",
                taskCount.HasValue ? $", {taskCount} tasks" : ""
            );
            var logger = verbose ? new VerboseHunkLogger() : null;
            var encodeOptions = logger?.Options;
            if (encodeOptions == null && (taskCount.HasValue || parentPath != null))
                encodeOptions = new ChdEncodeOptions();

            if (encodeOptions != null)
            {
                if (taskCount.HasValue)
                    encodeOptions.TaskCount = taskCount;

                if (parentPath != null)
                    encodeOptions.ParentPath = parentPath;
            }

            ChdEncoder.EncodeCd(
                inputPath,
                outputPath,
                hunkSize,
                unitBytes,
                codecTags,
                encodeOptions
            );
            logger?.LogSummary();
            log.Information("  Created ({File:N0} bytes)", new FileInfo(outputPath).Length);
            VerifyResultChd(outputPath, parentPath);
        }
        catch (Exception ex)
            when (ex
                      is ArgumentException
                      or InvalidDataException
                      or IOException
                      or UnauthorizedAccessException
                      or FileNotFoundException
                 )
        {
            log.Warning("--createcd failed: {Message}", ex.Message);
        }
    }

    /// <summary>
    ///     Creates a laserdisc CHD from an AVI file using the CHDSharp.Encoder ('avhu' codec),
    ///     then verifies the result with a deep CHDSharpLib check.
    /// </summary>
    /// <param name="inputPath">Path of the source .avi file.</param>
    /// <param name="outputPath">Path of the output .chd file.</param>
    /// <param name="options">
    ///     Optional <c>-c</c> codec list, <c>-isf</c>/<c>-if</c> frame range,
    ///     <c>-np</c> task count, <c>-op</c> output parent, and <c>-v</c> verbose arguments.
    /// </param>
    private static void CreateLdTest(string inputPath, string outputPath, string[] options)
    {
        var log = Log.Logger;
        if (!File.Exists(inputPath))
        {
            log.Warning("--createld: input file not found: {Path}", inputPath);
            return;
        }

        uint hunkBytes = 0;
        string? codecs = null;
        string? outputParentPath = null;
        long startFrame = 0;
        long? lengthFrames = null;
        var verbose = false;
        var force = false;
        int? taskCount = null;
        for (var i = 0; i < options.Length; i++)
            switch (options[i])
            {
                case "--compression" or "-c" or "--codecs" when i + 1 < options.Length:
                    codecs = options[++i];
                    break;
                case "--hunksize" or "-hs" when i + 1 < options.Length:
                    if (!TryParseSizeWithSuffix(options[++i], out uint hs) || hs == 0)
                    {
                        log.Warning("Invalid hunk size: {Value}", options[i]);
                        return;
                    }

                    hunkBytes = hs;
                    break;
                case "--inputstartframe" or "-isf" when i + 1 < options.Length:
                    if (!long.TryParse(options[++i], out var isf) || isf < 0)
                    {
                        log.Warning("Invalid input start frame: {Value}", options[i]);
                        return;
                    }

                    startFrame = isf;
                    break;
                case "--inputframes" or "-if" when i + 1 < options.Length:
                    if (!long.TryParse(options[++i], out var ifr) || ifr < 1)
                    {
                        log.Warning("Invalid input frame count: {Value}", options[i]);
                        return;
                    }

                    lengthFrames = ifr;
                    break;
                case "--outputparent" or "-op" when i + 1 < options.Length:
                    outputParentPath = options[++i].Replace("\"", "");
                    break;
                case "--numprocessors" or "-np" when i + 1 < options.Length:
                case "-t" or "--tasks" when i + 1 < options.Length:
                    if (!int.TryParse(options[++i], out var t) || t < 1 || t > 64)
                    {
                        log.Warning("Invalid task count (1-64): {Value}", options[i]);
                        return;
                    }

                    taskCount = t;
                    break;
                case "--force" or "-f":
                    force = true;
                    break;
                case "-v" or "--verbose":
                    verbose = true;
                    break;
                default:
                    log.Warning("Unknown option: {Option}", options[i]);
                    return;
            }

        if (File.Exists(outputPath) && !force)
        {
            log.Warning(
                "Output file already exists: {Path} (use --force to overwrite)",
                outputPath
            );
            return;
        }

        try
        {
            var codecTags = ChdCodecs.ParseCodecTags(codecs ?? "avhu");
            var logger = verbose ? new VerboseHunkLogger() : null;
            var encodeOptions = logger?.Options;
            if (encodeOptions == null && (taskCount.HasValue || outputParentPath != null))
                encodeOptions = new ChdEncodeOptions();

            if (encodeOptions != null)
            {
                if (taskCount.HasValue)
                    encodeOptions.TaskCount = taskCount;

                if (outputParentPath != null)
                    encodeOptions.ParentPath = outputParentPath;
            }

            log.Information(
                "Creating laserdisc CHD: {Input} -> {Output}  (codecs {Codecs}{Parent}{Tasks})",
                Path.GetFileName(inputPath),
                outputPath,
                string.Join(",", codecTags.Select(CodecTags.ToString)),
                outputParentPath != null ? $", parent {Path.GetFileName(outputParentPath)}" : "",
                taskCount.HasValue ? $", {taskCount} tasks" : ""
            );

            var info = ChdEncoder.EncodeLaserDisc(
                inputPath,
                outputPath,
                hunkBytes,
                codecTags,
                encodeOptions,
                startFrame,
                lengthFrames
            );

            log.Information(
                "  Frame rate:   {Fps}.{FpsFrac:D6}",
                info.FpsTimes1Million / 1000000,
                info.FpsTimes1Million % 1000000
            );
            log.Information(
                "  Frame size:   {Width} x {Height}{Interlaced}",
                info.Width,
                info.Interlaced ? info.Height * 2 : info.Height,
                info.Interlaced ? " interlaced" : ""
            );
            log.Information(
                "  Audio:        {Channels} channels at {Rate} Hz",
                info.Channels,
                info.SampleRate
            );
            log.Information(
                "  Frames:       {Frames} ({First}..{Last})",
                info.Frames,
                info.FirstFrame,
                info.FirstFrame + info.Frames - 1
            );
            log.Information(
                "  Hunk size:    {Hunk} bytes ({Samples} max samples/frame)",
                info.HunkBytes,
                info.MaxSamplesPerFrame
            );
            log.Information("  Created ({File:N0} bytes)", new FileInfo(outputPath).Length);
            VerifyResultChd(outputPath);
        }
        catch (Exception ex)
            when (ex
                      is ArgumentException
                      or NotSupportedException
                      or InvalidDataException
                      or IOException
                      or UnauthorizedAccessException
                 )
        {
            log.Warning("--createld failed: {Message}", ex.Message);
        }
    }

    /// <summary>
    ///     Extracts a laserdisc CHD back to an AVI file and verifies the result.
    /// </summary>
    private static void ExtractLdTest(string inputPath, string outputPath, string[] options)
    {
        var log = Log.Logger;
        if (!File.Exists(inputPath))
        {
            log.Warning("extractld: input file not found: {Path}", inputPath);
            return;
        }

        long startFrame = 0;
        long? lengthFrames = null;
        var force = false;
        string? parentPath = null;
        for (var i = 0; i < options.Length; i++)
            switch (options[i])
            {
                case "--inputstartframe" or "-isf" when i + 1 < options.Length:
                    if (!long.TryParse(options[++i], out var sf) || sf < 0)
                    {
                        log.Warning("Invalid input start frame: {Value}", options[i]);
                        return;
                    }

                    startFrame = sf;
                    break;
                case "--inputframes" or "-if" when i + 1 < options.Length:
                    if (!long.TryParse(options[++i], out var ifr) || ifr <= 0)
                    {
                        log.Warning("Invalid input frames: {Value}", options[i]);
                        return;
                    }

                    lengthFrames = ifr;
                    break;
                case "--inputparent" or "-ip" when i + 1 < options.Length:
                    parentPath = options[++i].Replace("\"", "");
                    break;
                case "--force" or "-f":
                    force = true;
                    break;
                default:
                    log.Warning("Unknown option: {Option}", options[i]);
                    return;
            }

        if (File.Exists(outputPath) && !force)
        {
            log.Warning(
                "Output file already exists: {Path} (use --force to overwrite)",
                outputPath
            );
            return;
        }

        try
        {
            log.Information(
                "Extracting laserdisc CHD: {Input} -> {Output}",
                Path.GetFileName(inputPath),
                outputPath
            );
            ChdEncoder.ExtractLaserDisc(
                inputPath,
                outputPath,
                parentPath,
                startFrame,
                lengthFrames
            );
            log.Information(
                "  Created {File} ({Size:N0} bytes)",
                Path.GetFileName(outputPath),
                new FileInfo(outputPath).Length
            );
        }
        catch (Exception ex)
            when (ex
                      is ArgumentException
                      or NotSupportedException
                      or InvalidDataException
                      or IOException
                      or UnauthorizedAccessException
                 )
        {
            log.Warning("--extractld failed: {Message}", ex.Message);
        }
    }

    /// <summary>
    ///     Prints the built-in hard disk geometry templates (MAME's <c>listtemplates</c>).
    /// </summary>
    private static void ListTemplates()
    {
        Log.Logger.Information("");
        Log.Logger.Information(
            "ID  Manufacturer  Model           Cylinders  Heads  Sectors  Sector Size  Total Size"
        );
        Log.Logger.Information(
            "------------------------------------------------------------------------------------"
        );
        for (var id = 0; id < HardDiskTemplates.Templates.Length; id++)
        {
            var t = HardDiskTemplates.Templates[id];
            Log.Logger.Information(
                "{Id,2}  {Manufacturer,-13} {Model,-15} {Cylinders,9}  {Heads,5}  {Sectors,7}  {SectorSize,11}  {TotalMb,7} MB",
                id,
                t.Manufacturer,
                t.Model,
                t.Cylinders,
                t.Heads,
                t.Sectors,
                t.SectorSize,
                t.TotalMb
            );
        }
    }

    /// <summary>
    ///     Parses optional codec/hunk/unit/parent/task/template/verbose arguments from the CLI.
    ///     Accepts both chdman-style (<c>--hunksize</c>, <c>--numprocessors</c>) and legacy-style (<c>--hunk-size</c>,
    ///     <c>--tasks</c>) names.
    /// </summary>
    private static bool TryParseOptions(
        string[] options,
        ref uint hunkSize,
        ref uint unitSize,
        ref string? codecs,
        ref string? parentPath,
        ref bool verbose,
        ref int? taskCount,
        ref bool dvd,
        ref int? templateId,
        ref long? inputStartBytes,
        ref long? inputLengthBytes,
        ref bool force,
        ref long? inputStartHunk,
        ref long? inputLengthHunks,
        ref long? inputStartFrame,
        ref long? inputLengthFrames
    )
    {
        for (var i = 0; i < options.Length; i++)
            switch (options[i])
            {
                case "--compression" or "-c" when i + 1 < options.Length:
                    codecs = options[++i];
                    break;
                case "--inputparent" or "-ip" when i + 1 < options.Length:
                case "--outputparent" or "-op" when i + 1 < options.Length:
                    parentPath = options[++i].Replace("\"", "");
                    break;
                case "--hunksize" or "-hs" when i + 1 < options.Length:
                    if (!TryParseSizeWithSuffix(options[++i], out uint hs) || hs == 0)
                    {
                        Log.Logger.Warning("Invalid hunk size: {Value}", options[i]);
                        return false;
                    }

                    hunkSize = hs;
                    break;
                case "--unitsize" or "-us" when i + 1 < options.Length:
                    if (!TryParseSizeWithSuffix(options[++i], out uint us) || us == 0)
                    {
                        Log.Logger.Warning("Invalid unit size: {Value}", options[i]);
                        return false;
                    }

                    unitSize = us;
                    break;
                case "--numprocessors" or "-np" when i + 1 < options.Length:
                    if (!int.TryParse(options[++i], out var t) || t < 1 || t > 64)
                    {
                        Log.Logger.Warning("Invalid task count (1-64): {Value}", options[i]);
                        return false;
                    }

                    taskCount = t;
                    break;
                case "--template" or "-tp" when i + 1 < options.Length:
                    if (
                        !int.TryParse(options[++i], out var tp)
                        || tp < 0
                        || tp >= HardDiskTemplates.Templates.Length
                    )
                    {
                        Log.Logger.Warning(
                            "Invalid template ID (0-{Max}): {Value}",
                            HardDiskTemplates.Templates.Length - 1,
                            options[i]
                        );
                        return false;
                    }

                    templateId = tp;
                    break;
                case "--inputstartbyte" or "-isb" when i + 1 < options.Length:
                    if (!long.TryParse(options[++i], out var isb) || isb < 0)
                    {
                        Log.Logger.Warning("Invalid input start byte: {Value}", options[i]);
                        return false;
                    }

                    inputStartBytes = isb;
                    break;
                case "--inputstarthunk" or "-ish" when i + 1 < options.Length:
                    if (!long.TryParse(options[++i], out var ish) || ish < 0)
                    {
                        Log.Logger.Warning("Invalid input start hunk: {Value}", options[i]);
                        return false;
                    }

                    inputStartHunk = ish;
                    break;
                case "--inputstartframe" or "-isf" when i + 1 < options.Length:
                    if (!long.TryParse(options[++i], out var isf) || isf < 0)
                    {
                        Log.Logger.Warning("Invalid input start frame: {Value}", options[i]);
                        return false;
                    }

                    inputStartFrame = isf;
                    break;
                case "--inputbytes" or "-ib" when i + 1 < options.Length:
                    if (!long.TryParse(options[++i], out var ib) || ib <= 0)
                    {
                        Log.Logger.Warning("Invalid input bytes: {Value}", options[i]);
                        return false;
                    }

                    inputLengthBytes = ib;
                    break;
                case "--inputhunks" or "-ih" when i + 1 < options.Length:
                    if (!long.TryParse(options[++i], out var ih) || ih <= 0)
                    {
                        Log.Logger.Warning("Invalid input hunks: {Value}", options[i]);
                        return false;
                    }

                    inputLengthHunks = ih;
                    break;
                case "--inputframes" or "-if" when i + 1 < options.Length:
                    if (!long.TryParse(options[++i], out var ifr) || ifr <= 0)
                    {
                        Log.Logger.Warning("Invalid input frames: {Value}", options[i]);
                        return false;
                    }

                    inputLengthFrames = ifr;
                    break;
                case "--dvd" or "-d":
                    dvd = true;
                    break;
                case "--force" or "-f":
                    force = true;
                    break;
                case "--verbose" or "-v":
                    verbose = true;
                    break;
                default:
                    Log.Logger.Warning("Unknown option: {Option}", options[i]);
                    return false;
            }

        return true;
    }

    /// <summary>
    ///     Re-compresses a CHD file into a new CHD with the target codecs (<c>--copy</c>),
    ///     cloning the source's metadata, then verifies the result with a deep CHDSharpLib check.
    /// </summary>
    /// <param name="inputPath">Path of the source CHD file.</param>
    /// <param name="outputPath">Path of the output .chd file.</param>
    /// <param name="options">
    ///     Optional <c>-c</c> codec list, <c>-np</c> task count, <c>-ip</c> source
    ///     parent, <c>-op</c> output parent, <c>-hs</c> hunk size, <c>-isb</c>/<c>-ish</c> input start,
    ///     <c>-ib</c>/<c>-ih</c> input length, <c>--no-upgrade</c> to preserve legacy metadata, and
    ///     <c>-v</c> verbose arguments.
    /// </param>
    private static void CopyTest(string inputPath, string outputPath, string[] options)
    {
        var log = Log.Logger;
        if (!File.Exists(inputPath))
        {
            log.Warning("--copy: input file not found: {Path}", inputPath);
            return;
        }

        string? codecs = null;
        string? sourceParentPath = null;
        string? outputParentPath = null;
        var verbose = false;
        var force = false;
        int? taskCount = null;
        var noUpgrade = false;
        uint? hunkSize = null;
        long? inputStartBytes = null;
        long? inputLengthBytes = null;
        long? inputStartHunk = null;
        long? inputLengthHunks = null;
        for (var i = 0; i < options.Length; i++)
            switch (options[i])
            {
                case "--compression" or "-c" or "--codecs" when i + 1 < options.Length:
                    codecs = options[++i];
                    break;
                case "-ip" or "--inputparent" when i + 1 < options.Length:
                    sourceParentPath = options[++i].Replace("\"", "");
                    break;
                case "-op" or "--outputparent" when i + 1 < options.Length:
                    outputParentPath = options[++i].Replace("\"", "");
                    break;
                case "--numprocessors" or "-np" when i + 1 < options.Length:
                case "-t" or "--tasks" when i + 1 < options.Length:
                    if (!int.TryParse(options[++i], out var t) || t < 1 || t > 64)
                    {
                        log.Warning("Invalid task count (1-64): {Value}", options[i]);
                        return;
                    }

                    taskCount = t;
                    break;
                case "--hunksize" or "-hs" when i + 1 < options.Length:
                    if (!TryParseSizeWithSuffix(options[++i], out uint hs) || hs == 0)
                    {
                        log.Warning("Invalid hunk size: {Value}", options[i]);
                        return;
                    }

                    hunkSize = hs;
                    break;
                case "--inputstartbyte" or "-isb" when i + 1 < options.Length:
                    if (!long.TryParse(options[++i], out var isb) || isb < 0)
                    {
                        log.Warning("Invalid input start byte: {Value}", options[i]);
                        return;
                    }

                    inputStartBytes = isb;
                    break;
                case "--inputstarthunk" or "-ish" when i + 1 < options.Length:
                    if (!long.TryParse(options[++i], out var ish) || ish < 0)
                    {
                        log.Warning("Invalid input start hunk: {Value}", options[i]);
                        return;
                    }

                    inputStartHunk = ish;
                    break;
                case "--inputbytes" or "-ib" when i + 1 < options.Length:
                    if (!long.TryParse(options[++i], out var ib) || ib <= 0)
                    {
                        log.Warning("Invalid input bytes: {Value}", options[i]);
                        return;
                    }

                    inputLengthBytes = ib;
                    break;
                case "--inputhunks" or "-ih" when i + 1 < options.Length:
                    if (!long.TryParse(options[++i], out var ih) || ih <= 0)
                    {
                        log.Warning("Invalid input hunks: {Value}", options[i]);
                        return;
                    }

                    inputLengthHunks = ih;
                    break;
                case "--no-upgrade":
                    noUpgrade = true;
                    break;
                case "--force" or "-f":
                    force = true;
                    break;
                case "-v" or "--verbose":
                    verbose = true;
                    break;
                default:
                    log.Warning("Unknown option: {Option}", options[i]);
                    return;
            }

        if (File.Exists(outputPath) && !force)
        {
            log.Warning(
                "Output file already exists: {Path} (use --force to overwrite)",
                outputPath
            );
            return;
        }

        try
        {
            var codecTags = ChdCodecs.ParseCodecTags(codecs);
            log.Information(
                "Copying CHD: {Input} -> {Output}  (codecs {Codecs}{SourceParent}{OutputParent}{Tasks}{Upgrade})",
                Path.GetFileName(inputPath),
                outputPath,
                string.Join(",", codecTags.Select(CodecTags.ToString)),
                sourceParentPath != null
                    ? $", source parent {Path.GetFileName(sourceParentPath)}"
                    : "",
                outputParentPath != null
                    ? $", output parent {Path.GetFileName(outputParentPath)}"
                    : "",
                taskCount.HasValue ? $", {taskCount} tasks" : "",
                noUpgrade ? ", no metadata upgrade" : ""
            );

            // chdman copy: -ish/-ih are in units of the *input* CHD's hunk size (parse_input_start_end:1203)
            uint sourceHunkBytes = 4096;
            try
            {
                var herr = Chd.ReadHeader(inputPath, out var shdr);
                if (herr == ChdError.Chderrnone && shdr != null && shdr.HunkBytes != 0)
                    sourceHunkBytes = shdr.HunkBytes;
            }
            catch
            {
                /* ignore */
            }

            var encodeOptions = new ChdEncodeOptions
            {
                SourceParentPath = sourceParentPath,
                ParentPath = outputParentPath,
                TaskCount = taskCount,
                NoMetadataUpgrade = noUpgrade,
                HunkBytes = hunkSize
            };

            if (inputStartBytes.HasValue)
                encodeOptions.InputStartBytes = inputStartBytes.Value;
            else if (inputStartHunk.HasValue)
                encodeOptions.InputStartBytes = inputStartHunk.Value * sourceHunkBytes;

            if (inputLengthBytes.HasValue)
                encodeOptions.InputLengthBytes = inputLengthBytes.Value;
            else if (inputLengthHunks.HasValue)
                encodeOptions.InputLengthBytes = inputLengthHunks.Value * sourceHunkBytes;

            var logger = verbose ? new VerboseHunkLogger() : null;
            encodeOptions.HunkCompleted = logger?.Options.HunkCompleted;

            ChdEncoder.Copy(inputPath, outputPath, codecTags, encodeOptions);
            logger?.LogSummary();
            log.Information("  Created {Size:N0} bytes", new FileInfo(outputPath).Length);
            VerifyResultChd(outputPath, outputParentPath);
        }
        catch (Exception ex)
            when (ex
                      is ArgumentException
                      or IOException
                      or InvalidDataException
                      or UnauthorizedAccessException
                      or FileNotFoundException
                 )
        {
            log.Warning("--copy failed: {Message}", ex.Message);
        }
    }

    /// <summary>
    ///     Runs a deep CHDSharpLib check on a created CHD file (raw + combined SHA1);
    ///     for differential children the parent CHD is supplied so parent references resolve.
    /// </summary>
    private static void VerifyResultChd(string path, string? parentPath = null)
    {
        if (parentPath != null)
        {
            var parentResult = Chd.CheckFileWithParent(path, parentPath);
            if (parentResult.IsSuccess)
                Log.Logger.Information(
                    "  Verified OK (V{Version}, sha1={Sha1}, parent={Parent})",
                    parentResult.Version,
                    parentResult.Sha1Hex,
                    Path.GetFileName(parentPath)
                );
            else
                Log.Logger.Warning("  Verified FAILED: {Error}", parentResult.Error);
            return;
        }

        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var result = Chd.CheckFile(fs, Path.GetFileName(path), true);
        if (result.IsSuccess)
            Log.Logger.Information(
                "  Verified OK (V{Version}, sha1={Sha1})",
                result.Version,
                result.Sha1Hex
            );
        else
            Log.Logger.Warning("  Verified FAILED: {Error}", result.Error);
    }

    /// <summary>
    ///     Detects the game platform of a disc image (CHD or raw/descriptor file) and prints
    ///     the platform, title, and manufacturer ID.
    /// </summary>
    private static void DetectTest(string file)
    {
        var log = Log.Logger;
        if (!File.Exists(file))
        {
            log.Warning("--detect: file not found: {Path}", file);
            return;
        }

        try
        {
            DiscPlatformInfo result;
            if (file.EndsWith(".chd", StringComparison.OrdinalIgnoreCase))
                result = DiscDetector.DetectChd(file);
            else
                result = PlatformDetector.Detect(file);

            log.Information("{File}: {Platform}", Path.GetFileName(file), result.ToString());
            if (result.Platform != DiscPlatform.Unknown)
            {
                var preset = PlatformDetector.AutoCodecs(
                    result.Platform,
                    result.Platform == DiscPlatform.Dvd ? "dvd" : "cd"
                );
                if (preset != null)
                    log.Information(
                        "  Recommended codecs: {Codecs}",
                        string.Join(",", preset.Select(CodecTags.ToString))
                    );
            }
        }
        catch (Exception ex)
            when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            log.Warning("--detect failed: {Message}", ex.Message);
        }
    }

    /// <summary>Verifies a CHD, optionally repairing mismatched SHA-1 header fields (<c>--fix</c>).</summary>
    private static bool VerifyTest(string file, string[] options)
    {
        var log = Log.Logger;
        var fix =
            options.Contains("--fix", StringComparer.Ordinal)
            || options.Contains("-f", StringComparer.Ordinal);
        if (fix)
        {
            var err = Chd.CheckFileAndRepair(file, out var repaired);
            if (!err.IsSuccess)
            {
                log.Warning("Verify failed: {Error}", err);
                return false;
            }

            if (repaired)
                log.Information("  Fixed mismatched SHA-1 field(s); re-verifying...");
        }

        string? parentFile = null;
        for (var i = 0; i < options.Length - 1; i++)
            if (options[i] is "-ip" or "--inputparent")
            {
                parentFile = options[i + 1];
                break;
            }

        var result =
            parentFile != null
                ? Chd.CheckFileWithParent(file, parentFile)
                : Chd.CheckFileWithParent(file, (string?)null);
        if (result.IsSuccess)
        {
            log.Information(
                "  Verified OK (V{Version}, sha1={Sha1})",
                result.Version,
                result.Sha1Hex
            );
            return true;
        }

        log.Warning("  Verified FAILED: {Error}", result.Error);
        return false;
    }

    /// <summary>
    ///     Prints a full header/map dump (chdman <c>info</c> + CHDlite header-dump parity):
    ///     version, sizes, codecs per map slot, map CRC-16 status, parent linkage, and metadata list.
    /// </summary>
    private static void InfoTest(string file, string[]? options = null)
    {
        var log = Log.Logger;
        var verbose = false;
        if (options != null)
            for (var i = 0; i < options.Length; i++)
                switch (options[i])
                {
                    case "--verbose" or "-v":
                        verbose = true;
                        break;
                    case "--input" or "-i" when i + 1 < options.Length:
                        // -i <file> is already consumed by ParseInput; skip both tokens
                        i++;
                        break;
                    default:
                        if (!options[i].StartsWith('-'))
                            break; // positional file path (already consumed); ignore

                        log.Warning("info: unknown option: {Option}", options[i]);
                        return;
                }

        var err = Chd.ReadHeader(file, out var header);
        if (err != ChdError.Chderrnone || header == null)
        {
            log.Warning("Info failed: {Error}", err);
            return;
        }

        // Match chdman info output format exactly (Key: Value)
        Console.WriteLine($"Input file:   {Path.GetFileName(file)}");
        Console.WriteLine($"File Version: {header.Version}");

        var compression = header.Compression;
        Console.WriteLine($"Logical size: {BigintString(header.TotalBytes)} bytes");
        Console.WriteLine($"Hunk Size:    {BigintString(header.HunkBytes)} bytes");
        Console.WriteLine($"Total Hunks:  {BigintString(header.TotalHunks)}");
        Console.WriteLine($"Unit Size:    {BigintString(header.UnitBytes)} bytes");
        Console.WriteLine($"Total Units:  {BigintString(header.UnitCount)}");
        Console.WriteLine($"Compression:  {CompressionString(compression)}");

        // CHD file size
        long chdSize = 0;
        try
        {
            chdSize = new FileInfo(file).Length;
        }
        catch
        {
            /* ignore */
        }

        Console.WriteLine($"CHD size:     {BigintString((ulong)chdSize)} bytes");

        // SHA-1 hashes
        var overall = header.Sha1;
        if (overall != null && !Util.IsAllZeroArray(overall))
        {
            Console.WriteLine($"SHA1:         {Util.ToHex(overall)}");
            if (header.Version >= 4)
                Console.WriteLine($"Data SHA1:    {Util.ToHex(header.RawSha1)}");
        }

        var parent = header.ParentSha1;
        if (parent != null && !Util.IsAllZeroArray(parent))
            Console.WriteLine($"Parent SHA1:  {Util.ToHex(parent)}");

        // Metadata listing
        if (header.MetaOffset == 0 && !verbose)
            return;

        var openErr = ChdFile.Open(file, out var chd);
        if (openErr != ChdError.Chderrnone || chd == null)
        {
            if (header.MetaOffset != 0)
                log.Warning("  Cannot open for metadata listing: {Error}", openErr);
            return;
        }

        using (chd)
        {
            if (header.MetaOffset != 0)
            {
                // chdman prints each metadata entry with its per-tag index (0, 1, 2...)
                var tagIndices = new Dictionary<string, uint>(StringComparer.Ordinal);
                foreach (var meta in chd.Metadata)
                {
                    var index = tagIndices.GetValueOrDefault(meta.Tag);
                    tagIndices[meta.Tag] = index + 1;

                    var tagDisplay = IsPrintableTag(meta.Tag)
                        ? $"'{meta.Tag}'"
                        : $"0x{TagValue(meta.Tag):X8}";
                    Console.WriteLine(
                        $"Metadata:     Tag={tagDisplay}  Index={index}  Length={meta.Data.Length} bytes"
                    );

                    // Print data preview (up to 60 chars, or full if verbose)
                    var count = verbose ? meta.Data.Length : Math.Min(60, meta.Data.Length);
                    var preview = new StringBuilder();
                    for (var ci = 0; ci < count; ci++)
                    {
                        var b = meta.Data[ci];
                        preview.Append(b is >= 32 and < 127 ? (char)b : '.');
                    }

                    Console.WriteLine($"              {preview}");
                }
            }

            if (verbose)
            {
                var compressionTypes = new Dictionary<string, int>(StringComparer.Ordinal);
                var hunkCount = chd.HunkCount;
                for (uint hunkNum = 0; hunkNum < hunkCount; hunkNum++)
                {
                    var hunkErr = chd.ReadHunk(hunkNum, new byte[chd.HunkBytes]);
                    var codecName = hunkErr == ChdError.Chderrnone ? "data" : "error";
                    compressionTypes[codecName] = compressionTypes.GetValueOrDefault(codecName) + 1;
                }

                Console.WriteLine();
                Console.WriteLine("     Hunks  Percent  Name");
                Console.WriteLine("----------  -------  ------------------------------------");
                foreach (var kv in compressionTypes.OrderByDescending(k => k.Value))
                {
                    var pct = 100.0 * kv.Value / hunkCount;
                    Console.WriteLine($"{kv.Value,10}   {pct,5:F1}%  {kv.Key}");
                }
            }
        }
    }

    /// <summary>Formats a number with comma thousands separators (chdman parity: "65,536", not "065,536").</summary>
    private static string BigintString(ulong value)
    {
        if (value == 0)
            return "0";

        var chunks = new List<string>();
        while (value != 0)
        {
            chunks.Add((value % 1000).ToString());
            value /= 1000;
        }

        // most-significant chunk first, no leading zeros; later chunks are zero-padded to 3
        var sb = new StringBuilder(chunks[^1]);
        for (var i = chunks.Count - 2; i >= 0; i--)
        {
            sb.Append(',');
            var chunk = chunks[i];
            for (var pad = chunk.Length; pad < 3; pad++)
                sb.Append('0');
            sb.Append(chunk);
        }

        return sb.ToString();
    }

    /// <summary>Formats codec tags like chdman: "zlib (Deflate), lzma (LZMA)" or "none".</summary>
    private static string CompressionString(IReadOnlyList<ChdCodec> codecs)
    {
        var active = codecs.Where(c => c != ChdCodec.None).ToArray();
        if (active.Length == 0)
            return "none";

        return string.Join(
            ", ",
            active.Select(c =>
            {
                var name = CodecTagName(c);
                var desc = CodecDescription(c);
                return $"{name} ({desc})";
            })
        );
    }

    private static string CodecDescription(ChdCodec codec)
    {
        return codec switch
        {
            ChdCodec.Zlib => "Deflate",
            ChdCodec.Lzma => "LZMA",
            ChdCodec.Zstd => "Zstandard",
            ChdCodec.Huffman => "Huffman",
            ChdCodec.Flac => "FLAC",
            ChdCodec.Cdzlib => "CD Deflate",
            ChdCodec.Cdlzma => "CD LZMA",
            ChdCodec.Cdzstd => "CD Zstandard",
            ChdCodec.Cdflac => "CD FLAC",
            _ => CodecTagName(codec)
        };
    }

    private static string CodecTagName(ChdCodec codec)
    {
        Span<char> chars = stackalloc char[4];
        var value = (uint)codec;
        chars[0] = (char)((value >> 24) & 0xFF);
        chars[1] = (char)((value >> 16) & 0xFF);
        chars[2] = (char)((value >> 8) & 0xFF);
        chars[3] = (char)(value & 0xFF);
        return new string(chars);
    }

    private static uint TagValue(string tag)
    {
        var bytes = Encoding.ASCII.GetBytes(tag);
        if (bytes.Length != 4)
            return 0;

        return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
    }

    private static bool IsPrintableTag(string tag)
    {
        return tag.All(c => c >= 32 && c < 127);
    }

    /// <summary>
    ///     Dumps a metadata entry (chdman <c>dumpmeta</c> parity): prints text entries to the
    ///     console, writes the raw payload to <c>-o</c> when given.
    /// </summary>
    private static void DumpMetaTest(string[] args)
    {
        var log = Log.Logger;
        string? file = null;
        string? tag = null;
        uint index = 0;
        string? outFile = null;
        var force = false;
        for (var i = 0; i < args.Length; i++)
            switch (args[i])
            {
                case "--input" or "-i" when i + 1 < args.Length:
                    file = args[++i].Replace("\"", "");
                    break;
                case "--tag" or "-t" when i + 1 < args.Length:
                    tag = args[++i];
                    break;
                case "--index" or "-ix" when i + 1 < args.Length:
                    if (!uint.TryParse(args[++i], out index))
                    {
                        log.Warning("Invalid metadata index: {Value}", args[i]);
                        return;
                    }

                    break;
                case "--output" or "-o" when i + 1 < args.Length:
                    outFile = args[++i];
                    break;
                case "--force" or "-f":
                    force = true;
                    break;
                default:
                    if (file == null && !args[i].StartsWith('-'))
                    {
                        file = args[i].Replace("\"", "");
                        break;
                    }

                    log.Warning("Unknown option: {Option}", args[i]);
                    return;
            }

        if (file == null)
        {
            log.Warning("dumpmeta requires --input <file>");
            return;
        }

        var err = ChdFile.Open(file, out var chd);
        if (err != ChdError.Chderrnone || chd == null)
        {
            log.Warning("dumpmeta: open failed: {Error}", err);
            return;
        }

        using (chd)
        {
            err = chd.GetMetadata(tag, index, out var entry);
            if (err != ChdError.Chderrnone || entry == null)
            {
                log.Warning("dumpmeta: {Error}", err);
                return;
            }

            log.Information(
                "{Tag} flags=0x{Flags:X2} length={Length}",
                entry.Tag,
                entry.Flags,
                entry.Data.Length
            );
            if (outFile != null)
            {
                if (File.Exists(outFile) && !force)
                {
                    log.Warning(
                        "Output file already exists: {Path} (use --force to overwrite)",
                        outFile
                    );
                    return;
                }

                File.WriteAllBytes(outFile, entry.Data);
                log.Information("  Wrote {Length} bytes to {Path}", entry.Data.Length, outFile);
            }
            else if (entry.IsText)
            {
                log.Information("{Text}", entry.GetText());
            }
            else
            {
                log.Information("  (binary payload; use -o to write it to a file)");
            }
        }
    }

    /// <summary>
    ///     Computes hashes over a CHD's content (CHDlite <c>hash_content</c> parity) with
    ///     text/JSON/SFV output, optionally per-track for CD images.
    /// </summary>
    private static void HashTest(string[] args)
    {
        var log = Log.Logger;
        string? file = null;
        var hashes = ChdHashType.Sha1;
        var format = "text";
        var perTrack = false;
        for (var i = 0; i < args.Length; i++)
            switch (args[i])
            {
                case "--input" or "-i" when i + 1 < args.Length:
                    file = args[++i].Replace("\"", "");
                    break;
                case "--hashes" when i + 1 < args.Length:
                {
                    var types = ChdHashType.None;
                    foreach (
                        var name in args[++i]
                            .Split(
                                ',',
                                StringSplitOptions.RemoveEmptyEntries
                                | StringSplitOptions.TrimEntries
                            )
                    )
                        switch (name.ToLowerInvariant())
                        {
                            case "sha1":
                                types |= ChdHashType.Sha1;
                                break;
                            case "sha256":
                                types |= ChdHashType.Sha256;
                                break;
                            case "crc32":
                                types |= ChdHashType.Crc32;
                                break;
                            case "xxh3":
                                types |= ChdHashType.Xxh3;
                                break;
                            default:
                                log.Warning(
                                    "Unknown hash type: {Name} (valid: sha1, sha256, crc32, xxh3)",
                                    name
                                );
                                return;
                        }

                    hashes = types;
                    break;
                }
                case "--result" when i + 1 < args.Length:
                    format = args[++i].ToLowerInvariant();
                    if (format is not ("text" or "json" or "sfv"))
                    {
                        log.Warning("Invalid result format [{Format}] (text|json|sfv)", format);
                        return;
                    }

                    break;
                case "--tracks":
                    perTrack = true;
                    break;
                default:
                    if (file == null && !args[i].StartsWith('-'))
                    {
                        file = args[i].Replace("\"", "");
                        break;
                    }

                    log.Warning("Unknown option: {Option}", args[i]);
                    return;
            }

        if (file == null)
        {
            log.Warning("hash requires --input <file>");
            return;
        }

        IReadOnlyList<ChdHashResult> results;
        try
        {
            results = Chd.ComputeHashes(file, hashes, perTrack: perTrack);
        }
        catch (Exception ex)
            when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            log.Warning("hash failed: {Message}", ex.Message);
            return;
        }

        switch (format)
        {
            case "json":
            {
                var jsonArray = new JsonArray();
                foreach (var r in results)
                {
                    var obj = new JsonObject
                    {
                        ["track"] = r.TrackNumber,
                        ["offset"] = r.StartOffset,
                        ["length"] = r.Length
                    };
                    if (r.Sha1 != null)
                        obj["sha1"] = r.ToHex(ChdHashType.Sha1);

                    if (r.Sha256 != null)
                        obj["sha256"] = r.ToHex(ChdHashType.Sha256);

                    if (r.Crc32 != null)
                        obj["crc32"] = r.ToHex(ChdHashType.Crc32);

                    if (r.Xxh3 != null)
                        obj["xxh3"] = r.ToHex(ChdHashType.Xxh3);

                    jsonArray.Add(obj);
                }

                log.Information("{Json}", jsonArray.ToJsonString());
                break;
            }
            case "sfv":
                foreach (var r in results)
                {
                    var name = r.TrackNumber is { } tn
                        ? $"track{tn:D2}.bin"
                        : Path.GetFileName(file);
                    if (r.Crc32 is { } crc)
                        log.Information("{Name} {Crc:X8}", name, crc);
                    else
                        log.Warning("sfv output requires crc32; use --hashes crc32");
                }

                break;
            default:
                foreach (var r in results)
                {
                    var prefix = r.TrackNumber is { } trackNum
                        ? $"track {trackNum:D2}"
                        : "whole file";
                    log.Information("{Prefix}:", prefix);
                    if (r.Sha1 != null)
                        log.Information("  SHA-1:   {Hash}", r.ToHex(ChdHashType.Sha1));
                    if (r.Sha256 != null)
                        log.Information("  SHA-256: {Hash}", r.ToHex(ChdHashType.Sha256));
                    if (r.Crc32 != null)
                        log.Information("  CRC-32:  {Hash}", r.ToHex(ChdHashType.Crc32));
                    if (r.Xxh3 != null)
                        log.Information("  XXH3:    {Hash}", r.ToHex(ChdHashType.Xxh3));
                }

                break;
        }
    }

    /// <summary>
    ///     Batch mode (CHDlite <c>cmd_auto_batch</c> parity): scans a directory for
    ///     .chd/.cue/.gdi/.iso inputs and extracts or creates CHDs with a bounded worker pool.
    /// </summary>
    private static void BatchTest(string inputDir, string outputDir, string[] options)
    {
        var log = Log.Logger;
        var action = "extract";
        string? codecs = null;
        for (var i = 0; i < options.Length; i++)
            switch (options[i])
            {
                case "--action" when i + 1 < options.Length:
                    action = options[++i].ToLowerInvariant();
                    if (action is not ("extract" or "create"))
                    {
                        log.Warning("Invalid action [{Action}] (extract|create)", action);
                        return;
                    }

                    break;
                case "--compression" or "-c" or "--codecs" when i + 1 < options.Length:
                    codecs = options[++i];
                    break;
                default:
                    log.Warning("Unknown option: {Option}", options[i]);
                    return;
            }

        if (!Directory.Exists(inputDir))
        {
            log.Warning("Input directory not found: {Path}", inputDir);
            return;
        }

        Directory.CreateDirectory(outputDir);

        var files = new List<string>();
        if (string.Equals(action, "extract", StringComparison.Ordinal))
            // Extract mode: only .chd files can be extracted
            files.AddRange(Directory.GetFiles(inputDir, "*.chd", SearchOption.TopDirectoryOnly));
        else
            // Create mode: .cue/.gdi/.iso are source formats; .chd files should not be re-encoded
            foreach (var pattern in new[] { "*.cue", "*.gdi", "*.iso" })
                files.AddRange(
                    Directory.GetFiles(inputDir, pattern, SearchOption.TopDirectoryOnly)
                );

        if (files.Count == 0)
        {
            log.Warning(
                "No {Files} found in {Path}",
                string.Equals(action, "extract", StringComparison.Ordinal)
                    ? ".chd files"
                    : ".cue/.gdi/.iso files",
                inputDir
            );
            return;
        }

        // concurrent = clamp(cores/4, 1..4), like CHDlite's auto-batch.
        var workers = Math.Clamp(Environment.ProcessorCount / 4, 1, 4);
        var queue = new ConcurrentQueue<string>(files);
        var failures = new ConcurrentQueue<string>();
        var processed = 0;
        log.Information(
            "Batch {Action}: {Count} files, {Workers} workers",
            action,
            files.Count,
            workers
        );

        Parallel.For(
            0,
            workers,
            _ =>
            {
                while (queue.TryDequeue(out var input))
                    try
                    {
                        var baseName = Path.GetFileNameWithoutExtension(input);
                        if (string.Equals(action, "extract", StringComparison.Ordinal))
                        {
                            var err = ChdFile.Open(input, out var chd);
                            if (err != ChdError.Chderrnone || chd == null)
                                throw new InvalidDataException($"open failed: {err}");

                            using (chd)
                            {
                                chd.ExtractToDirectory(outputDir, baseName);
                            }
                        }
                        else
                        {
                            var codecTags = ChdCodecs.ParseCodecTags(codecs);
                            if (
                                input.EndsWith(".cue", StringComparison.OrdinalIgnoreCase)
                                || input.EndsWith(".gdi", StringComparison.OrdinalIgnoreCase)
                                || input.EndsWith(".iso", StringComparison.OrdinalIgnoreCase)
                            )
                            {
                                var outChd = Path.Combine(outputDir, baseName + ".chd");
                                ChdEncoder.EncodeCd(input, outChd, codecTags: codecTags);
                            }
                            else
                            {
                                var outChd = Path.Combine(outputDir, baseName + ".chd");
                                ChdEncoder.EncodeRaw(input, outChd, codecTags: codecTags);
                            }
                        }

                        Interlocked.Increment(ref processed);
                        log.Information(
                            "[{Done}/{Total}] {Action}: {Name}",
                            processed,
                            files.Count,
                            action,
                            Path.GetFileName(input)
                        );
                    }
                    catch (Exception ex)
                        when (ex
                                  is InvalidDataException
                                  or IOException
                                  or ArgumentException
                                  or UnauthorizedAccessException
                             )
                    {
                        failures.Enqueue($"{Path.GetFileName(input)}: {ex.Message}");
                        log.Warning(
                            "  FAIL: {Name}: {Message}",
                            Path.GetFileName(input),
                            ex.Message
                        );
                    }
            }
        );

        log.Information(
            "Batch complete: {Done} processed, {Failures} failed",
            processed,
            failures.Count
        );
        foreach (var f in failures)
            log.Information("  FAIL: {Failure}", f);
    }

    /// <summary>Adds or replaces a metadata entry (chdman <c>addmeta</c> parity).</summary>
    private static void AddMetaTest(string[] args)
    {
        var log = Log.Logger;
        string? file = null;
        string? tag = null;
        string? text = null;
        string? inputFile = null;
        uint index = 0;
        var noChecksum = false;
        for (var i = 0; i < args.Length; i++)
            switch (args[i])
            {
                case "--input" or "-i" when i + 1 < args.Length:
                    file = args[++i].Replace("\"", "");
                    break;
                case "--tag" or "-t" when i + 1 < args.Length:
                    tag = args[++i];
                    break;
                case "--valuetext" or "-vt" when i + 1 < args.Length:
                    text = args[++i];
                    break;
                case "--valuefile" or "-vf" when i + 1 < args.Length:
                    inputFile = args[++i];
                    break;
                case "--index" or "-ix" when i + 1 < args.Length:
                    if (!uint.TryParse(args[++i], out index))
                    {
                        log.Warning("Invalid metadata index: {Value}", args[i]);
                        return;
                    }

                    break;
                case "--nochecksum" or "-nocs":
                    noChecksum = true;
                    break;
                default:
                    if (file == null && !args[i].StartsWith('-'))
                    {
                        file = args[i].Replace("\"", "");
                        break;
                    }

                    log.Warning("Unknown option: {Option}", args[i]);
                    return;
            }

        if (file == null)
        {
            log.Warning("addmeta requires --input <file>");
            return;
        }

        if (tag is not { Length: 4 })
        {
            log.Warning("addmeta requires a 4-character tag (--tag)");
            return;
        }

        if (text != null && inputFile != null)
        {
            log.Warning("addmeta: specify either --valuetext or --valuefile, not both");
            return;
        }

        byte[] data;
        if (inputFile != null)
        {
            try
            {
                data = File.ReadAllBytes(inputFile);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                log.Warning("Cannot read metadata file {Path}: {Message}", inputFile, ex.Message);
                return;
            }
        }
        else
        {
            text ??= "";
            if (text.Any(c => c > 127))
            {
                log.Warning(
                    "addmeta: --valuetext contains non-ASCII characters; use --valuefile with a binary file instead"
                );
                return;
            }

            // chdman addmeta --valuetext writes exactly text.size() bytes with no NUL
            // (chdman.cpp:3266), not a C-string terminator
            data = Encoding.ASCII.GetBytes(text);
        }

        var err = ChdFile.Open(file, out var chd);
        if (err != ChdError.Chderrnone || chd == null)
        {
            log.Warning("addmeta: open failed: {Error}", err);
            return;
        }

        using (chd)
        {
            var flags = noChecksum ? (byte)0 : ChdFile.MetadataChecksumFlag;
            err = chd.SetMetadata(tag, data, index, flags);
            if (err != ChdError.Chderrnone)
            {
                log.Warning("addmeta failed: {Error}", err);
                return;
            }

            log.Information(
                "  Added/replaced {Tag} (index {Index}, {Length} bytes)",
                tag,
                index,
                data.Length
            );
        }
    }

    /// <summary>Deletes a metadata entry (chdman <c>delmeta</c> parity).</summary>
    private static void DeleteMetaTest(string[] args)
    {
        var log = Log.Logger;
        string? file = null;
        string? tag = null;
        uint index = 0;
        for (var i = 0; i < args.Length; i++)
            switch (args[i])
            {
                case "--input" or "-i" when i + 1 < args.Length:
                    file = args[++i].Replace("\"", "");
                    break;
                case "--tag" or "-t" when i + 1 < args.Length:
                    tag = args[++i];
                    break;
                case "--index" or "-ix" when i + 1 < args.Length:
                    if (!uint.TryParse(args[++i], out index))
                    {
                        log.Warning("Invalid metadata index: {Value}", args[i]);
                        return;
                    }

                    break;
                default:
                    if (file == null && !args[i].StartsWith('-'))
                    {
                        file = args[i].Replace("\"", "");
                        break;
                    }

                    log.Warning("Unknown option: {Option}", args[i]);
                    return;
            }

        if (file == null)
        {
            log.Warning("delmeta requires --input <file>");
            return;
        }

        if (tag is not { Length: 4 })
        {
            log.Warning("delmeta requires a 4-character tag (--tag)");
            return;
        }

        var err = ChdFile.Open(file, out var chd);
        if (err != ChdError.Chderrnone || chd == null)
        {
            log.Warning("delmeta: open failed: {Error}", err);
            return;
        }

        using (chd)
        {
            err = chd.DeleteMetadata(tag, index);
            if (err != ChdError.Chderrnone)
            {
                log.Warning("delmeta failed: {Error}", err);
                return;
            }

            log.Information("  Deleted {Tag} (index {Index})", tag, index);
        }
    }

    /// <summary>
    ///     Parses a number string with an optional K/M/G suffix (e.g. "10M" = 10485760).
    ///     Matches MAME chdman's <c>parse_number()</c> behaviour.
    /// </summary>
    private static bool TryParseSizeWithSuffix(string s, out uint result)
    {
        if (TryParseSizeWithSuffix(s, out long r) && r is >= 0 and <= uint.MaxValue)
        {
            result = (uint)r;
            return true;
        }

        result = 0;
        return false;
    }

    /// <summary>
    ///     Parses a number string with an optional K/M/G suffix (e.g. "10M" = 10485760).
    ///     Matches MAME chdman's <c>parse_number()</c> behaviour.
    /// </summary>
    private static bool TryParseSizeWithSuffix(string s, out long result)
    {
        result = 0;
        if (string.IsNullOrEmpty(s))
            return false;

        s = s.Trim();
        long multiplier = 1;
        var digits = s;

        if (s.Length > 1)
        {
            var last = s[^1];
            switch (last)
            {
                case 'k' or 'K':
                    multiplier = 1024;
                    digits = s[..^1];
                    break;
                case 'm' or 'M':
                    multiplier = 1024 * 1024;
                    digits = s[..^1];
                    break;
                case 'g' or 'G':
                    multiplier = 1024L * 1024 * 1024;
                    digits = s[..^1];
                    break;
            }
        }

        if (!long.TryParse(digits, out var num) || num < 0)
            return false;

        try
        {
            result = checked(num * multiplier);
        }
        catch (OverflowException)
        {
            return false;
        }

        return true;
    }

    /// <summary>Normalizes a command name: strips leading <c>--</c> and maps aliases.</summary>
    private static string? NormalizeCommand(string? raw)
    {
        if (raw == null)
            return null;

        var cmd = raw.TrimStart('-');
        // Map legacy aliases
        return cmd switch
        {
            "h" or "?" or "help" => "help",
            _ => cmd
        };
    }

    /// <summary>Gets an input file path from a named <c>--input</c>/<c>-i</c> flag or a positional argument.</summary>
    private static string ParseInput(string[] args, int positionalIndex)
    {
        // Check for --input/-i flag first
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i] is "--input" or "-i")
                return args[i + 1].Replace("\"", "");

        // Fall back to positional
        var pos = 0;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith('-'))
                continue;

            if (pos == positionalIndex)
                return args[i].Replace("\"", "");

            pos++;
        }

        return "";
    }

    /// <summary>
    ///     Parses <c>--input</c>/<c>-i</c> and <c>--output</c>/<c>-o</c> named flags, falling back to
    ///     positional arguments. Returns (input, output, remainingOptions).
    /// </summary>
    private static (string? input, string? output, string[] rest) ParseCreateArgs(string[] args)
    {
        string? input = null;
        string? output = null;
        var rest = new List<string>();

        for (var i = 0; i < args.Length; i++)
            switch (args[i])
            {
                case "--input" or "-i" when i + 1 < args.Length:
                    input = args[++i].Replace("\"", "");
                    break;
                case "--output" or "-o" when i + 1 < args.Length:
                    output = args[++i].Replace("\"", "");
                    break;
                default:
                    rest.Add(args[i]);
                    break;
            }

        // Fall back to positional args, but only scan the LEADING non-option tokens.
        // Legacy style puts <input> <output> before any flags; anything after the first
        // '-'-prefixed token is an option or an option value and must not be stolen
        // (e.g. "--size 1048576" would otherwise lose its value to this fallback).
        var positionalIdx = 0;
        while (positionalIdx < rest.Count && !rest[positionalIdx].StartsWith('-'))
            positionalIdx++;

        if (input == null && positionalIdx > 0)
        {
            input = rest[0].Replace("\"", "");
            rest.RemoveAt(0);
            positionalIdx--;
        }

        if (output == null && positionalIdx > 0)
        {
            output = rest[0].Replace("\"", "");
            rest.RemoveAt(0);
        }

        return (input, output, rest.ToArray());
    }

    /// <summary>Prints the main usage text in chdman style.</summary>
    private static void PrintUsage()
    {
        var exe = Path.GetFileNameWithoutExtension(Environment.GetCommandLineArgs()[0]);
        Log.Logger.Information("{Exe} - CHDSharp Compressed Hunks of Data (CHD) manager", exe);
        Log.Logger.Information("Usage:");
        Log.Logger.Information("   {Exe} info: displays information about a CHD", exe);
        Log.Logger.Information("   {Exe} verify: verifies a CHD's integrity", exe);
        Log.Logger.Information("   {Exe} createraw: create a raw CHD from the input file", exe);
        Log.Logger.Information(
            "   {Exe} createhd: create a hard disk CHD from the input file",
            exe
        );
        Log.Logger.Information("   {Exe} createcd: create a CD CHD from the input file", exe);
        Log.Logger.Information("   {Exe} createdvd: create a DVD CHD from the input file", exe);
        Log.Logger.Information(
            "   {Exe} createld: create a laserdisc CHD from the input file",
            exe
        );
        Log.Logger.Information("   {Exe} extractraw: extract raw file from a CHD input file", exe);
        Log.Logger.Information(
            "   {Exe} extracthd: extract raw hard disk file from a CHD input file",
            exe
        );
        Log.Logger.Information("   {Exe} extractcd: extract CD file from a CHD input file", exe);
        Log.Logger.Information("   {Exe} extractdvd: extract DVD file from a CHD input file", exe);
        Log.Logger.Information(
            "   {Exe} extractld: extract laserdisc AVI from a CHD input file",
            exe
        );
        Log.Logger.Information(
            "   {Exe} copy: copy data from one CHD to another of the same type",
            exe
        );
        Log.Logger.Information("   {Exe} addmeta: add metadata to the CHD", exe);
        Log.Logger.Information("   {Exe} delmeta: remove metadata from the CHD", exe);
        Log.Logger.Information(
            "   {Exe} dumpmeta: dump metadata from the CHD to stdout or to a file",
            exe
        );
        Log.Logger.Information("   {Exe} listtemplates: list hard disk templates", exe);
        Log.Logger.Information("");
        Log.Logger.Information("For help with any command, run:");
        Log.Logger.Information("   {Exe} help <command>", exe);
    }

    /// <summary>Prints detailed help for a specific command.</summary>
    private static void PrintCommandHelp(string command)
    {
        var cmd = NormalizeCommand(command) ?? command;
        var exe = Path.GetFileNameWithoutExtension(Environment.GetCommandLineArgs()[0]);
        switch (cmd)
        {
            case "info":
                Log.Logger.Information("{Exe} info --input <file> [--verbose]", exe);
                Log.Logger.Information("  Displays information about a CHD file.");
                Log.Logger.Information("  --input, -i    Input CHD file (required)");
                Log.Logger.Information("  --verbose, -v  Additional information");
                break;
            case "verify":
                Log.Logger.Information(
                    "{Exe} verify --input <file> [--inputparent <file>] [--fix]",
                    exe
                );
                Log.Logger.Information("  Verifies a CHD's integrity.");
                Log.Logger.Information("  --input, -i        Input CHD file (required)");
                Log.Logger.Information("  --inputparent, -ip Parent CHD file");
                Log.Logger.Information("  --fix, -f          Fix mismatched SHA-1 header fields");
                break;
            case "createraw" or "create":
                Log.Logger.Information(
                    "{Exe} createraw --output <file> --input <file> [options]",
                    exe
                );
                Log.Logger.Information("  Create a raw CHD from the input file.");
                Log.Logger.Information("  --output, -o         Output CHD file (required)");
                Log.Logger.Information("  --input, -i          Input file (required)");
                Log.Logger.Information("  --outputparent, -op  Output parent CHD");
                Log.Logger.Information(
                    "  --compression, -c    Codecs (default: lzma,zlib,huff,flac)"
                );
                Log.Logger.Information("  --hunksize, -hs      Hunk size in bytes");
                Log.Logger.Information("  --unitsize, -us      Unit size in bytes");
                Log.Logger.Information(
                    "  --inputstartbyte, -isb  Starting byte offset within input"
                );
                Log.Logger.Information(
                    "  --inputstarthunk, -ish  Starting hunk offset within input"
                );
                Log.Logger.Information("  --inputbytes, -ib    Effective length of input in bytes");
                Log.Logger.Information("  --inputhunks, -ih    Effective length of input in hunks");
                Log.Logger.Information("  --numprocessors, -np Parallel workers");
                Log.Logger.Information("  --template, -tp      Hard disk template ID");
                Log.Logger.Information("  --dvd, -d            Force DVD metadata");
                Log.Logger.Information("  --force, -f          Overwrite existing output");
                Log.Logger.Information("  --verbose, -v        Per-hunk compression logging");
                break;
            case "createhd":
                Log.Logger.Information(
                    "{Exe} createhd --output <file> [--input <file>] [options]",
                    exe
                );
                Log.Logger.Information(
                    "  Create a hard disk CHD. If --input is omitted, creates a blank zero-filled image."
                );
                Log.Logger.Information("  --output, -o         Output CHD file (required)");
                Log.Logger.Information(
                    "  --input, -i          Input file (optional; omit for blank)"
                );
                Log.Logger.Information("  --outputparent, -op  Output parent CHD");
                Log.Logger.Information(
                    "  --size, -s           Size of blank image (supports K/M/G suffixes)"
                );
                Log.Logger.Information(
                    "  --chs, -chs          CHS geometry: cylinders,heads,sectors"
                );
                Log.Logger.Information(
                    "  --sectorsize, -ss    Sector size in bytes (default: 512)"
                );
                Log.Logger.Information("  --ident, -id         512-byte ATA IDENTIFY DEVICE file");
                Log.Logger.Information("  --compression, -c    Codecs (default: none for blank)");
                Log.Logger.Information("  --hunksize, -hs      Hunk size in bytes");
                Log.Logger.Information("  --template, -tp      Hard disk template ID");
                Log.Logger.Information(
                    "  --inputstartbyte, -isb  Starting byte offset within input"
                );
                Log.Logger.Information(
                    "  --inputstarthunk, -ish  Starting hunk offset within input"
                );
                Log.Logger.Information("  --inputbytes, -ib    Effective length of input in bytes");
                Log.Logger.Information("  --inputhunks, -ih    Effective length of input in hunks");
                Log.Logger.Information("  --numprocessors, -np Parallel workers");
                Log.Logger.Information("  --force, -f          Overwrite existing output");
                Log.Logger.Information("  --verbose, -v        Per-hunk compression logging");
                break;
            case "createcd":
                Log.Logger.Information(
                    "{Exe} createcd --output <file> --input <file> [options]",
                    exe
                );
                Log.Logger.Information("  Create a CD CHD from CUE/GDI/ISO/TOC/NRG input.");
                Log.Logger.Information("  --output, -o         Output CHD file (required)");
                Log.Logger.Information(
                    "  --input, -i          Input file (required): .cue, .gdi, .iso, .toc, .nrg, .cdr, .toast"
                );
                Log.Logger.Information("  --outputparent, -op  Output parent CHD");
                Log.Logger.Information("  --compression, -c    Codecs (default: cdlz,cdzl,cdfl)");
                Log.Logger.Information("  --hunksize, -hs      Hunk size in bytes");
                Log.Logger.Information("  --numprocessors, -np Parallel workers");
                Log.Logger.Information("  --force, -f          Overwrite existing output");
                Log.Logger.Information("  --verbose, -v        Per-hunk compression logging");
                break;
            case "createdvd":
                Log.Logger.Information(
                    "{Exe} createdvd --output <file> --input <file> [options]",
                    exe
                );
                Log.Logger.Information("  Create a DVD CHD from the input file.");
                Log.Logger.Information("  --output, -o         Output CHD file (required)");
                Log.Logger.Information(
                    "  --input, -i          Input file (required): typically .iso"
                );
                Log.Logger.Information("  --outputparent, -op  Output parent CHD");
                Log.Logger.Information(
                    "  --compression, -c    Codecs (default: lzma,zlib,huff,flac)"
                );
                Log.Logger.Information("  --hunksize, -hs      Hunk size in bytes");
                Log.Logger.Information(
                    "  --inputstartbyte, -isb  Starting byte offset within input"
                );
                Log.Logger.Information(
                    "  --inputstarthunk, -ish  Starting hunk offset within input"
                );
                Log.Logger.Information("  --inputbytes, -ib    Effective length of input in bytes");
                Log.Logger.Information("  --inputhunks, -ih    Effective length of input in hunks");
                Log.Logger.Information("  --numprocessors, -np Parallel workers");
                Log.Logger.Information("  --force, -f          Overwrite existing output");
                Log.Logger.Information("  --verbose, -v        Per-hunk compression logging");
                break;
            case "createld":
                Log.Logger.Information(
                    "{Exe} createld --output <file> --input <file> [options]",
                    exe
                );
                Log.Logger.Information("  Create a laserdisc CHD from an AVI file.");
                Log.Logger.Information("  --output, -o             Output CHD file (required)");
                Log.Logger.Information("  --input, -i              Input AVI file (required)");
                Log.Logger.Information("  --outputparent, -op      Output parent CHD");
                Log.Logger.Information("  --compression, -c        Codecs (default: avhu)");
                Log.Logger.Information("  --inputstartframe, -isf  Starting frame");
                Log.Logger.Information("  --inputframes, -if       Frame count");
                Log.Logger.Information("  --hunksize, -hs          Hunk size in bytes");
                Log.Logger.Information("  --numprocessors, -np     Parallel workers");
                Log.Logger.Information("  --force, -f              Overwrite existing output");
                Log.Logger.Information("  --verbose, -v            Per-hunk compression logging");
                break;
            case "extractraw":
                Log.Logger.Information(
                    "{Exe} extractraw --output <file> --input <file> [options]",
                    exe
                );
                Log.Logger.Information("  Extract raw file from a CHD input file.");
                Log.Logger.Information("  --output, -o             Output file (required)");
                Log.Logger.Information("  --input, -i              Input CHD file (required)");
                Log.Logger.Information("  --inputparent, -ip       Parent CHD file");
                Log.Logger.Information("  --inputstartbyte, -isb   Starting byte offset");
                Log.Logger.Information("  --inputstarthunk, -ish   Starting hunk offset");
                Log.Logger.Information("  --inputbytes, -ib        Byte count to extract");
                Log.Logger.Information("  --inputhunks, -ih        Hunk count to extract");
                Log.Logger.Information("  --force, -f              Overwrite existing output");
                break;
            case "extracthd":
                Log.Logger.Information(
                    "{Exe} extracthd --output <file> --input <file> [options]",
                    exe
                );
                Log.Logger.Information("  Extract raw hard disk file from a CHD input file.");
                Log.Logger.Information("  (Same options as extractraw)");
                break;
            case "extractcd":
                Log.Logger.Information(
                    "{Exe} extractcd --output <file> --input <file> [options]",
                    exe
                );
                Log.Logger.Information("  Extract CD file from a CHD input file.");
                Log.Logger.Information("  --output, -o          Output CUE file (required)");
                Log.Logger.Information("  --input, -i           Input CHD file (required)");
                Log.Logger.Information("  --outputbin, -ob      Output BIN file name");
                Log.Logger.Information("  --splitbin, -sb       One binary file per track");
                Log.Logger.Information("  --inputparent, -ip    Parent CHD file");
                Log.Logger.Information("  --force, -f           Overwrite existing output");
                break;
            case "extractdvd":
                Log.Logger.Information(
                    "{Exe} extractdvd --output <file> --input <file> [options]",
                    exe
                );
                Log.Logger.Information("  Extract DVD file from a CHD input file.");
                Log.Logger.Information("  (Same options as extractraw)");
                break;
            case "extractld":
                Log.Logger.Information(
                    "{Exe} extractld --output <file> --input <file> [options]",
                    exe
                );
                Log.Logger.Information("  Extract laserdisc AVI from a CHD input file.");
                Log.Logger.Information("  --output, -o              Output AVI file (required)");
                Log.Logger.Information("  --input, -i               Input CHD file (required)");
                Log.Logger.Information("  --inputparent, -ip        Parent CHD file");
                Log.Logger.Information("  --inputstartframe, -isf   Starting frame");
                Log.Logger.Information("  --inputframes, -if        Frame count");
                Log.Logger.Information("  --force, -f               Overwrite existing output");
                break;
            case "copy":
                Log.Logger.Information("{Exe} copy --output <file> --input <file> [options]", exe);
                Log.Logger.Information("  Copy data from one CHD to another of the same type.");
                Log.Logger.Information("  --output, -o         Output CHD file (required)");
                Log.Logger.Information("  --input, -i          Input CHD file (required)");
                Log.Logger.Information("  --inputparent, -ip   Source parent CHD");
                Log.Logger.Information("  --outputparent, -op  Output parent CHD");
                Log.Logger.Information("  --compression, -c    Codecs");
                Log.Logger.Information("  --hunksize, -hs      Hunk size in bytes");
                Log.Logger.Information(
                    "  --inputstartbyte, -isb  Starting byte offset within input"
                );
                Log.Logger.Information(
                    "  --inputstarthunk, -ish  Starting hunk offset within input"
                );
                Log.Logger.Information("  --inputbytes, -ib    Effective length of input in bytes");
                Log.Logger.Information("  --inputhunks, -ih    Effective length of input in hunks");
                Log.Logger.Information("  --numprocessors, -np Parallel workers");
                Log.Logger.Information("  --no-upgrade         Preserve legacy metadata tags");
                Log.Logger.Information("  --force, -f          Overwrite existing output");
                Log.Logger.Information("  --verbose, -v        Per-hunk compression logging");
                break;
            case "addmeta":
                Log.Logger.Information(
                    "{Exe} addmeta --input <file> --tag <tag> [--index <n>] (--valuetext <text> | --valuefile <file>)",
                    exe
                );
                Log.Logger.Information("  Add metadata to the CHD.");
                Log.Logger.Information("  --input, -i        Input CHD file (required)");
                Log.Logger.Information("  --tag, -t          4-character metadata tag (required)");
                Log.Logger.Information("  --index, -ix       Indexed instance of this tag");
                Log.Logger.Information("  --valuetext, -vt   Text value");
                Log.Logger.Information("  --valuefile, -vf   File containing data");
                Log.Logger.Information("  --nochecksum, -nocs  Exclude from combined SHA-1");
                break;
            case "delmeta":
                Log.Logger.Information(
                    "{Exe} delmeta --input <file> --tag <tag> [--index <n>]",
                    exe
                );
                Log.Logger.Information("  Remove metadata from the CHD.");
                Log.Logger.Information("  --input, -i        Input CHD file (required)");
                Log.Logger.Information("  --tag, -t          4-character metadata tag (required)");
                Log.Logger.Information("  --index, -ix       Indexed instance of this tag");
                break;
            case "dumpmeta":
                Log.Logger.Information(
                    "{Exe} dumpmeta --input <file> --tag <tag> [--output <file>] [--index <n>]",
                    exe
                );
                Log.Logger.Information("  Dump metadata from the CHD to stdout or to a file.");
                Log.Logger.Information("  --input, -i        Input CHD file (required)");
                Log.Logger.Information("  --tag, -t          4-character metadata tag (required)");
                Log.Logger.Information("  --output, -o       Output file for binary data");
                Log.Logger.Information("  --index, -ix       Indexed instance of this tag");
                Log.Logger.Information("  --force, -f        Overwrite existing output");
                break;
            case "listtemplates":
                Log.Logger.Information("{Exe} listtemplates", exe);
                Log.Logger.Information("  List built-in hard disk geometry templates.");
                break;
            case "random":
                Log.Logger.Information("{Exe} random <file> [count]", exe);
                Log.Logger.Information(
                    "  Random-access stress test: reads random hunks from the CHD."
                );
                Log.Logger.Information("  <file>   Input CHD file (positional, required)");
                Log.Logger.Information("  [count]  Number of random reads (default: 1000)");
                break;
            case "list":
                Log.Logger.Information("{Exe} list --input <file>", exe);
                Log.Logger.Information("  Lists all metadata entries in the CHD.");
                Log.Logger.Information("  --input, -i    Input CHD file (required)");
                break;
            case "parent":
                Log.Logger.Information("{Exe} parent --input <file>", exe);
                Log.Logger.Information(
                    "  Displays the parent SHA-1 hash for a child (differential) CHD."
                );
                Log.Logger.Information("  --input, -i    Input CHD file (required)");
                break;
            case "toc":
                Log.Logger.Information("{Exe} toc --input <file>", exe);
                Log.Logger.Information(
                    "  Displays the table of contents (track layout) for a CD CHD."
                );
                Log.Logger.Information("  --input, -i    Input CHD file (required)");
                break;
            case "cue":
                Log.Logger.Information("{Exe} cue --input <file> [--output <file>]", exe);
                Log.Logger.Information("  Generates a CUE sheet for a CD CHD.");
                Log.Logger.Information("  --input, -i    Input CHD file (required)");
                Log.Logger.Information("  --output, -o   Output CUE file (default: stdout)");
                break;
            case "classify":
                Log.Logger.Information("{Exe} classify --input <file>", exe);
                Log.Logger.Information(
                    "  Classifies the CHD type (CD, DVD, HDD, Laserdisc, etc.)."
                );
                Log.Logger.Information("  --input, -i    Input CHD file (required)");
                break;
            case "detect":
                Log.Logger.Information("{Exe} detect --input <file>", exe);
                Log.Logger.Information("  Detects the platform/region for a CD CHD.");
                Log.Logger.Information("  --input, -i    Input CHD file (required)");
                break;
            case "hash":
                Log.Logger.Information(
                    "{Exe} hash --input <file> [--hashes sha1,sha256,crc32,xxh3] [--format text|json|sfv] [--per-track]",
                    exe
                );
                Log.Logger.Information(
                    "  Computes content hashes over the CHD's decompressed data."
                );
                Log.Logger.Information("  --input, -i        Input CHD file (required)");
                Log.Logger.Information(
                    "  --hashes           Comma-separated hash types (default: sha1)"
                );
                Log.Logger.Information(
                    "  --format           Output format: text, json, sfv (default: text)"
                );
                Log.Logger.Information("  --per-track        Compute per-track hashes (CD only)");
                break;
            case "batch":
                Log.Logger.Information(
                    "{Exe} batch --input <dir> --output <dir> [--compression <codecs>] [--numprocessors <n>]",
                    exe
                );
                Log.Logger.Information(
                    "  Batch create/extract: processes all matching files in a directory."
                );
                Log.Logger.Information("  --input, -i        Input directory (required)");
                Log.Logger.Information("  --output, -o       Output directory (required)");
                Log.Logger.Information("  --compression, -c  Codecs for create mode");
                Log.Logger.Information("  --numprocessors, -np Parallel workers");
                break;
            default:
                Log.Logger.Information(
                    "Unknown command: {Command}. Run '{Exe} help' for a list of commands.",
                    command,
                    exe
                );
                break;
        }
    }

    /// <summary>Creates a DVD CHD from an input file (ISO). Forces DVD metadata and 2048-byte unit size.</summary>
    private static void CreateDvdTest(string inputPath, string outputPath, string[] options)
    {
        var log = Log.Logger;
        if (!File.Exists(inputPath))
        {
            log.Warning("createdvd: input file not found: {Path}", inputPath);
            return;
        }

        var hunkBytes = 4096u;
        var unitBytes = 2048u;
        string? codecs = null;
        string? parentPath = null;
        var verbose = false;
        var force = false;
        int? taskCount = null;
        var dvdDummy = false;
        int? templateDummy = null;
        long? inputStartBytes = null;
        long? inputLengthBytes = null;
        long? inputStartHunk = null;
        long? inputLengthHunks = null;
        long? inputStartFrame = null;
        long? inputLengthFrames = null;
        if (
            !TryParseOptions(
                options,
                ref hunkBytes,
                ref unitBytes,
                ref codecs,
                ref parentPath,
                ref verbose,
                ref taskCount,
                ref dvdDummy,
                ref templateDummy,
                ref inputStartBytes,
                ref inputLengthBytes,
                ref force,
                ref inputStartHunk,
                ref inputLengthHunks,
                ref inputStartFrame,
                ref inputLengthFrames
            )
        )
            return;

        if (File.Exists(outputPath) && !force)
        {
            log.Warning(
                "Output file already exists: {Path} (use --force to overwrite)",
                outputPath
            );
            return;
        }

        codecs ??= "lzma,zlib,huff,flac";
        unitBytes = 2048;

        try
        {
            var codecTags = ChdCodecs.ParseCodecTags(codecs);
            log.Information(
                "Creating DVD CHD: {Input} -> {Output}  (hunk {Hunk}B, unit {Unit}B, codecs {Codecs}{Parent}{Tasks})",
                Path.GetFileName(inputPath),
                outputPath,
                hunkBytes,
                unitBytes,
                string.Join(",", codecTags.Select(CodecTags.ToString)),
                parentPath != null ? $", parent {Path.GetFileName(parentPath)}" : "",
                taskCount.HasValue ? $", {taskCount} tasks" : ""
            );

            var logger = verbose ? new VerboseHunkLogger() : null;
            // DVD metadata must ALWAYS be written — createdvd exists to stamp the 'DVD ' tag.
            var encodeOptions = logger?.Options ?? new ChdEncodeOptions();
            if (taskCount.HasValue)
                encodeOptions.TaskCount = taskCount;

            if (parentPath != null)
                encodeOptions.ParentPath = parentPath;

            if (inputStartBytes.HasValue)
                encodeOptions.InputStartBytes = inputStartBytes.Value;

            if (inputLengthBytes.HasValue)
                encodeOptions.InputLengthBytes = inputLengthBytes.Value;

            encodeOptions.Metadata = [MetadataWriter.BuildDvdMetadata()];

            ChdEncoder.EncodeRaw(
                inputPath,
                outputPath,
                hunkBytes,
                unitBytes,
                codecTags,
                encodeOptions
            );
            logger?.LogSummary();
            log.Information("  Created {Size:N0} bytes", new FileInfo(outputPath).Length);
            VerifyResultChd(outputPath, parentPath);
        }
        catch (Exception ex)
            when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            log.Warning("createdvd failed: {Message}", ex.Message);
        }
    }

    /// <summary>
    ///     Extracts raw data from a CHD file to an output file, with optional partial extraction
    ///     via byte (<c>--inputstartbyte</c>/<c>--inputbytes</c>) or hunk (<c>--inputstarthunk</c>/<c>--inputhunks</c>)
    ///     ranges.
    /// </summary>
    private static void ExtractRawTest(string inputPath, string outputPath, string[] options)
    {
        var log = Log.Logger;
        if (!File.Exists(inputPath))
        {
            log.Warning("extractraw: input file not found: {Path}", inputPath);
            return;
        }

        string? parentPath = null;
        var force = false;
        long? startByte = null;
        long? lengthBytes = null;
        long? startHunk = null;
        long? lengthHunks = null;
        for (var i = 0; i < options.Length; i++)
            switch (options[i])
            {
                case "--inputparent" or "-ip" when i + 1 < options.Length:
                    parentPath = options[++i].Replace("\"", "");
                    break;
                case "--inputstartbyte" or "-isb" when i + 1 < options.Length:
                    if (!long.TryParse(options[++i], out var sb) || sb < 0)
                    {
                        log.Warning("Invalid input start byte: {Value}", options[i]);
                        return;
                    }

                    startByte = sb;
                    break;
                case "--inputbytes" or "-ib" when i + 1 < options.Length:
                    if (!long.TryParse(options[++i], out var ib) || ib <= 0)
                    {
                        log.Warning("Invalid input bytes: {Value}", options[i]);
                        return;
                    }

                    lengthBytes = ib;
                    break;
                case "--inputstarthunk" or "-ish" when i + 1 < options.Length:
                    if (!long.TryParse(options[++i], out var sh) || sh < 0)
                    {
                        log.Warning("Invalid input start hunk: {Value}", options[i]);
                        return;
                    }

                    startHunk = sh;
                    break;
                case "--inputhunks" or "-ih" when i + 1 < options.Length:
                    if (!long.TryParse(options[++i], out var ih) || ih <= 0)
                    {
                        log.Warning("Invalid input hunks: {Value}", options[i]);
                        return;
                    }

                    lengthHunks = ih;
                    break;
                case "--force" or "-f":
                    force = true;
                    break;
                default:
                    log.Warning("Unknown option: {Option}", options[i]);
                    return;
            }

        if (File.Exists(outputPath) && !force)
        {
            log.Warning(
                "Output file already exists: {Path} (use --force to overwrite)",
                outputPath
            );
            return;
        }

        try
        {
            var err =
                parentPath != null
                    ? ChdFile.Open(inputPath, parentPath, out var chd)
                    : ChdFile.Open(inputPath, out chd);
            if (err != ChdError.Chderrnone || chd == null)
            {
                log.Warning("Open failed: {Error}", err);
                return;
            }

            using (chd)
            {
                // Convert hunk-based ranges to byte ranges; byte options take precedence.
                var readStart = startHunk.HasValue ? (ulong)startHunk.Value * chd.HunkBytes : 0;
                if (startByte.HasValue)
                    readStart = (ulong)startByte.Value;

                var readLength = lengthHunks.HasValue
                    ? (ulong)lengthHunks.Value * chd.HunkBytes
                    : chd.TotalBytes - readStart;
                if (lengthBytes.HasValue)
                    readLength = (ulong)lengthBytes.Value;

                if (readStart >= chd.TotalBytes)
                {
                    log.Warning(
                        "Start offset {Start} exceeds image size {Size}",
                        readStart,
                        chd.TotalBytes
                    );
                    return;
                }

                readLength = Math.Min(readLength, chd.TotalBytes - readStart);

                log.Information(
                    "Extracting: {Input} -> {Output}  ({Bytes:N0} bytes from offset {Start:N0})",
                    Path.GetFileName(inputPath),
                    outputPath,
                    readLength,
                    readStart
                );

                var tempPath = outputPath + ".tmp";
                try
                {
                    using var fs = new FileStream(
                        tempPath,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.None,
                        1024 * 1024
                    );
                    var buf = new byte[chd.HunkBytes];
                    var remaining = readLength;
                    var offset = readStart;
                    while (remaining > 0)
                    {
                        var chunk = (int)Math.Min((ulong)buf.Length, remaining);
                        err = chd.Read(offset, buf, 0, chunk);
                        if (err != ChdError.Chderrnone)
                        {
                            log.Warning("  Read(offset={Offset}) => {Error}", offset, err);
                            return;
                        }

                        fs.Write(buf, 0, chunk);
                        offset += (ulong)chunk;
                        remaining -= (ulong)chunk;
                    }
                }
                catch
                {
                    try
                    {
                        File.Delete(tempPath);
                    }
                    catch
                    {
                        /* best-effort cleanup */
                    }

                    throw;
                }

                File.Move(tempPath, outputPath, true);
                log.Information("  Extracted {Size:N0} bytes", readLength);
            }
        }
        catch (Exception ex)
            when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            log.Warning("extractraw failed: {Message}", ex.Message);
        }
    }

    /// <summary>Extracts a CD CHD to BIN/CUE (or ISO for DVD-mode CHDs), with optional --outputbin and --splitbin support.</summary>
    private static void ExtractCdTest(string inputPath, string outputPath, string[] options)
    {
        var log = Log.Logger;
        if (!File.Exists(inputPath))
        {
            log.Warning("extractcd: input file not found: {Path}", inputPath);
            return;
        }

        string? parentPath = null;
        string? binPath = null;
        var splitBin = false;
        var force = false;
        var cooked = true;
        for (var i = 0; i < options.Length; i++)
            switch (options[i])
            {
                case "--inputparent" or "-ip" when i + 1 < options.Length:
                    parentPath = options[++i].Replace("\"", "");
                    break;
                case "--outputbin" or "-ob" when i + 1 < options.Length:
                    binPath = options[++i].Replace("\"", "");
                    break;
                case "--splitbin" or "-sb":
                    splitBin = true;
                    break;
                case "--cooked":
                    cooked = true;
                    break;
                case "--raw" or "--raw-frames":
                    cooked = false;
                    break;
                case "--force" or "-f":
                    force = true;
                    break;
                default:
                    log.Warning("Unknown option: {Option}", options[i]);
                    return;
            }

        if (File.Exists(outputPath) && !force)
        {
            log.Warning(
                "Output file already exists: {Path} (use --force to overwrite)",
                outputPath
            );
            return;
        }

        try
        {
            var err =
                parentPath != null
                    ? ChdFile.Open(inputPath, parentPath, out var chd)
                    : ChdFile.Open(inputPath, out chd);
            if (err != ChdError.Chderrnone || chd == null)
            {
                log.Warning("Open failed: {Error}", err);
                return;
            }

            using (chd)
            {
                var outputDir = Path.GetDirectoryName(outputPath) ?? ".";
                var baseName = Path.GetFileNameWithoutExtension(outputPath);

                // chdman.cpp:2675 is_splitbin = mode==GDI || --splitbin || (is_gdrom && mode==CUEBIN)
                var outputExt = Path.GetExtension(outputPath);
                var isGdiMode = outputExt.Equals(".gdi", StringComparison.OrdinalIgnoreCase);
                var isTocMode = !isGdiMode && !outputExt.Equals(".cue", StringComparison.OrdinalIgnoreCase);
                var effectiveSplitBin = isGdiMode || splitBin || (chd.IsGdRom && !isGdiMode);
                if (isTocMode)
                    log.Information("  Note: .toc output not fully supported; generating CUE-compatible output (chdman MODE_NORMAL)");

                // --outputbin %t handling (chdman.cpp:2748): require %t when splitbin
                if (effectiveSplitBin && binPath != null)
                {
                    var tRegex = new Regex(@"(?<!%)%(?:%)*0*\d*t");
                    if (!tRegex.IsMatch(binPath))
                    {
                        log.Warning("A track number variable (%t) must be specified in the output bin filename when --splitbin is enabled");
                        return;
                    }
                }

                if (effectiveSplitBin && chd is { IsCd: true, Tracks.Count: > 1 })
                {
                    // --splitbin: extract each track to a separate file
                    log.Information(
                        "Extracting CD (split): {Input} -> {Dir}",
                        Path.GetFileName(inputPath),
                        outputDir
                    );
                    Directory.CreateDirectory(outputDir);

                    var trackNames = new List<string>();
                    foreach (var track in chd.Tracks)
                    {
                        string trackFileName;
                        string trackFile;
                        if (binPath != null)
                        {
                            // expand %t template per chdman.cpp:2748 (%t, %02t etc.)
                            var expanded = Regex.Replace(
                                binPath,
                                @"%0*(\d*)t",
                                m =>
                                {
                                    var widthStr = m.Groups[1].Value;
                                    if (int.TryParse(widthStr, out var w) && w > 0)
                                        return track.TrackNumber.ToString($"D{w}");
                                    return track.TrackNumber.ToString();
                                }
                            );
                            // handle escaped %% -> %
                            expanded = expanded.Replace("%%", "%");
                            trackFile = Path.IsPathRooted(expanded)
                                ? expanded
                                : Path.Combine(outputDir, expanded);
                            // ensure extension: chdman appends .raw for GD-ROM audio, else output_bin_ext
                            if (Path.GetExtension(trackFile).Length == 0)
                                trackFile += isGdiMode && track.TrackType == ChdTrackType.Audio ? ".raw" : ".bin";
                            trackFileName = Path.GetFileName(trackFile);
                        }
                        else
                        {
                            trackFileName = $"track{track.TrackNumber:D2}.bin";
                            trackFile = Path.Combine(outputDir, trackFileName);
                        }

                        log.Information(
                            "  Track {Track}: {Frames} frames -> {File}",
                            track.TrackNumber,
                            track.Frames,
                            Path.GetFileName(trackFile)
                        );
                        var trackErr = chd.WriteTrackToFile(track, trackFile, cooked);
                        if (trackErr != ChdError.Chderrnone)
                        {
                            log.Warning(
                                "  Track {Track} extraction failed: {Error}",
                                track.TrackNumber,
                                trackErr
                            );
                            return;
                        }

                        trackNames.Add(trackFileName);
                    }

                    // Write CUE sheet referencing per-track files
                    var cueFile = Path.Combine(outputDir, $"{baseName}.cue");
                    var cueSb = new StringBuilder();
                    cueSb.AppendLine("REM Generated by CHDSharp");
                    cueSb.AppendLine($"REM Tracks: {chd.Tracks.Count}");
                    cueSb.AppendLine();
                    for (var ti = 0; ti < chd.Tracks.Count; ti++)
                    {
                        var track = chd.Tracks[ti];
                        var trackFileName = trackNames[ti];
                        cueSb.AppendLine($"FILE \"{trackFileName}\" BINARY");
                        var modeStr = track.TrackType switch
                        {
                            ChdTrackType.Mode1 or ChdTrackType.Mode1Raw =>
                                $"MODE1/{track.DataSize:D4}",
                            ChdTrackType.Mode2 => $"MODE2/{track.DataSize:D4}",
                            ChdTrackType.Mode2Form1 => $"MODE2/{track.DataSize:D4}",
                            ChdTrackType.Mode2Form2 => $"MODE2/{track.DataSize:D4}",
                            ChdTrackType.Mode2FormMix => $"MODE2/{track.DataSize:D4}",
                            ChdTrackType.Mode2Raw => $"MODE2/{track.DataSize:D4}",
                            ChdTrackType.Audio => "AUDIO",
                            _ => string.Create(
                                CultureInfo.InvariantCulture,
                                $"MODE1/{track.DataSize:D4}"
                            )
                        };
                        cueSb.AppendLine($"  TRACK {track.TrackNumber:D2} {modeStr}");
                        cueSb.AppendLine("    INDEX 01 00:00:00");
                    }

                    File.WriteAllText(cueFile, cueSb.ToString());
                    log.Information("  Created: {File}", cueFile);
                }
                else
                {
                    // Standard extraction
                    log.Information(
                        "Extracting CD: {Input} -> {Dir}  ({Mode})",
                        Path.GetFileName(inputPath),
                        outputDir,
                        cooked ? "cooked" : "raw frames"
                    );
                    var created = chd.ExtractToDirectory(outputDir, baseName, cooked: cooked);

                    // If --outputbin is specified, rename the BIN file and update the CUE
                    if (binPath != null)
                    {
                        var binFile = created.FirstOrDefault(f =>
                            f.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)
                        );
                        if (binFile != null)
                        {
                            var targetBin = Path.GetFullPath(binPath);
                            if (File.Exists(targetBin) && !force)
                            {
                                log.Warning(
                                    "Output BIN already exists: {Path} (use --force to overwrite)",
                                    targetBin
                                );
                                return;
                            }

                            File.Move(binFile, targetBin, true);
                            log.Information(
                                "  Renamed: {Old} -> {New}",
                                Path.GetFileName(binFile),
                                targetBin
                            );

                            // Update CUE sheet to reference the new BIN name
                            var cueFile = created.FirstOrDefault(f =>
                                f.EndsWith(".cue", StringComparison.OrdinalIgnoreCase)
                            );
                            if (cueFile != null)
                            {
                                var cueContent = File.ReadAllText(cueFile);
                                var oldBinName = Path.GetFileName(binFile);
                                var newBinName = Path.GetFileName(targetBin);
                                cueContent = cueContent.Replace(oldBinName, newBinName);
                                File.WriteAllText(cueFile, cueContent);
                                log.Information("  Updated CUE: {File}", cueFile);
                            }

                            // Update GDI file to reference the new BIN name
                            var gdiFile = created.FirstOrDefault(f =>
                                f.EndsWith(".gdi", StringComparison.OrdinalIgnoreCase)
                            );
                            if (gdiFile != null)
                            {
                                var gdiContent = File.ReadAllText(gdiFile);
                                var oldBinName = Path.GetFileName(binFile);
                                var newBinName = Path.GetFileName(targetBin);
                                gdiContent = gdiContent.Replace(oldBinName, newBinName);
                                File.WriteAllText(gdiFile, gdiContent);
                                log.Information("  Updated GDI: {File}", gdiFile);
                            }
                        }
                    }

                    foreach (var f in created)
                        log.Information("  Created: {File}", f);
                }
            }
        }
        catch (Exception ex)
            when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            log.Warning("extractcd failed: {Message}", ex.Message);
        }
    }

    /// <summary>Pauses before exit when the application was launched by double-clicking (no arguments).</summary>
    private static void WaitForExitIfDoubleClicked()
    {
        // When launched by double-click, Environment.GetCommandLineArgs() has exactly 1 entry
        // and stdin is not redirected. In that case, pause so the user can read the output.
        if (Interlocked.Exchange(ref _exitPrompted, 1) == 1)
            return;

        if (Environment.GetCommandLineArgs().Length <= 1 && !Console.IsInputRedirected)
        {
            Console.WriteLine();
            Console.Write("Press any key to exit...");
            try
            {
                Console.ReadKey(true);
            }
            catch
            {
                // InvalidOperationException if no console available (e.g. redirected)
            }
        }
    }

    /// <summary>
    ///     Logs one line per hunk (codec, sizes, compression ratio) while encoding, then a
    ///     summary of the stored bytes and per-codec hunk counts.
    /// </summary>
    private sealed class VerboseHunkLogger
    {
        private readonly Dictionary<string, int> _counts = new(StringComparer.Ordinal);
        private long _totalRaw;
        private long _totalStored;

        public VerboseHunkLogger()
        {
            Options.HunkCompleted = p =>
            {
                _totalRaw += p.RawBytes;
                _totalStored += p.StoredBytes;
                _counts[p.CodecName] = _counts.GetValueOrDefault(p.CodecName) + 1;
                Log.Logger.Information(
                    "  hunk {Hunk,6}/{Count,6}  {Codec,-5} {Raw,10} -> {Stored,10} B  ({Ratio,5:P1})",
                    p.HunkIndex,
                    p.HunkCount,
                    p.CodecName,
                    p.RawBytes,
                    p.StoredBytes,
                    p.Ratio
                );
            };
        }

        /// <summary>The <see cref="ChdEncodeOptions" /> to pass to the encoder.</summary>
        public ChdEncodeOptions Options { get; } = new();

        public void LogSummary()
        {
            var overall = _totalRaw == 0 ? 1.0 : _totalStored / (double)_totalRaw;
            Log.Logger.Information(
                "  Ratio: {Stored:N0} / {Raw:N0} bytes = {Overall:P1}  [{Counts}]",
                _totalStored,
                _totalRaw,
                overall,
                string.Join(
                    ", ",
                    _counts
                        .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                        .Select(kv => $"{kv.Key}: {kv.Value}")
                )
            );
        }
    }
}