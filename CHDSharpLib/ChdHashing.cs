using System.Diagnostics;
using System.Security.Cryptography;
using System.IO.Hashing;

namespace CHDSharp;

public static partial class Chd
{
    /// <summary>
    /// Computes hash digests over a CHD's decompressed content, returning a <see cref="ChdHashComputeResult"/>
    /// with the error code instead of throwing on failure. For CD/GD-ROM images with <paramref name="perTrack"/>
    /// set, one hash per track is returned; otherwise a single whole-image hash is returned.
    /// </summary>
    /// <param name="filename">Path to the CHD file (standalone; child CHDs need
    /// <paramref name="parentFilename"/>).</param>
    /// <param name="types">The hash algorithms to compute (bitwise OR of <see cref="ChdHashType"/>).</param>
    /// <param name="parentFilename">Parent CHD path for a child CHD, or <c>null</c> for standalone.</param>
    /// <param name="perTrack">For CD/GD-ROM images, hash each track separately instead of the whole image.</param>
    /// <param name="progress">An optional <see cref="IProgress{T}"/> receiving a <see cref="ChdProgress"/>
    /// report after each decompressed hunk.</param>
    /// <param name="cancellationToken">A token to cancel the hashing. <see cref="OperationCanceledException"/>
    /// is thrown if cancellation is requested.</param>
    /// <returns>A <see cref="ChdHashComputeResult"/> with the error code and hash results.</returns>
    public static ChdHashComputeResult ComputeHashesWithReporting(string filename, ChdHashType types,
        string? parentFilename = null, bool perTrack = false, IProgress<ChdProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(filename);
        if (types == ChdHashType.None)
            return new ChdHashComputeResult(ChdError.Chderrnone, []);

        var err = ChdFile.Open(filename, parentFilename, out var chd);
        if (err != ChdError.Chderrnone || chd == null)
            return new ChdHashComputeResult(err, []);

        using (chd)
        {
            var regions = new List<(int? Track, ulong Offset, long Length)>();
            if (perTrack && chd.Tracks is { Count: > 0 })
            {
                var unitBytes = chd.UnitBytes;
                foreach (var track in chd.Tracks)
                {
                    var start = track.StartFrame * unitBytes;
                    var length = (track.Frames + track.ExtraFrames) * unitBytes;
                    regions.Add((track.TrackNumber, start, length));
                }
            }
            else
            {
                regions.Add((null, 0, (long)chd.TotalBytes));
            }

            var results = new List<ChdHashResult>(regions.Count);
            foreach (var (track, offset, length) in regions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var hashResult = HashRegionWithReporting(chd, offset, (ulong)length, types, track, progress, cancellationToken);
                if (hashResult.Error != ChdError.Chderrnone)
                    return new ChdHashComputeResult(hashResult.Error, []);

                results.Add(hashResult.Result!);
            }

            return new ChdHashComputeResult(ChdError.Chderrnone, results);
        }
    }

    /// <summary>
    /// Computes hash digests over a CHD's decompressed content (CHDlite <c>hash_content</c>
    /// parity). For CD/GD-ROM images with <paramref name="perTrack"/> set, one hash per track is
    /// returned (track boundaries from <see cref="ChdFile.Tracks"/>); otherwise a single
    /// whole-image hash is returned. Reading and hashing happen in one sequential pass.
    /// </summary>
    /// <param name="filename">Path to the CHD file (standalone; child CHDs need
    /// <paramref name="parentFilename"/>).</param>
    /// <param name="types">The hash algorithms to compute (bitwise OR of <see cref="ChdHashType"/>).</param>
    /// <param name="parentFilename">Parent CHD path for a child CHD, or <c>null</c> for standalone.</param>
    /// <param name="perTrack">For CD/GD-ROM images, hash each track separately instead of the whole image.</param>
    /// <param name="progress">An optional <see cref="IProgress{T}"/> receiving a <see cref="ChdProgress"/>
    /// report after each decompressed hunk.</param>
    /// <param name="cancellationToken">A token to cancel the hashing. <see cref="OperationCanceledException"/>
    /// is thrown if cancellation is requested.</param>
    /// <returns>One <see cref="ChdHashResult"/> per hashed region (track or whole image), in order.
    /// Empty when <paramref name="types"/> is <see cref="ChdHashType.None"/>.</returns>
    /// <exception cref="InvalidDataException">The CHD cannot be opened or a hunk fails to decompress.</exception>
    public static IReadOnlyList<ChdHashResult> ComputeHashes(string filename, ChdHashType types,
        string? parentFilename = null, bool perTrack = false, IProgress<ChdProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = ComputeHashesWithReporting(filename, types, parentFilename, perTrack, progress, cancellationToken);
        if (result.Error != ChdError.Chderrnone)
            throw new InvalidDataException($"Cannot open CHD '{filename}' ({result.Error.GetMessage()} ({result.Error}))");

        return result.Results;
    }

    private static (ChdError Error, ChdHashResult? Result) HashRegionWithReporting(ChdFile chd, ulong offset, ulong length, ChdHashType types,
        int? trackNumber, IProgress<ChdProgress>? progress, CancellationToken cancellationToken)
    {
        using var sha1 = (types & ChdHashType.Sha1) != ChdHashType.None ? IncrementalHash.CreateHash(HashAlgorithmName.SHA1) : null;
        using var sha256 = (types & ChdHashType.Sha256) != ChdHashType.None ? IncrementalHash.CreateHash(HashAlgorithmName.SHA256) : null;
        var crc32 = (types & ChdHashType.Crc32) != ChdHashType.None ? new Crc32() : null;
        var xxh3 = (types & ChdHashType.Xxh3) != ChdHashType.None ? new XxHash3() : null;

        var sw = progress != null ? Stopwatch.StartNew() : null;
        var buffer = new byte[chd.HunkBytes];
        var remaining = length;
        while (remaining > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var chunk = (int)Math.Min((ulong)buffer.Length, remaining);
            var err = chd.Read(offset, buffer, 0, chunk, cancellationToken);
            if (err != ChdError.Chderrnone)
                return (err, null);

            sha1?.AppendData(buffer, 0, chunk);
            sha256?.AppendData(buffer, 0, chunk);
            crc32?.Append(buffer.AsSpan(0, chunk));
            xxh3?.Append(buffer.AsSpan(0, chunk));

            offset += (ulong)chunk;
            remaining -= (ulong)chunk;

            progress?.Report(new ChdProgress(
                (uint)((offset - (length - remaining)) / chd.HunkBytes),
                (uint)((length + chd.HunkBytes - 1) / chd.HunkBytes),
                (long)(length - remaining),
                (long)length,
                sw!.Elapsed));
        }

        return (ChdError.Chderrnone, new ChdHashResult(
            trackNumber,
            offset - length,
            (long)length,
            types.HasFlag(ChdHashType.Sha1) ? sha1!.GetHashAndReset() : null,
            types.HasFlag(ChdHashType.Sha256) ? sha256!.GetHashAndReset() : null,
            types.HasFlag(ChdHashType.Crc32) ? crc32!.GetCurrentHashAsUInt32() : null,
            types.HasFlag(ChdHashType.Xxh3) ? xxh3!.GetCurrentHashAsUInt64() : null));
    }
}