namespace CHDSharp.Models;

/// <summary>Hashes of one contiguous region of a CHD's decompressed content.</summary>
/// <param name="TrackNumber">The 1-based CD track number for per-track hashing, or <c>null</c> for the whole image.</param>
/// <param name="StartOffset">Byte offset of the region within the decompressed image.</param>
/// <param name="Length">Length of the region in bytes.</param>
/// <param name="Sha1">SHA-1 of the region, or <c>null</c> if not requested.</param>
/// <param name="Sha256">SHA-256 of the region, or <c>null</c> if not requested.</param>
/// <param name="Crc32">CRC-32 of the region, or <c>null</c> if not requested.</param>
/// <param name="Xxh3">XXH3-64 of the region, or <c>null</c> if not requested.</param>
public sealed record ChdHashResult(
    int? TrackNumber,
    ulong StartOffset,
    long Length,
    byte[]? Sha1,
    byte[]? Sha256,
    uint? Crc32,
    ulong? Xxh3
)
{
    /// <summary>Formats a hex string for one of the hashes, or <c>null</c> when unavailable.</summary>
    public string? ToHex(ChdHashType type)
    {
        return type switch
        {
            ChdHashType.Sha1 => Sha1 is null ? null : Convert.ToHexString(Sha1).ToLowerInvariant(),
            ChdHashType.Sha256 => Sha256 is null
                ? null
                : Convert.ToHexString(Sha256).ToLowerInvariant(),
            ChdHashType.Crc32 => Crc32?.ToString("X8").ToLowerInvariant(),
            ChdHashType.Xxh3 => Xxh3?.ToString("X16").ToLowerInvariant(),
            _ => null,
        };
    }
}
