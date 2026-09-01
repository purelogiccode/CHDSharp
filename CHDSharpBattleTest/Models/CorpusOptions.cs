namespace CHDSharpBattleTest.Models;

/// <summary>
///     Options for the timed real-corpus battle pipeline (ported from the former
///     CHDBattleTest harness): which battles run, codecs, worker count, and
///     corpus filters.
/// </summary>
internal sealed class CorpusOptions
{
    public bool Battles { get; set; } = true;
    public bool ListOnly { get; set; }
    public bool Resume { get; set; }
    public bool IncludeAv { get; set; }
    public bool LibDecode { get; set; }
    public bool KeepTemp { get; set; }
    public string CodecRaw { get; set; } = "zstd";
    public string CodecCd { get; set; } = "cdzl";
    public int Workers { get; set; } = Environment.ProcessorCount;
    public string Filter { get; set; } = "*.chd";
    public double MinMb { get; set; }
    public double MaxMb { get; set; }
    public int MaxFiles { get; set; }

    public string CodecFor(MediaKind kind)
    {
        return kind switch
        {
            MediaKind.Cd or MediaKind.GdRom => CodecCd,
            MediaKind.LaserDisc => "avhu",
            _ => CodecRaw
        };
    }
}
