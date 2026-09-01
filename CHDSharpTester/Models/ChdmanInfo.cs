namespace CHDSharpTester.Models;

/// <summary>Represents parsed header information returned by chdman's info command.</summary>
internal sealed class ChdmanInfo
{
    /// <summary>The string description of compression codec(s) used by the CHD (e.g. "zstd", "cdzs", "cdzl,cdfl").</summary>
    internal string Compression = "";

    /// <summary>The raw data SHA1 hash, or null if not present.</summary>
    internal string? DataSha1;

    /// <summary>The size of each hunk, in bytes.</summary>
    internal uint HunkBytes;

    /// <summary>The logical (decompressed) size of the CHD image, in bytes.</summary>
    internal ulong LogicalBytes;

    /// <summary>The overall SHA1 hash (raw data + metadata), or null if not present.</summary>
    internal string? Sha1;

    /// <summary>The total number of hunks in the image.</summary>
    internal uint TotalHunks;

    /// <summary>The CHD file format version.</summary>
    internal int Version;
}