using System.Diagnostics;

namespace CHDSharp.Tests;

/// <summary>
///     Tests that verify the wiring between CLI commands/options and the underlying CHDSharpLib APIs.
///     Each test exercises a specific CLI command and checks that the library produces the expected output.
/// </summary>
[Collection("CLI")]
public sealed class CliWiringTests : IDisposable
{
    private static readonly string TestDataDir = Path.Combine(AppContext.BaseDirectory, "TestData");
    private readonly string _tempDir;

    public CliWiringTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(),
            "chd_wiring_" + Guid.NewGuid().ToString("N")[..8]
        );
        Directory.CreateDirectory(_tempDir);
    }

    private static string CliPath
    {
        get
        {
            var baseDir = AppContext.BaseDirectory;
            var testBinIdx = baseDir.IndexOf(
                Path.Combine("CHDSharpTest", "bin"),
                StringComparison.OrdinalIgnoreCase
            );
            if (testBinIdx >= 0)
            {
                var slnRoot = baseDir[..testBinIdx];
                var config =
                    Path.GetFileName(
                        Path.GetDirectoryName(baseDir.TrimEnd(Path.DirectorySeparatorChar))
                    ) ?? "Debug";
                var tfm = Path.GetFileName(baseDir.TrimEnd(Path.DirectorySeparatorChar));
                return Path.Combine(slnRoot, "CHDSharpCli", "bin", config, tfm, "CHDSharp.dll");
            }

            return Path.Combine(AppContext.BaseDirectory, "CHDSharp.dll");
        }
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDir, true);
        }
        catch
        {
            // ignored
        }
    }

    private static (int exitCode, string output) RunCli(params string[] args)
    {
        var escapedArgs = string.Join(" ", args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
        var argString = $"\"{CliPath}\" {escapedArgs}";

        var psi = new ProcessStartInfo("dotnet", argString)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var proc = Process.Start(psi);
        var stdoutTask = proc!.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();
        proc.WaitForExit(60000);
        var output = stdoutTask.Result + "\n" + stderrTask.Result;
        return (proc.ExitCode, output);
    }

    private static string GetTestChd(string name)
    {
        var path = Path.Combine(TestDataDir, name);
        if (!File.Exists(path))
            Assert.Skip("Test data missing: " + path);
        return path;
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Help / usage
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Help_shows_chdman_style_commands()
    {
        var (_, output) = RunCli("help");
        Assert.Contains("info:", output, StringComparison.Ordinal);
        Assert.Contains("verify:", output, StringComparison.Ordinal);
        Assert.Contains("createraw:", output, StringComparison.Ordinal);
        Assert.Contains("createhd:", output, StringComparison.Ordinal);
        Assert.Contains("createcd:", output, StringComparison.Ordinal);
        Assert.Contains("createdvd:", output, StringComparison.Ordinal);
        Assert.Contains("createld:", output, StringComparison.Ordinal);
        Assert.Contains("extractraw:", output, StringComparison.Ordinal);
        Assert.Contains("extracthd:", output, StringComparison.Ordinal);
        Assert.Contains("extractcd:", output, StringComparison.Ordinal);
        Assert.Contains("extractdvd:", output, StringComparison.Ordinal);
        Assert.Contains("extractld:", output, StringComparison.Ordinal);
        Assert.Contains("copy:", output, StringComparison.Ordinal);
        Assert.Contains("addmeta:", output, StringComparison.Ordinal);
        Assert.Contains("delmeta:", output, StringComparison.Ordinal);
        Assert.Contains("dumpmeta:", output, StringComparison.Ordinal);
        Assert.Contains("listtemplates:", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Help_command_shows_detailed_usage()
    {
        var (_, output) = RunCli("help", "createcd");
        Assert.Contains("createcd", output, StringComparison.Ordinal);
        Assert.Contains("--input", output, StringComparison.Ordinal);
        Assert.Contains("--output", output, StringComparison.Ordinal);
        Assert.Contains("--compression", output, StringComparison.Ordinal);
    }

    [Fact]
    public void DashDash_help_also_works()
    {
        var (_, output) = RunCli("--help");
        Assert.Contains("info:", output, StringComparison.Ordinal);
        Assert.Contains("verify:", output, StringComparison.Ordinal);
    }

    // ──────────────────────────────────────────────────────────────────────
    //  info command
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Info_chdman_style_displays_header()
    {
        var path = GetTestChd("v5_zlib.chd");
        var (exitCode, output) = RunCli("info", "--input", path);
        Assert.Equal(0, exitCode);
        Assert.Contains("Input file:", output, StringComparison.Ordinal);
        Assert.Contains("File Version:", output, StringComparison.Ordinal);
        Assert.Contains("Logical size:", output, StringComparison.Ordinal);
        Assert.Contains("Hunk Size:", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Info_legacy_style_displays_header()
    {
        var path = GetTestChd("v5_zlib.chd");
        var (exitCode, output) = RunCli("--info", path);
        Assert.Equal(0, exitCode);
        Assert.Contains("Input file:", output, StringComparison.Ordinal);
        Assert.Contains("File Version:", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Info_short_flag_i_works()
    {
        var path = GetTestChd("v5_zlib.chd");
        var (exitCode, output) = RunCli("info", "-i", path);
        Assert.Equal(0, exitCode);
        Assert.Contains("Input file:", output, StringComparison.Ordinal);
        Assert.Contains("File Version:", output, StringComparison.Ordinal);
    }

    // ──────────────────────────────────────────────────────────────────────
    //  verify command
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Verify_chdman_style_valid_chd()
    {
        var path = GetTestChd("v5_zlib.chd");
        var (exitCode, output) = RunCli("verify", "--input", path);
        Assert.Equal(0, exitCode);
        Assert.Contains("Verified OK", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Verify_legacy_style_valid_chd()
    {
        var path = GetTestChd("v5_zlib.chd");
        var (exitCode, output) = RunCli("--verify", path);
        Assert.Equal(0, exitCode);
        Assert.Contains("Verified OK", output, StringComparison.Ordinal);
    }

    // ──────────────────────────────────────────────────────────────────────
    //  listtemplates command
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void ListTemplates_shows_geometry_table()
    {
        var (exitCode, output) = RunCli("listtemplates");
        Assert.Equal(0, exitCode);
        Assert.Contains("Conner", output, StringComparison.Ordinal);
        Assert.Contains("CFA170A", output, StringComparison.Ordinal);
        Assert.Contains("Cylinders", output, StringComparison.Ordinal);
        Assert.Contains("Heads", output, StringComparison.Ordinal);
    }

    // ──────────────────────────────────────────────────────────────────────
    //  classify command
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Classify_chdman_style_cd()
    {
        var path = GetTestChd("v5_cd_default.chd");
        var (exitCode, output) = RunCli("classify", "--input", path);
        Assert.Equal(0, exitCode);
        Assert.Contains("cd", output, StringComparison.OrdinalIgnoreCase);
    }

    // ──────────────────────────────────────────────────────────────────────
    //  toc command
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Toc_chdman_style_cd()
    {
        var path = GetTestChd("v5_cd_default.chd");
        var (exitCode, output) = RunCli("toc", "--input", path);
        Assert.Equal(0, exitCode);
        Assert.Contains("CD-ROM", output, StringComparison.Ordinal);
        Assert.Contains("Track", output, StringComparison.Ordinal);
    }

    // ──────────────────────────────────────────────────────────────────────
    //  cue command
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Cue_chdman_style_cd()
    {
        var path = GetTestChd("v5_cd_default.chd");
        var (exitCode, output) = RunCli("cue", "--input", path);
        Assert.Equal(0, exitCode);
        Assert.Contains("TRACK 01", output, StringComparison.Ordinal);
        Assert.Contains("INDEX 01", output, StringComparison.Ordinal);
    }

    // ──────────────────────────────────────────────────────────────────────
    //  createcd + extractcd round-trip
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Createcd_then_verify_round_trip()
    {
        // Create a minimal CUE+BIN for testing
        var binPath = Path.Combine(_tempDir, "test.bin");
        var cuePath = Path.Combine(_tempDir, "test.cue");
        var chdPath = Path.Combine(_tempDir, "test.chd");

        // Create a minimal raw binary (2352 bytes = 1 CD frame)
        var frameData = new byte[2352];
        new Random(42).NextBytes(frameData);
        File.WriteAllBytes(binPath, frameData);

        const string cueContent = """
            FILE "test.bin" BINARY
              TRACK 01 MODE1/2352
                INDEX 01 00:00:00
            """;
        File.WriteAllText(cuePath, cueContent);

        // Create CD CHD
        var (exitCode, output) = RunCli("createcd", "--output", chdPath, "--input", cuePath);
        Assert.Equal(0, exitCode);
        Assert.Contains("Created", output, StringComparison.Ordinal);

        // Verify the created CHD
        var (verifyExit, verifyOut) = RunCli("verify", "--input", chdPath);
        Assert.Equal(0, verifyExit);
        Assert.Contains("Verified OK", verifyOut, StringComparison.Ordinal);
    }

    [Fact]
    public void Createcd_legacy_style_positional_args()
    {
        var binPath = Path.Combine(_tempDir, "test.bin");
        var cuePath = Path.Combine(_tempDir, "test.cue");
        var chdPath = Path.Combine(_tempDir, "test.chd");

        var frameData = new byte[2352];
        new Random(42).NextBytes(frameData);
        File.WriteAllBytes(binPath, frameData);

        File.WriteAllText(
            cuePath,
            """
            FILE "test.bin" BINARY
              TRACK 01 MODE1/2352
                INDEX 01 00:00:00
            """
        );

        var (exitCode, output) = RunCli("--createcd", cuePath, chdPath);
        Assert.Equal(0, exitCode);
        Assert.Contains("Created", output, StringComparison.Ordinal);
    }

    // ──────────────────────────────────────────────────────────────────────
    //  extractcd with --outputbin
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extractcd_with_outputbin_renames_bin()
    {
        var path = GetTestChd("v5_cd_default.chd");
        var outCue = Path.Combine(_tempDir, "out.cue");
        var outBin = Path.Combine(_tempDir, "custom_name.bin");

        var (exitCode, _) = RunCli(
            "extractcd",
            "--output",
            outCue,
            "--input",
            path,
            "--outputbin",
            outBin
        );
        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(outBin), "BIN file should exist at specified path");
        Assert.True(File.Exists(outCue), "CUE file should exist");

        var cueContent = File.ReadAllText(outCue);
        Assert.Contains("custom_name.bin", cueContent, StringComparison.Ordinal);
    }

    // ──────────────────────────────────────────────────────────────────────
    //  extractcd with --splitbin
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extractcd_with_splitbin_creates_per_track_files()
    {
        var path = GetTestChd("v5_cd_default.chd");
        var outCue = Path.Combine(_tempDir, "out.cue");

        var (exitCode, _) = RunCli("extractcd", "--output", outCue, "--input", path, "--splitbin");
        Assert.Equal(0, exitCode);

        // Should have track01.bin, track02.bin, etc.
        Assert.True(File.Exists(Path.Combine(_tempDir, "track01.bin")), "track01.bin should exist");
        Assert.True(File.Exists(Path.Combine(_tempDir, "track02.bin")), "track02.bin should exist");
        Assert.True(File.Exists(outCue), "CUE should exist");

        var cueContent = File.ReadAllText(outCue);
        Assert.Contains("track01.bin", cueContent, StringComparison.Ordinal);
        Assert.Contains("track02.bin", cueContent, StringComparison.Ordinal);
    }

    // ──────────────────────────────────────────────────────────────────────
    //  extractraw
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extractraw_chdman_style()
    {
        var path = GetTestChd("v5_zlib.chd");
        var outPath = Path.Combine(_tempDir, "extracted.bin");

        var (exitCode, output) = RunCli("extractraw", "--output", outPath, "--input", path);
        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(outPath), "Extracted file should exist");
        Assert.Contains("Extracted", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Extractraw_with_force_overwrites_existing()
    {
        var path = GetTestChd("v5_zlib.chd");
        var outPath = Path.Combine(_tempDir, "extracted.bin");
        File.WriteAllBytes(outPath, new byte[] { 1, 2, 3 });

        var (exitCode, _) = RunCli("extractraw", "--output", outPath, "--input", path, "--force");
        Assert.Equal(0, exitCode);
        Assert.True(
            new FileInfo(outPath).Length > 3,
            "File should be overwritten with CHD content"
        );
    }

    [Fact]
    public void Extractraw_without_force_refuses_overwrite()
    {
        var path = GetTestChd("v5_zlib.chd");
        var outPath = Path.Combine(_tempDir, "extracted.bin");
        File.WriteAllBytes(outPath, new byte[] { 1, 2, 3 });

        var (_, output) = RunCli("extractraw", "--output", outPath, "--input", path);
        Assert.Contains("already exists", output, StringComparison.OrdinalIgnoreCase);
    }

    // ──────────────────────────────────────────────────────────────────────
    //  extractraw with partial extraction
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extractraw_with_inputstartbyte_and_inputbytes()
    {
        var path = GetTestChd("v5_zlib.chd");
        var outFull = Path.Combine(_tempDir, "full.bin");
        var outPartial = Path.Combine(_tempDir, "partial.bin");

        // Extract full
        var (e1, _) = RunCli("extractraw", "--output", outFull, "--input", path);
        Assert.Equal(0, e1);

        // Extract partial (bytes 1000..2000)
        var (e2, _) = RunCli(
            "extractraw",
            "--output",
            outPartial,
            "--input",
            path,
            "--inputstartbyte",
            "1000",
            "--inputbytes",
            "1000"
        );
        Assert.Equal(0, e2);
        Assert.True(File.Exists(outPartial), "Partial file should exist");

        var fullData = File.ReadAllBytes(outFull);
        var partialData = File.ReadAllBytes(outPartial);
        Assert.Equal(1000, partialData.Length);

        // Verify the partial matches the corresponding region of the full
        for (var i = 0; i < 1000; i++)
            Assert.Equal(fullData[1000 + i], partialData[i]);
    }

    // ──────────────────────────────────────────────────────────────────────
    //  createdvd
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Createdvd_creates_dvd_chd()
    {
        var isoPath = Path.Combine(_tempDir, "test.iso");
        var chdPath = Path.Combine(_tempDir, "test.chd");

        // Create a minimal ISO (2048 bytes = 1 DVD sector)
        var data = new byte[2048];
        new Random(42).NextBytes(data);
        File.WriteAllBytes(isoPath, data);

        var (exitCode, output) = RunCli("createdvd", "--output", chdPath, "--input", isoPath);
        Assert.Equal(0, exitCode);
        Assert.Contains("Created", output, StringComparison.Ordinal);

        // Verify it's classified as DVD
        var (clsExit, clsOut) = RunCli("classify", "--input", chdPath);
        Assert.Equal(0, clsExit);
        Assert.Contains("dvd", clsOut, StringComparison.OrdinalIgnoreCase);
    }

    // ──────────────────────────────────────────────────────────────────────
    //  createraw with --inputstartbyte / --inputbytes
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Createraw_with_inputstartbyte_and_inputbytes()
    {
        var srcPath = Path.Combine(_tempDir, "source.bin");
        var chdPath = Path.Combine(_tempDir, "out.chd");

        // Create a 8192-byte source
        var data = new byte[8192];
        new Random(42).NextBytes(data);
        File.WriteAllBytes(srcPath, data);

        // Encode only bytes 1024..3072 (2048 bytes)
        var (exitCode, output) = RunCli(
            "createraw",
            "--output",
            chdPath,
            "--input",
            srcPath,
            "--inputstartbyte",
            "1024",
            "--inputbytes",
            "2048"
        );
        Assert.Equal(0, exitCode);
        Assert.Contains("Created", output, StringComparison.Ordinal);

        // Verify the CHD logical size is 2048
        var (infoExit, infoOut) = RunCli("info", "--input", chdPath);
        Assert.Equal(0, infoExit);
        Assert.Contains("2,048 bytes", infoOut, StringComparison.Ordinal);
    }

    // ──────────────────────────────────────────────────────────────────────
    //  createhd (blank)
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Createhd_blank_with_size()
    {
        var chdPath = Path.Combine(_tempDir, "blank.chd");

        var (exitCode, output) = RunCli("createhd", "--output", chdPath, "--size", "1048576");
        Assert.Equal(0, exitCode);
        Assert.Contains("Created", output, StringComparison.Ordinal);

        // Verify
        var (vExit, vOut) = RunCli("verify", "--input", chdPath);
        Assert.Equal(0, vExit);
        Assert.Contains("Verified OK", vOut, StringComparison.Ordinal);
    }

    [Fact]
    public void Createhd_blank_with_chs()
    {
        var chdPath = Path.Combine(_tempDir, "blank_chs.chd");

        var (exitCode, output) = RunCli("createhd", "--output", chdPath, "--chs", "100,4,17");
        Assert.Equal(0, exitCode);
        Assert.Contains("Created", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Createhd_blank_with_template()
    {
        var chdPath = Path.Combine(_tempDir, "blank_tpl.chd");

        var (exitCode, output) = RunCli("createhd", "--output", chdPath, "--template", "0");
        Assert.Equal(0, exitCode);
        Assert.Contains("Created", output, StringComparison.Ordinal);
    }

    // ──────────────────────────────────────────────────────────────────────
    //  copy (re-compress)
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Copy_recompresses_with_new_codec()
    {
        var srcPath = GetTestChd("v5_zlib.chd");
        var dstPath = Path.Combine(_tempDir, "copied.chd");

        var (exitCode, output) = RunCli(
            "copy",
            "--output",
            dstPath,
            "--input",
            srcPath,
            "--compression",
            "zstd"
        );
        Assert.Equal(0, exitCode);
        Assert.Contains("Created", output, StringComparison.Ordinal);

        // Verify the copy
        var (vExit, vOut) = RunCli("verify", "--input", dstPath);
        Assert.Equal(0, vExit);
        Assert.Contains("Verified OK", vOut, StringComparison.Ordinal);
    }

    // ──────────────────────────────────────────────────────────────────────
    //  addmeta / dumpmeta / delmeta
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Addmeta_then_dumpmeta_then_delmeta_round_trip()
    {
        var srcPath = GetTestChd("v5_zlib.chd");
        var workPath = Path.Combine(_tempDir, "work.chd");
        File.Copy(srcPath, workPath);

        // Add metadata
        var (e1, o1) = RunCli(
            "addmeta",
            "--input",
            workPath,
            "--tag",
            "TEST",
            "--valuetext",
            "hello world"
        );
        Assert.Equal(0, e1);
        Assert.Contains("Added/replaced", o1, StringComparison.Ordinal);

        // Dump metadata
        var (e2, o2) = RunCli("dumpmeta", "--input", workPath, "--tag", "TEST");
        Assert.Equal(0, e2);
        Assert.Contains("hello world", o2, StringComparison.Ordinal);

        // Delete metadata
        var (e3, o3) = RunCli("delmeta", "--input", workPath, "--tag", "TEST");
        Assert.Equal(0, e3);
        Assert.Contains("Deleted", o3, StringComparison.Ordinal);
    }

    // ──────────────────────────────────────────────────────────────────────
    //  hash command
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Hash_computes_sha1()
    {
        var path = GetTestChd("v5_zlib.chd");
        var (exitCode, output) = RunCli("--hash", path, "--hashes", "sha1");
        Assert.Equal(0, exitCode);
        Assert.Contains("SHA-1:", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Hash_json_output()
    {
        var path = GetTestChd("v5_zlib.chd");
        var (exitCode, output) = RunCli("--hash", path, "--hashes", "sha1", "--result", "json");
        Assert.Equal(0, exitCode);
        Assert.Contains("\"sha1\"", output, StringComparison.Ordinal);
    }

    // ──────────────────────────────────────────────────────────────────────
    //  detect command
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Detect_cd_chd()
    {
        var path = GetTestChd("v5_cd_default.chd");
        var (exitCode, output) = RunCli("--detect", path);
        Assert.Equal(0, exitCode);
        // Should detect some platform
        Assert.Contains(Path.GetFileName(path), output, StringComparison.Ordinal);
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Option parsing: chdman-style names
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Createhd_accepts_chdman_style_option_names()
    {
        var chdPath = Path.Combine(_tempDir, "opts.chd");

        // Use chdman-style: --hunksize, --unitsize, --numprocessors, --compression
        var (exitCode, output) = RunCli(
            "createhd",
            "--output",
            chdPath,
            "--size",
            "65536",
            "--hunksize",
            "4096",
            "--unitsize",
            "512",
            "--compression",
            "none",
            "--numprocessors",
            "1"
        );
        Assert.Equal(0, exitCode);
        Assert.Contains("Created", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Createhd_accepts_short_option_names()
    {
        var chdPath = Path.Combine(_tempDir, "opts2.chd");

        // Use short names: -o, -s, -hs, -us, -c, -np
        var (exitCode, output) = RunCli(
            "createhd",
            "-o",
            chdPath,
            "-s",
            "65536",
            "-hs",
            "4096",
            "-us",
            "512",
            "-c",
            "none",
            "-np",
            "1"
        );
        Assert.Equal(0, exitCode);
        Assert.Contains("Created", output, StringComparison.Ordinal);
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Error handling
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Info_missing_file_reports_error()
    {
        var (exitCode, output) = RunCli("info", "--input", @"Z:\no\such\file.chd");
        Assert.Equal(0, exitCode);
        Assert.Contains("Info failed", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Verify_missing_file_reports_error()
    {
        var (exitCode, output) = RunCli("verify", "--input", @"Z:\no\such\file.chd");
        // chdman exits 1 when the input CHD cannot be opened
        Assert.Equal(1, exitCode);
        Assert.Contains("FAILED", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Unknown_option_reports_error()
    {
        var chdPath = Path.Combine(_tempDir, "dummy.chd");
        File.WriteAllBytes(chdPath, new byte[100]);

        var (_, output) = RunCli("createhd", "--output", chdPath, "--size", "4096", "--bogus");
        Assert.Contains("unknown option", output, StringComparison.OrdinalIgnoreCase);
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Legacy commands still work
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Legacy_random_requires_path()
    {
        var (_, output) = RunCli("--random");
        Assert.Contains("requires", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Legacy_list_requires_path()
    {
        var (_, output) = RunCli("--list");
        Assert.Contains("requires", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Legacy_parent_requires_two_paths()
    {
        var (_, output) = RunCli("--parent", "a.chd");
        Assert.Contains("requires", output, StringComparison.OrdinalIgnoreCase);
    }

    // ──────────────────────────────────────────────────────────────────────
    //  extractld (basic — requires AV test data which may not exist)
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extractld_missing_file_reports_error()
    {
        var (exitCode, output) = RunCli(
            "extractld",
            "--output",
            Path.Combine(_tempDir, "out.avi"),
            "--input",
            @"Z:\no\such\file.chd"
        );
        Assert.Equal(0, exitCode);
        Assert.Contains("not found", output, StringComparison.OrdinalIgnoreCase);
    }

    // ──────────────────────────────────────────────────────────────────────
    //  createld (basic — requires AVI test data which may not exist)
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Createld_missing_file_reports_error()
    {
        var (exitCode, output) = RunCli(
            "createld",
            "--output",
            Path.Combine(_tempDir, "out.chd"),
            "--input",
            @"Z:\no\such\file.avi"
        );
        Assert.Equal(0, exitCode);
        Assert.Contains("not found", output, StringComparison.OrdinalIgnoreCase);
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Directory verification (legacy positional)
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Directory_positional_arg_verifies_chds()
    {
        var (exitCode, output) = RunCli(TestDataDir);
        Assert.Equal(0, exitCode);
        Assert.Contains("Done:", output, StringComparison.Ordinal);
    }
}
