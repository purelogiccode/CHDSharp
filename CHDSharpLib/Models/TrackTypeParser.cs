namespace CHDSharp.Models;

/// <summary>Identifies which legacy/modern TOC metadata tag a parsed track set came from.</summary>
internal enum TrackTypeParser
{
    /// <summary>Legacy V1-V4 CHTR metadata tag.</summary>
    Chtr,

    /// <summary>Modern CHT2 metadata tag (V5).</summary>
    Cht2,

    /// <summary>GD-ROM TOC metadata tag.</summary>
    GdRom
}