namespace CHDSharpBattleTest.Models;

/// <summary>Parsed <c>chdman info</c> output (the fields that matter for cross-checks).</summary>
public sealed record ChdmanInfo(
    int Version,
    ulong LogicalBytes,
    uint HunkBytes,
    uint TotalHunks,
    uint UnitBytes,
    uint TotalUnits,
    string Compression,
    long ChdSize,
    string? Sha1,
    string? DataSha1,
    string? Md5,
    string? ParentSha1,
    string? ParentMd5);
