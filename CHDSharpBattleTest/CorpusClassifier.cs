using CHDSharp;

namespace CHDSharpBattleTest;

/// <summary>Media class of a corpus CHD, derived from the CHD metadata classification.</summary>
internal enum MediaKind
{
    Cd,
    GdRom,
    Dvd,
    Hdd,
    LaserDisc,
    Unknown
}

/// <summary>Header-only inspection result for one corpus file.</summary>
internal sealed record CorpusInfo(
    bool IsChd,
    uint Version,
    MediaKind Kind,
    ulong LogicalBytes,
    uint HunkBytes,
    uint UnitBytes,
    string? Error);

/// <summary>
///     Classifies a corpus CHD (version, logical size, media kind) without decoding it,
///     so corpus battles can pick the right extract/create command pair per file.
/// </summary>
internal static class CorpusClassifier
{
    internal static CorpusInfo Inspect(string path)
    {
        if (!Chd.IsChdFile(path, out var version))
            return new CorpusInfo(false, 0, MediaKind.Unknown, 0, 0, 0, "not a CHD file");

        var err = ChdFile.Open(path, out var chd);
        if (err != ChdError.Chderrnone)
            return new CorpusInfo(true, version, MediaKind.Unknown, 0, 0, 0, $"open failed: {err}");

        try
        {
            if (chd == null)
                return new CorpusInfo(true, version, MediaKind.Unknown, 0, 0, 0, "open failed: null instance");

            var logical = (ulong)chd.HunkCount * chd.HunkBytes;
            return new CorpusInfo(true, version, DetectKind(path, chd), logical,
                chd.HunkBytes, chd.UnitBytes, null);
        }
        finally
        {
            chd?.Dispose();
        }
    }

    private static MediaKind DetectKind(string path, ChdFile chd)
    {
        var classErr = Chd.Classify(path, out var classification);
        if (classErr == ChdError.Chderrnone && classification is not null)
            return classification switch
            {
                "cd" => MediaKind.Cd,
                "gd-rom" => MediaKind.GdRom,
                "dvd" => MediaKind.Dvd,
                "hdd" => MediaKind.Hdd,
                _ => MediaKind.Unknown
            };

        // laser-disc CHDs carry AVLD/AVAV metadata instead of a class
        foreach (var meta in chd.Metadata)
        {
            var s = meta.ToString() ?? "";
            if (s.Contains("AVLD", StringComparison.Ordinal) || s.Contains("AVAV", StringComparison.Ordinal))
                return MediaKind.LaserDisc;
        }

        return MediaKind.Unknown;
    }
}
