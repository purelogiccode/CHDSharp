using System.Security.Cryptography;
using System.Text;
using CHDSharp.Utils;
using Microsoft.Extensions.Logging;

namespace CHDSharp;

/// <summary>
///     Reads, validates, and hashes CHD metadata entries from the metadata chain, and computes the combined (overall)
///     SHA-1 for V4/V5 CHDs.
/// </summary>
internal static class ChdMetaData
{
    private const uint ChdMdflagsChecksum = 0x01;

    private const uint MaxMetadataEntryBytes = 64 * 1024;
    private static readonly ILogger Log = ChdLogger.GetLogger(nameof(ChdMetaData));

    private static readonly Action<ILogger, string, uint, Exception?> LogMetaTag =
        LoggerMessage.Define<string, uint>(LogLevel.Debug, new EventId(1), "{Tag}  Length: {Length}");

    private static readonly Action<ILogger, string, Exception?> LogMetaDataText =
        LoggerMessage.Define<string>(LogLevel.Debug, new EventId(2), "Data: {Data}");

    private static readonly Action<ILogger, int, Exception?> LogMetaDataBinary =
        LoggerMessage.Define<int>(LogLevel.Debug, new EventId(3), "Data: Binary Data Length {Length}");

    /// <summary>Reads all metadata entries and validates the combined SHA-1 hash stored in the header (V4/V5).</summary>
    /// <param name="file">The stream containing the CHD file.</param>
    /// <param name="chd">The parsed CHD header with metadata offset and hash fields.</param>
    /// <returns>
    ///     <see cref="ChdError.Chderrnone" /> on success; <see cref="ChdError.Chderrinvalidmetadata" /> if the combined
    ///     hash does not match; otherwise a read/parse error code.
    /// </returns>
    internal static ChdError ReadMetaData(Stream file, ChdHeader chd)
    {
        if (chd.Rawsha1 is not { Length: 20 } || chd.Sha1 is not { Length: 20 } || Util.IsAllZeroArray(chd.Sha1))
            return ChdError.Chderrnone;

        var metaHashes = new List<byte[]>();

        var metaErr = ReadMetaDataInternal(file, chd, true, out var entries);
        if (metaErr != ChdError.Chderrnone)
            return metaErr;

        foreach (var entry in entries)
            if (entry.Hash != null)
                metaHashes.Add(entry.Hash);

        metaHashes.Sort(Util.ByteArrCompare);

        using var sha1Total = SHA1.Create();
        sha1Total.TransformBlock(chd.Rawsha1, 0, chd.Rawsha1.Length, null, 0);

        foreach (var t in metaHashes)
            sha1Total.TransformBlock(t, 0, t.Length, null, 0);

        var tmp = Array.Empty<byte>();
        sha1Total.TransformFinalBlock(tmp, 0, 0);

        if (!Util.IsAllZeroArray(chd.Sha1) && !Util.ByteArrEquals(chd.Sha1, sha1Total.Hash!))
            return ChdError.Chderrinvalidmetadata;

        return ChdError.Chderrnone;
    }

    /// <summary>Reads all metadata entries from the chain without validating the combined SHA-1 hash.</summary>
    /// <param name="file">The stream containing the CHD file.</param>
    /// <param name="chd">The parsed CHD header with the metadata offset.</param>
    /// <param name="entries">
    ///     When this method returns, contains the list of parsed metadata entries on success, or an empty
    ///     list on error.
    /// </param>
    /// <returns><see cref="ChdError.Chderrnone" /> on success; otherwise a read/parse error code.</returns>
    internal static ChdError ReadMetaDataEntries(Stream file, ChdHeader chd,
        out List<ChdMetadataEntry> entries)
    {
        entries = [];
        var metaErr = ReadMetaDataInternal(file, chd, false, out var internalEntries);
        if (metaErr != ChdError.Chderrnone)
            return metaErr;

        foreach (var e in internalEntries) entries.Add(new ChdMetadataEntry(e.Tag, e.Data) { Flags = e.Flags });

        return ChdError.Chderrnone;
    }

