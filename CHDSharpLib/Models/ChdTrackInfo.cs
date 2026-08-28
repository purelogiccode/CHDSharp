namespace CHDSharp.Models;

/// <summary>
///     Represents a single track in a CD/GD-ROM CHD image, including type, size, pregap/postgap, and frame offset
///     information.
/// </summary>
public sealed class ChdTrackInfo
{
    /// <summary>1-based track number.</summary>
    public int TrackNumber { get; init; }

    /// <summary>CD track data type (Mode1, Audio, etc.).</summary>
    public ChdTrackType TrackType { get; init; }

    /// <summary>Subcode type for this track.</summary>
    public ChdSubType SubType { get; init; }

    /// <summary>Bytes per sector for this track (2048, 2352, etc.).</summary>
    public int DataSize { get; init; }

    /// <summary>Subcode bytes per sector (0 or 96).</summary>
    public int SubSize { get; init; }

    /// <summary>Number of frames in this track.</summary>
    public int Frames { get; init; }

    /// <summary>Padding frames added for 4-frame alignment.</summary>
    public int ExtraFrames { get; init; }

    /// <summary>Pregap frames (index 00 to index 01).</summary>
    public int PreGap { get; init; }

    /// <summary>Postgap frames.</summary>
    public int PostGap { get; init; }

    /// <summary>Track type of pregap sectors.</summary>
    public ChdTrackType PreGapType { get; init; }

    /// <summary>Subcode type of pregap sectors.</summary>
    public ChdSubType PreGapSubType { get; init; }

    /// <summary>Bytes per sector for pregap data.</summary>
    public int PreGapDataSize { get; init; }

    /// <summary>Subcode bytes per sector for pregap.</summary>
    public int PreGapSubSize { get; init; }

    /// <summary>GD-ROM pad frames (GD-ROM only).</summary>
    public int PadFrames { get; init; }

    /// <summary>
    ///     GD-ROM split frames — number of frames from the next track to append to the end of
    ///     the previous track after padding (Redump split-bin; <c>track_info::splitframes</c> in
    ///     <c>cdrom.h:103</c>). Used only during GD-ROM CUE/BIN extraction fixup
    ///     (<c>chdman.cpp:2886</c>). Zero for all tracks except the HD-area tracks that require
    ///     Redump reinterpretation.
    /// </summary>
    public int SplitFrames { get; init; }

    /// <summary>Physical frame offset where this track starts (cumulative <c>Frames</c> without <c>ExtraFrames</c>).</summary>
    public ulong PhysFrameOfs { get; init; }

    /// <summary>CHD frame offset where this track starts.</summary>
    public ulong StartFrame { get; init; }

    /// <summary>Returns a human-readable track type string such as "MODE1/2048" or "AUDIO".</summary>
    public string GetTypeString()
    {
        return TrackType switch
        {
            ChdTrackType.Mode1 => "MODE1/2048",
            ChdTrackType.Mode1Raw => "MODE1/2352",
            ChdTrackType.Mode2 => "MODE2/2336",
            ChdTrackType.Mode2Form1 => "MODE2/2048",
            ChdTrackType.Mode2Form2 => "MODE2/2324",
            ChdTrackType.Mode2FormMix => "MODE2/2336",
            ChdTrackType.Mode2Raw => "MODE2/2352",
            ChdTrackType.Audio => "AUDIO",
            _ => "UNKNOWN"
        };
    }

    /// <summary>Returns a human-readable subcode type string: "RW", "RW_RAW", or "NONE".</summary>
    public string GetSubTypeString()
    {
        return SubType switch
        {
            ChdSubType.Normal => "RW",
            ChdSubType.Raw => "RW_RAW",
            _ => "NONE"
        };
    }

    /// <summary>Returns MAME <c>get_type_string</c> value (MODE1, MODE1_RAW, MODE2, etc., AUDIO) without size suffix.</summary>
    public string GetMameTypeString()
    {
        return TrackType switch
        {
            ChdTrackType.Mode1 => "MODE1",
            ChdTrackType.Mode1Raw => "MODE1_RAW",
            ChdTrackType.Mode2 => "MODE2",
            ChdTrackType.Mode2Form1 => "MODE2_FORM1",
            ChdTrackType.Mode2Form2 => "MODE2_FORM2",
            ChdTrackType.Mode2FormMix => "MODE2_FORM_MIX",
            ChdTrackType.Mode2Raw => "MODE2_RAW",
            ChdTrackType.Audio => "AUDIO",
            _ => "UNKNOWN"
        };
    }

    /// <summary>Returns MAME <c>get_subtype_string</c> value (RW, RW_RAW, NONE).</summary>
    public string GetMameSubTypeString()
    {
        return SubType switch
        {
            ChdSubType.Normal => "RW",
            ChdSubType.Raw => "RW_RAW",
            _ => "NONE"
        };
    }
}