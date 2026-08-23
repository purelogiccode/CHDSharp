namespace CHDSharpEncoder.Models;

/// <summary>Parsed VBI metadata for one frame.</summary>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
public struct VbiMetadata
{
    /// <summary>White flag: on or off.</summary>
    public uint White;

    /// <summary>Line 16 code.</summary>
    public uint Line16;

    /// <summary>Line 17 code.</summary>
    public uint Line17;

    /// <summary>Line 18 code.</summary>
    public uint Line18;

    /// <summary>Most plausible value from lines 17/18.</summary>
    public uint Line1718;
}
