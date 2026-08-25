namespace CHDSharpBattleTest.Models;

/// <summary>A CHD produced during the run, decoded exhaustively by the decode suite.</summary>
internal sealed class Asset
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public required string ChdPath { get; init; }
    public string? ParentPath { get; init; }
    public required byte[] Expected { get; init; }
    public required bool IsCd { get; init; }
    public required string CodecLabel { get; init; }
}