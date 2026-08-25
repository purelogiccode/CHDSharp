namespace CHDSharpBattleTest.Models;

/// <summary>One assertion result from the battle run.</summary>
internal sealed record CheckResult(string Suite, string Name, string Detail, bool Passed, bool Skipped, double Seconds);