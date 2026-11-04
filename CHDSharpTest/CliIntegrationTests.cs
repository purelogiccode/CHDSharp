using System.Diagnostics;

namespace CHDSharp.Tests;

[Collection("CLI")]
public sealed class CliIntegrationTests
{
    private static readonly string TestDataDir = Path.Combine(AppContext.BaseDirectory, "TestData");

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

    private static (int exitCode, string output) RunCli(params string[] args)
    {
        var escapedArgs = string.Join(" ", args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
        var argString = $"\"{CliPath}\" {escapedArgs}";

        var psi = new ProcessStartInfo("dotnet", argString)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Tests drive error paths on purpose — spawned CLI runs must not file bug
        // reports or usage stats against the production APIs.
        psi.Environment["CHDSHARP_NO_BUG_REPORTS"] = "1";
        psi.Environment["CHDSHARP_NO_TELEMETRY"] = "1";

        using var proc = Process.Start(psi);
        var stdoutTask = proc!.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();
        proc.WaitForExit(30000);
        var output = stdoutTask.Result + "\n" + stderrTask.Result;
        return (proc.ExitCode, output);
    }

    [Fact]
    public void Toc_command_produces_toc_for_cd()
    {
        var path = Path.Combine(TestDataDir, "v5_cd_default.chd");
        var (exitCode, output) = RunCli("--toc", path);

        Assert.Equal(0, exitCode);
        // Serilog debug output may prepend metadata dump; check the actual TOC content
        Assert.Contains("CD-ROM", output, StringComparison.Ordinal);
        Assert.Contains("MODE1/2048", output, StringComparison.Ordinal);
        Assert.Contains("AUDIO", output, StringComparison.Ordinal);
        Assert.Contains("Track", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Toc_command_for_raw_returns_no_tracks_message()
    {
        var path = Path.Combine(TestDataDir, "v5_zlib.chd");
        var (exitCode, output) = RunCli("--toc", path);

        Assert.Equal(0, exitCode);
        Assert.Contains("No CD/GD-ROM track metadata", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Cue_command_produces_cue_for_cd()
    {
        var path = Path.Combine(TestDataDir, "v5_cd_default.chd");
        var (exitCode, output) = RunCli("--cue", path);

        Assert.Equal(0, exitCode);
        Assert.Contains("TRACK 01 MODE1/2048", output, StringComparison.Ordinal);
        Assert.Contains("TRACK 02 AUDIO", output, StringComparison.Ordinal);
        Assert.Contains("INDEX 01 00:00:00", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Cue_command_accepts_custom_bin_name()
    {
        var path = Path.Combine(TestDataDir, "v5_cd_default.chd");
        var (exitCode, output) = RunCli("--cue", path, "custom.bin");

        Assert.Equal(0, exitCode);
        Assert.Contains("FILE \"custom.bin\"", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Cue_command_for_raw_fails_gracefully()
    {
        var path = Path.Combine(TestDataDir, "v5_zlib.chd");
        var (exitCode, output) = RunCli("--cue", path);

        Assert.Equal(0, exitCode);
        Assert.Contains("CUE generation failed", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Classify_command_returns_cd_for_cd()
    {
        var path = Path.Combine(TestDataDir, "v5_cd_default.chd");
        var (exitCode, output) = RunCli("--classify", path);

        Assert.Equal(0, exitCode);
        Assert.Contains("cd", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Classify_command_returns_unknown_for_raw()
    {
        var path = Path.Combine(TestDataDir, "v5_zlib.chd");
        var (exitCode, output) = RunCli("--classify", path);

        Assert.Equal(0, exitCode);
        Assert.Contains("unknown/raw", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Classify_command_reports_file_not_found()
    {
        var path = Path.Combine(TestDataDir, "nonexistent.chd");
        var (exitCode, output) = RunCli("--classify", path);

        Assert.Equal(0, exitCode);
        Assert.Contains("Classify failed", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Usage_shows_chdman_style_commands()
    {
        var (exitCode, output) = RunCli();

        Assert.Equal(0, exitCode);
        Assert.Contains("info:", output, StringComparison.Ordinal);
        Assert.Contains("verify:", output, StringComparison.Ordinal);
        Assert.Contains("createcd:", output, StringComparison.Ordinal);
        Assert.Contains("extractcd:", output, StringComparison.Ordinal);
        Assert.Contains("help <command>", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Createdvd_rejects_input_size_that_is_not_sector_aligned()
    {
        // chdman.cpp:2263 - createdvd refuses input whose length is not a multiple of 2048.
        var dir = Path.Combine(Path.GetTempPath(), "chdsharp-createdvd-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var input = Path.Combine(dir, "odd.iso");
            File.WriteAllBytes(input, new byte[1000]);
            var output = Path.Combine(dir, "out.chd");

            var (exitCode, log) = RunCli("createdvd", "--input", input, "--output", output);

            Assert.Equal(1, exitCode);
            Assert.Contains(
                "Data size 1,000 is not divisible by sector size 2048",
                log,
                StringComparison.Ordinal
            );
            Assert.False(File.Exists(output));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Createdvd_reports_usage_error_when_arguments_are_missing()
    {
        var (exitCode, log) = RunCli("createdvd");

        Assert.Equal(1, exitCode);
        Assert.Contains(
            "createdvd requires --input <file> --output <file>",
            log,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void Createcd_rejects_iso_size_that_is_not_sector_aligned()
    {
        // cdrom.cpp:2078 - parse_iso only accepts sizes that are a multiple of 2048,
        // 2336 or 2352. This is an expected input error, not a defect.
        var dir = Path.Combine(Path.GetTempPath(), "chdsharp-createcd-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var input = Path.Combine(dir, "odd.iso");
            File.WriteAllBytes(input, new byte[1000]);
            var output = Path.Combine(dir, "out.chd");

            var (exitCode, log) = RunCli("createcd", "--input", input, "--output", output);

            Assert.Equal(1, exitCode);
            Assert.Contains("Unrecognized ISO sector size", log, StringComparison.Ordinal);
            Assert.False(File.Exists(output));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
