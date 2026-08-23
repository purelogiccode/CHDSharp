using System.Diagnostics;

namespace CHDSharpBattleTest;

/// <summary>
/// Thin wrapper around the CHDSharpCli executable: runs commands, captures output, and
/// returns exit codes for cross-checking against chdman.exe.
/// </summary>
internal sealed class CliRunner
{
    internal string ExePath { get; }

    internal CliRunner(string exePath)
    {
        ExePath = exePath;
    }

    /// <summary>Runs <c>CHDSharp &lt;command&gt; [args...]</c> and captures stdout/stderr.</summary>
    internal RunResult Run(string command, params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ExePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add(command);
        foreach (var a in args)
            psi.ArgumentList.Add(a);

        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start {ExePath}");
        var tOut = p.StandardOutput.ReadToEndAsync();
        var tErr = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(300_000))
        {
            try
            {
                p.Kill(entireProcessTree: true);
            }
            catch
            {
                // ignore
            }

            throw new TimeoutException($"CHDSharp {command} timed out after 300s");
        }

        return new RunResult(p.ExitCode, tOut.Result, tErr.Result);
    }

    /// <summary>Runs <c>CHDSharp info</c> and returns the raw output text.</summary>
    internal string? InfoRaw(string chdPath)
    {
        var r = Run("info", "-i", chdPath);
        return r.ExitCode == 0 ? r.Combined : null;
    }
}