    /// <summary>
    ///     Computes the combined (overall) SHA-1 of a V4/V5 CHD: <c>SHA1(rawsha1 ‖ sorted hashes)</c>
    ///     where each hash is the big-endian 4-byte metadata tag followed by the SHA-1 of the entry
    ///     payload (checksummed entries only, sorted byte-wise) — MAME <c>compute_overall_sha1</c>
    ///     parity. Returns <c>null</c> when the header has no SHA-1 fields to anchor the computation
    ///     (V1/V2, or V3 whose "sha1" is the raw hash), or when the metadata chain cannot be read.
    /// </summary>
    internal static byte[]? ComputeOverallSha1(Stream file, ChdHeader chd, byte[] rawSha1)
    {
        if (rawSha1 is not { Length: 20 } || Util.IsAllZeroArray(rawSha1))
            return null;

        var metaHashes = new List<byte[]>();
        var metaErr = ReadMetaDataInternal(file, chd, true, out var entries);
        if (metaErr != ChdError.Chderrnone)
            return null;

        foreach (var entry in entries)
            if (entry.Hash != null)
                metaHashes.Add(entry.Hash);

        metaHashes.Sort(Util.ByteArrCompare);

        using var sha1Total = SHA1.Create();
        sha1Total.TransformBlock(rawSha1, 0, rawSha1.Length, null, 0);
        foreach (var t in metaHashes)
            sha1Total.TransformBlock(t, 0, t.Length, null, 0);

        var tmp = Array.Empty<byte>();
        sha1Total.TransformFinalBlock(tmp, 0, 0);
        return sha1Total.Hash;
    }

    private static ChdError ReadMetaDataInternal(Stream file, ChdHeader chd,
        bool collectHashes, out List<InternalEntry> entries)
    {
        entries = [];
        using var br = new BinaryReader(file, Encoding.UTF8, true);

        var currentOffset = chd.Metaoffset;
        var visitedOffsets = new HashSet<ulong>();
        while (currentOffset != 0)
        {
            if (!visitedOffsets.Add(currentOffset))
                break;

            file.Seek((long)currentOffset, SeekOrigin.Begin);
            var metaTag = br.ReadUInt32Be();
            var metaLength = br.ReadUInt32Be();
            var metaNext = br.ReadUInt64Be();
            var metaFlags = metaLength >> 24;
            metaLength &= 0x00ffffff;

            if (metaLength > MaxMetadataEntryBytes)
                return ChdError.Chderrinvaliddata;

            var metaData = new byte[metaLength];
            file.ReadExactly(metaData, 0, metaData.Length);

            var tag =
                $"{(char)((metaTag >> 24) & 0xFF)}{(char)((metaTag >> 16) & 0xFF)}{(char)((metaTag >> 8) & 0xFF)}{(char)((metaTag >> 0) & 0xFF)}";

            LogMetaTag(Log, tag, metaLength, null);
            if (Util.IsAscii(metaData))
                LogMetaDataText(Log, Encoding.ASCII.GetString(metaData), null);
            else
                LogMetaDataBinary(Log, metaData.Length, null);

            byte[]? hash = null;
            if (collectHashes && (metaFlags & ChdMdflagsChecksum) != 0) hash = metadata_hash(metaTag, metaData);

            entries.Add(new InternalEntry { Tag = tag, Data = metaData, Hash = hash, Flags = (byte)metaFlags });

            currentOffset = metaNext;
        }

        return ChdError.Chderrnone;
    }

    private static byte[] metadata_hash(uint metaTag, byte[] metaData)
    {
        var metaHash = new byte[24];
        metaHash[0] = (byte)((metaTag >> 24) & 0xff);
        metaHash[1] = (byte)((metaTag >> 16) & 0xff);
        metaHash[2] = (byte)((metaTag >> 8) & 0xff);
        metaHash[3] = (byte)((metaTag >> 0) & 0xff);
        var metaDataHash = SHA1.HashData(metaData);

        for (var i = 0; i < 20; i++) metaHash[4 + i] = metaDataHash[i];

        return metaHash;
    }

    private sealed class InternalEntry
    {
        public required byte[] Data;
        public byte Flags;
        public byte[]? Hash;
        public required string Tag;
    }
}