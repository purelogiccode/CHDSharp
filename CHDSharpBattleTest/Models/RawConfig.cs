namespace CHDSharpBattleTest.Models;

/// <summary>One raw-encode configuration (codecs + hunk/unit sizes).</summary>
internal sealed record RawConfig(string Codecs, uint HunkBytes, uint UnitBytes)
{
    public string Label => $"{Codecs}({HunkBytes}/{UnitBytes})";
}