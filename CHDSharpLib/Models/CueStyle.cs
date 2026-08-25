namespace CHDSharp.Models;

/// <summary>CUE sheet output styles (CHDlite <c>CueStyle</c> parity).</summary>
public enum CueStyle
{
    /// <summary>chdman-style: single-track discs get a " (Track 1)" file suffix, no CATALOG line.</summary>
    Chdman = 0,

    /// <summary>Redump-style: no CATALOG line, " (Track 1)" suffixes removed.</summary>
    Redump = 1,

    /// <summary>Redump style with a CATALOG line prepended.</summary>
    RedumpCatalog = 2
}