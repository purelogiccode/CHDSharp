namespace CHDSharp.Models;

/// <summary>Hash algorithms that <see cref="Chd.ComputeHashes" /> can compute over CHD content.</summary>
[Flags]
public enum ChdHashType
{
    /// <summary>No hash algorithms selected.</summary>
    None = 0x0000,

    /// <summary>SHA-1 (20 bytes) — the hash stored in V3-V5 CHD headers.</summary>
    Sha1 = 0x0001,

    /// <summary>SHA-256 (32 bytes).</summary>
    Sha256 = 0x0002,

    /// <summary>CRC-32 (IEEE 802.3, 4 bytes).</summary>
    Crc32 = 0x0004,

    /// <summary>XXH3-64 (8 bytes), the fast non-cryptographic hash used by Redump/CHDlite.</summary>
    Xxh3 = 0x0008,
}
