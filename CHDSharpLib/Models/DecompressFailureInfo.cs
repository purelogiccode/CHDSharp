using System.Text;

namespace CHDSharp.Models;

/// <summary>
///     Snapshot of the first hunk that failed during parallel verification (or of a
///     whole-image hash mismatch), carrying enough context (hunk index, file location, codec,
///     file size and the codec-level reason) to diagnose the failure from a log entry or bug
///     report.
/// </summary>
internal sealed class DecompressFailureInfo
{
    /// <summary>
    ///     Zero-based index of the hunk that failed to decompress; <c>null</c> when the failure
    ///     is not tied to a single hunk (e.g. a whole-image hash mismatch).
    /// </summary>
    public int? HunkIndex { get; init; }

    /// <summary>Total number of hunks in the image.</summary>
    public int TotalHunks { get; init; }

    /// <summary>File offset of the hunk's compressed data (or source hunk for self-references).</summary>
    public ulong HunkOffset { get; init; }

    /// <summary>Compressed length in bytes (0 for implicit/uncompressed hunk types).</summary>
    public uint CompressedLength { get; init; }

    /// <summary>Human-readable description of the codec used for this hunk.</summary>
    public string? Compression { get; init; }

    /// <summary>Length of the whole CHD file in bytes, or -1 when it could not be determined.</summary>
    public long FileLength { get; init; } = -1;

    /// <summary>Concrete reason reported by the codec or the pipeline.</summary>
    public string Detail { get; init; } = "";

    /// <summary>Renders the failure as a single diagnostic line.</summary>
    public string Describe()
    {
        var sb = new StringBuilder();
        if (HunkIndex is >= 0)
        {
            sb.Append("hunk ").Append(HunkIndex.Value).Append('/').Append(TotalHunks);
            if (!string.IsNullOrEmpty(Compression))
                sb.Append(", codec ").Append(Compression);
            if (CompressedLength > 0)
                sb.Append(", compressed length ").Append(CompressedLength).Append(" bytes");
            sb.Append(", offset ").Append(HunkOffset).Append(", ");
        }

        sb.Append(
            FileLength >= 0 ? $"file is {FileLength:N0} bytes" : "file size unknown"
        );
        if (!string.IsNullOrEmpty(Detail))
            sb.Append(": ").Append(Detail);

        return sb.ToString();
    }
}