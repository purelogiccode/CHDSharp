namespace CHDSharpBattleTest.Models;

/// <summary>Result of running a chdman command: exit code plus captured stdout/stderr.</summary>
public sealed record RunResult(int ExitCode, string Stdout, string Stderr)
{
    public string Combined => Stdout + Stderr;
}
