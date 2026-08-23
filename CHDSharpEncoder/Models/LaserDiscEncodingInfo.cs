namespace CHDSharpEncoder.Models;

/// <summary>
/// Summary of a completed <see cref="ChdEncoder.EncodeLaserDisc"/> run: the derived A/V
/// parameters and output geometry (mirrors chdman <c>createld</c>'s console report).
/// </summary>
public sealed record LaserDiscEncodingInfo(
    ulong FpsTimes1Million,
    uint Width,
    uint Height,
    bool Interlaced,
    uint Channels,
    uint SampleRate,
    uint MaxSamplesPerFrame,
    uint BytesPerFrame,
    uint HunkBytes,
    ulong FirstFrame,
    ulong Frames);
