using System.Diagnostics;

namespace CHDSharpEncoderTest;

internal static class ChdmanHelper
{
    internal static readonly string? ChdmanPath = ResolveChdmanPath();

    internal static (int ExitCode, string StdOut, string StdErr) RunChdman(params string[] args)
    {
        var chdmanPath =
            ChdmanPath ?? throw new InvalidOperationException("chdman.exe not available");

        var psi = new ProcessStartInfo
        {
            FileName = chdmanPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);

        using var p = Process.Start(psi)!;
        var tOut = p.StandardOutput.ReadToEndAsync();
        var tErr = p.StandardError.ReadToEndAsync();
        p.WaitForExit();

        return (p.ExitCode, tOut.Result, tErr.Result);
    }

    private static string? ResolveChdmanPath()
    {
        var exeName = OperatingSystem.IsWindows() ? "chdman.exe" : "chdman";

        // check alongside the test assembly
        var baseDir = AppContext.BaseDirectory;
        var candidate = Path.Combine(baseDir, exeName);
        if (File.Exists(candidate))
            return candidate;

        // check Tester project dir (for IDE Test Explorer runs)
        candidate = Path.GetFullPath(
            Path.Combine(baseDir, "..", "..", "..", "..", "CHDSharpTester", exeName)
        );
        if (File.Exists(candidate))
            return candidate;

        return null;
    }
}