namespace CHDSharpBattleTest.Models;

/// <summary>Result of running a chdman command: exit code plus captured stdout/stderr.</summary>
internal sealed record RunResult(int ExitCode, string Stdout, string Stderr, double Seconds = 0)
{
    public string Combined => Stdout + Stderr;
}