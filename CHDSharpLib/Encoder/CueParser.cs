using System.Globalization;
using System.Text;
using CHDSharp.Encoder.Models;

namespace CHDSharp.Encoder;

/// <summary>
///     Parses CUE sheets (CUE/BIN, CUE/ISO, CUE/WAV) into a <see cref="CdToc" />.
///     The parsing logic mirrors MAME's <c>cdrom_file::parse_cue</c> (src/lib/util/cdrom.cpp),
///     including track length and file offset resolution.
/// </summary>
public static class CueParser
{
    /// <summary>Redump multi-cue density areas (MAME's <c>gdi_area</c>).</summary>
    private const int SingleDensity = 0;

    /// <summary>Redump multi-cue high-density area marker value.</summary>
    private const int HighDensity = 1;

    /// <summary>LBA the GD-ROM high-density area starts at (MAME's parse_cue).</summary>
    private const int HighDensityStartLba = 45000;

    /// <summary>
    ///     Parses the numeric/time value of a REM extension line ("REM SESSION 02",
    ///     "REM LEAD-OUT 00:30:00"). MAME's tokenizer yields 0 for a missing or garbage value;
    ///     the built-in parser fails loudly instead, because a malformed session layout would
    ///     otherwise be encoded as a silently wrong image.
    /// </summary>
    /// <param name="remText">The REM line text, after the "REM" keyword.</param>
    /// <param name="rawLine">The raw line, for the error message.</param>
    /// <returns>The parsed frame count (or session number).</returns>
    private static int ParseRemValue(string remText, string rawLine)
    {
        var tokens = Tokenize(remText);
        if (tokens.Count < 2)
            throw new InvalidDataException($"Malformed REM command: {rawLine}");

        return ParseMsfToFrames(tokens[1]);
    }

    /// <summary>
    ///     Parses a CUE sheet file into a table of contents.
    /// </summary>
    /// <param name="cueFilePath">Path to the .cue file.</param>
    /// <returns>The parsed table of contents.</returns>
    /// <exception cref="FileNotFoundException">The CUE file or a referenced data file does not exist.</exception>
    /// <exception cref="InvalidDataException">The CUE file is malformed or uses an unsupported track/file type.</exception>
    public static CdToc Parse(string cueFilePath)
    {
        ArgumentNullException.ThrowIfNull(cueFilePath);
        if (!File.Exists(cueFilePath))
            throw new FileNotFoundException($"CUE file not found: {cueFilePath}", cueFilePath);

        var baseDir = Path.GetDirectoryName(Path.GetFullPath(cueFilePath)) ?? string.Empty;

        // Redump's Dreamcast multi-cue (is_gdicue): two images on one disc, marked by REM area
        // comments. MAME treats such cues as GD-ROMs and applies the density-area layout below.
        var isGdRom = IsGdiCue(cueFilePath);
        var currentArea = SingleDensity;

        // Multisession state (MAME's parse_cue): session number from REM SESSION, a pending
        // session pregap, a pending lead-in for the next track, and whether the cue spans more
        // than one data file (single-bin IsoBuster/ImgBurn vs multi-bin Redump/DiscImageCreator).
        var isMultiBin = false;
        var sessionNumber = 0;
        var sessionPregap = 0;
        var pendingLeadIn = -1;

        var toc = new CdToc { Flags = isGdRom ? CdTocFlags.GdRom : 0 };
        var tracks = toc.Tracks;
        CdTrack? currentTrack = null;
        var lastFile = string.Empty;
        long wavLength = 0;
        long wavOffset = 0;

        foreach (var rawLine in File.ReadLines(cueFilePath))
        {
            var tokens = Tokenize(rawLine);
            if (tokens.Count == 0)
                continue;

            switch (tokens[0])
            {
                case "REM":
                {
                    // MAME's parse_cue reads the IsoBuster/ImgBurn/Redump REM extensions here:
                    // session numbers, session pregaps, session lead-in/lead-out times, and the
                    // Redump multi-cue density-area markers.
                    var text = rawLine.TrimStart();
                    if (!text.StartsWith("REM", StringComparison.Ordinal))
                        break;

                    text = text[3..].TrimStart();

                    if (text.StartsWith("SESSION", StringComparison.Ordinal))
                    {
                        // IsoBuster extension: "REM SESSION n" (1-based in the file).
                        sessionNumber = ParseRemValue(text, rawLine) - 1;
                        if (sessionNumber >= 1)
                            toc.Flags |= CdTocFlags.Multisession;
                    }
                    else if (
                        (toc.Flags & CdTocFlags.Multisession) != 0
                        && text.StartsWith("PREGAP", StringComparison.Ordinal)
                    )
                    {
                        // Session pregap (Redump/DiscImageCreator extension): folded into the
                        // lead-in of the session's first track.
                        sessionPregap = ParseRemValue(text, rawLine);
                    }
                    else if (text.StartsWith("LEAD-OUT", StringComparison.Ordinal))
                    {
                        // IsoBuster/ImgBurn (single bin): absolute lead-out position;
                        // Redump/DiscImageCreator (multiple bins): duration of the lead-out.
                        if (currentTrack is { } leadOutTrack)
                        {
                            leadOutTrack.LeadOut = ParseRemValue(text, rawLine);
                            currentTrack = leadOutTrack;
                        }
                    }
                    else if (text.StartsWith("LEAD-IN", StringComparison.Ordinal))
                    {
                        // Consumed by the next TRACK ("use previously saved lead-in value").
                        pendingLeadIn = ParseRemValue(text, rawLine);
                    }
                    else if (
                        isGdRom
                        && text.StartsWith("SINGLE-DENSITY AREA", StringComparison.Ordinal)
                    )
                    {
                        currentArea = SingleDensity;
                    }
                    else if (
                        isGdRom
                        && text.StartsWith("HIGH-DENSITY AREA", StringComparison.Ordinal)
                    )
                    {
                        currentArea = HighDensity;
                    }

                    break;
                }

                case "FILE":
                {
                    if (tokens.Count < 3)
                        throw new InvalidDataException($"Malformed FILE command: {rawLine}");

                    // MAME tracks whether the cue spans more than one data file: the multisession
                    // lead-out padding rules differ between single-bin (IsoBuster/ImgBurn, dummy
                    // data between the lead-out and the next session) and multi-bin
                    // (Redump/DiscImageCreator, sessions in separate files).
                    var resolvedFile = Path.Combine(baseDir, tokens[1]);
                    if (!isMultiBin)
                    {
                        var previousFile = lastFile;
                        lastFile = resolvedFile;
                        isMultiBin =
                            previousFile.Length > 0
                            && !string.Equals(lastFile, previousFile, StringComparison.Ordinal);
                    }
                    else
                    {
                        lastFile = resolvedFile;
                    }

                    switch (tokens[2])
                    {
                        case "BINARY":
                        case "MOTOROLA":
                            break;
                        case "WAVE":
                            (wavLength, wavOffset) = ParseWavSample(lastFile);
                            if (wavLength == 0)
                                throw new InvalidDataException(
                                    $"Couldn't read [{lastFile}] or not a valid .WAV"
                                );

                            break;
                        default:
                            throw new InvalidDataException($"Unhandled file type [{tokens[2]}]");
                    }

                    break;
                }

                case "TRACK":
                {
                    if (tokens.Count < 3)
                        throw new InvalidDataException($"Malformed TRACK command: {rawLine}");
                    if (
                        !int.TryParse(
                            tokens[1],
                            NumberStyles.None,
                            CultureInfo.InvariantCulture,
                            out var trackNumber
                        )
                        || trackNumber < 1
                        || trackNumber > CdConstants.MaxTracks
                    )
                        throw new InvalidDataException($"Invalid track number [{tokens[1]}]");

                    if (currentTrack is { } previous)
                        tracks.Add(previous);

                    var track = new CdTrack
                    {
                        Number = trackNumber,
                        FileName = lastFile,
                        SubType = CdSubType.None,
                        SubSize = 0,
                        PgSub = CdSubType.None,
                        Pregap = 0,
                        Postgap = 0,
                        PgType = 0,
                        PgDataSize = 0,
                        Index00 = -1,
                        Index01 = -1,
                        MulticueArea = isGdRom ? currentArea : 0,
                        Session = sessionNumber,
                        LeadIn = pendingLeadIn,
                        LeadOut = -1
                    };

                    // "use previously saved lead-in value" (MAME): a REM LEAD-IN only applies to
                    // the next TRACK, and a session REM PREGAP folds into that same lead-in.
                    pendingLeadIn = -1;
                    if (sessionPregap != 0)
                    {
                        track.LeadIn =
                            track.LeadIn == -1 ? sessionPregap : track.LeadIn + sessionPregap;
                        sessionPregap = 0;
                    }

                    ParseTrackType(tokens[2], ref track);
                    if (tokens.Count >= 4)
                        ParseSubType(tokens[3], ref track);

                    if (wavLength != 0)
                    {
                        var frames = wavLength / CdConstants.MaxSectorData;
                        if (frames > int.MaxValue)
                            throw new InvalidDataException(
                                $"WAV file frame count ({frames}) exceeds the maximum supported value"
                            );

                        track.Frames = (int)frames;
                        track.FileOffset = wavOffset;
                        wavLength = 0;
                        wavOffset = 0;
                    }

                    currentTrack = track;
                    break;
                }

                case "INDEX":
                {
                    if (currentTrack == null)
                        throw new InvalidDataException(
                            $"INDEX command without a preceding TRACK: {rawLine}"
                        );

                    if (tokens.Count < 3)
                        throw new InvalidDataException($"Malformed INDEX command: {rawLine}");
                    if (
                        !int.TryParse(
                            tokens[1],
                            NumberStyles.None,
                            CultureInfo.InvariantCulture,
                            out var indexNumber
                        )
                        || indexNumber < 0
                        || indexNumber > CdConstants.MaxIndex
                    )
                        throw new InvalidDataException($"Encountered invalid index [{tokens[1]}]");

                    var track = currentTrack.Value;
                    var frames = ParseMsfToFrames(tokens[2]);

                    switch (indexNumber)
                    {
                        case 1:
                        {
                            if (track.Pregap == 0 && track.Index00 != -1)
                            {
                                track.Pregap = frames - track.Index00;
                                track.PgType = track.TrackType;
                                track.PgDataSize = track.DataSize;
                            }
                            else if (track.Index00 == -1)
                            {
                                // no pregap sectors in the file; INDEX 00 defaults to the INDEX 01 position
                                track.Index00 = frames;
                            }

                            track.Index01 = frames;
                            break;
                        }
                        case 0:
                            track.Index00 = frames;
                            break;
                    }

                    currentTrack = track;
                    break;
                }

                case "PREGAP":
                {
                    if (currentTrack == null)
                        throw new InvalidDataException(
                            $"PREGAP command without a preceding TRACK: {rawLine}"
                        );

                    if (tokens.Count < 2)
                        throw new InvalidDataException($"Malformed PREGAP command: {rawLine}");

                    var track = currentTrack.Value;
                    track.Pregap = ParseMsfToFrames(tokens[1]);
                    currentTrack = track;
                    break;
                }

                case "POSTGAP":
                {
                    if (currentTrack == null)
                        throw new InvalidDataException(
                            $"POSTGAP command without a preceding TRACK: {rawLine}"
                        );

                    if (tokens.Count < 2)
                        throw new InvalidDataException($"Malformed POSTGAP command: {rawLine}");

                    var track = currentTrack.Value;
                    track.Postgap = ParseMsfToFrames(tokens[1]);
                    currentTrack = track;
                    break;
                }
            }
        }

        if (currentTrack is { } last)
            tracks.Add(last);

        ResolveTrackLengths(tracks, isMultiBin, (toc.Flags & CdTocFlags.Multisession) != 0);
        if (isGdRom)
            ApplyGdRomLayout(tracks);

        return toc;
    }

    /// <summary>
    ///     Converts an MM:SS:FF (or bare frame count) token into a frame count.
    ///     Mirrors MAME's <c>msf_to_frames</c> for well-formed values; malformed tokens throw
    ///     instead of producing MAME's silent partial parse (a 2-part "MM:SS" time or garbage
    ///     would otherwise be encoded as a silently mistimed disc).
    /// </summary>
    public static int ParseMsfToFrames(string token)
    {
        var parts = token.Split(':');
        switch (parts.Length)
        {
            case 1:
            {
                if (
                    !int.TryParse(
                        parts[0],
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out var frames
                    )
                )
                    throw new InvalidDataException($"Invalid frame count [{token}]");

                return frames;
            }
            case 3
                when int.TryParse(
                         parts[0],
                         NumberStyles.None,
                         CultureInfo.InvariantCulture,
                         out var minutes
                     )
                     && int.TryParse(
                         parts[1],
                         NumberStyles.None,
                         CultureInfo.InvariantCulture,
                         out var seconds
                     )
                     && int.TryParse(
                         parts[2],
                         NumberStyles.None,
                         CultureInfo.InvariantCulture,
                         out var frame
                     ):
                return minutes * 60 * 75 + seconds * 75 + frame;
            default:
                // Intentionally stricter than MAME (which reads "MM:SS" as MM:SS:00 and
                // garbage as 0): silently mis-timing a disc is worse than failing loudly.
                throw new InvalidDataException($"Invalid MSF time format [{token}]");
        }
    }

    /// <summary>
    ///     Resolves each track's length and file offset, mirroring MAME's parse_cue length pass
    ///     including its multisession handling: for single-bin multisession cues the declared
    ///     lead-out trims the session's last track and the inter-session dummy data becomes
    ///     zero-filled pad frames; for multi-bin cues standard lead-in/lead-out values are
    ///     synthesized when the cue omits them. A negative result is rejected with a clear error
    ///     rather than encoded as a silently wrong image.
    /// </summary>
    /// <param name="tracks">The parsed tracks, in track order.</param>
    /// <param name="isMultiBin">True when the cue references more than one data file.</param>
    /// <param name="isMultisession">True when REM SESSION declared more than one session.</param>
    private static void ResolveTrackLengths(
        List<CdTrack> tracks,
        bool isMultiBin,
        bool isMultisession
    )
    {
        for (var i = 0; i < tracks.Count; i++)
        {
            var track = tracks[i];

            if (track.Index01 == -1)
                throw new InvalidDataException($"Track {track.Number} is missing INDEX 01 marker");

            // audio data must be byte-swapped for CHD storage
            if (track.TrackType == CdTrackType.Audio)
                track.Swap = true;

            // WAV tracks already have their length and offset resolved. Like MAME, they also
            // skip the multisession adjustments below (its parse_cue `continue`s here).
            if (track.FileOffset != 0)
            {
                tracks[i] = track;
                continue;
            }

            var sameFileAsPrev =
                i > 0
                && string.Equals(track.FileName, tracks[i - 1].FileName, StringComparison.Ordinal);
            var sameFileAsNext =
                i + 1 < tracks.Count
                && string.Equals(track.FileName, tracks[i + 1].FileName, StringComparison.Ordinal);

            if (i + 1 >= tracks.Count && sameFileAsPrev)
            {
                // last track in a shared file: remainder of the file
                var prevSize =
                    (long)tracks[i - 1].Frames * (tracks[i - 1].DataSize + tracks[i - 1].SubSize);
                track.FileOffset = tracks[i - 1].FileOffset + prevSize;
                track.Frames = (int)(
                    (GetFileSize(track.FileName!) - track.FileOffset)
                    / (track.DataSize + track.SubSize)
                );
            }
            else if (sameFileAsNext)
            {
                track.Frames = tracks[i + 1].Index00 - track.Index00;
                if (track.Frames == 0)
                    throw new InvalidDataException(
                        $"Unable to determine size of track {track.Number}, missing INDEX 01 markers?"
                    );

                if (i > 0)
                {
                    var prevSize =
                        (long)tracks[i - 1].Frames
                        * (tracks[i - 1].DataSize + tracks[i - 1].SubSize);
                    track.FileOffset = tracks[i - 1].FileOffset + prevSize;
                }
            }
            else if (track.Frames == 0)
            {
                // standalone file: whole file is the track
                track.Frames = (int)(
                    GetFileSize(track.FileName!) / (track.DataSize + track.SubSize)
                );
                track.FileOffset = 0;
            }

            if (isMultisession)
            {
                if (isMultiBin)
                {
                    // sessions live in separate files: synthesize the standard lead-out
                    // (1m30s for the first session, 0m30s afterwards) and lead-in (1m00s)
                    if (
                        track.LeadOut == -1
                        && i + 1 < tracks.Count
                        && track.Session != tracks[i + 1].Session
                    )
                        track.LeadOut = track.Session == 0 ? 6750 : 2250;

                    if (track.LeadIn == -1 && i > 0 && track.Session != tracks[i - 1].Session)
                        track.LeadIn = 4500;
                }
                else
                {
                    // single-bin: the declared lead-out is the start of the lead-out, so the
                    // session's last track is trimmed to it and the lead-out becomes the
                    // duration between the lead-out and the next track's pregap
                    if (track.LeadOut != -1)
                    {
                        var endFrames = track.LeadOut - track.Index00;

                        // chdman.cpp:2748 compares the uint32_t frame count with a signed
                        // int32_t end frame, so a lead-out at or before the track's INDEX 00
                        // converts to a huge unsigned value and the trim is skipped instead of
                        // producing a negative length. Mirror that conversion exactly: the
                        // layout then matches chdman byte for byte.
                        if ((uint)track.Frames >= (uint)endFrames)
                        {
                            track.Frames = endFrames;

                            if (i + 1 < tracks.Count)
                                track.LeadOut = tracks[i + 1].Index00 - track.LeadOut;
                        }
                    }

                    if (i > 0 && tracks[i - 1].LeadOut != -1)
                    {
                        // the inter-session dummy data of the previous track becomes an explicit
                        // zero-filled gap, and the next session's data starts after it. Files
                        // with a .img extension have no dummy data (DiscImageCreator) and keep
                        // their layout untouched (MAME's extension heuristic).
                        var previous = tracks[i - 1];
                        var previousName = previous.FileName ?? string.Empty;
                        if (!previousName.EndsWith(".img", StringComparison.OrdinalIgnoreCase))
                        {
                            previous.PadFrames += previous.LeadOut;
                            tracks[i - 1] = previous;
                            track.Frames -= previous.LeadOut;
                            track.FileOffset +=
                                (long)previous.LeadOut * (track.DataSize + track.SubSize);
                        }
                    }
                }
            }

            tracks[i] = track;
        }

        // A malformed session layout must not be encoded as a silently wrong image: MAME's math
        // can go negative on inconsistent lead-out values, which would corrupt the output.
        foreach (var track in tracks)
        {
            if (track.Frames < 0)
                throw new InvalidDataException(
                    $"Track {track.Number}: the session lead-out values leave a negative length ({track.Frames} frames); the cue's REM SESSION/LEAD-OUT values do not match the image."
                );

            if (track.PadFrames < 0)
                throw new InvalidDataException(
                    $"Track {track.Number}: the session lead-out values leave a negative gap ({track.PadFrames} frames); the cue's REM SESSION/LEAD-OUT values do not match the image."
                );

            if (track.FileOffset < 0)
                throw new InvalidDataException(
                    $"Track {track.Number}: the session lead-out values leave a negative file offset; the cue's REM SESSION/LEAD-OUT values do not match the image."
                );
        }
    }

    /// <summary>
    ///     Returns whether a cue is Redump's Dreamcast multi-cue GDI format: both
    ///     "REM SINGLE-DENSITY AREA" and "REM HIGH-DENSITY AREA" comments present
    ///     (mirrors MAME's <c>cdrom_file::is_gdicue</c>).
    /// </summary>
    /// <param name="cueFilePath">Path of the cue file.</param>
    /// <returns>True when the cue is a Redump multi-cue for a Dreamcast GDI disc.</returns>
    private static bool IsGdiCue(string cueFilePath)
    {
        var hasSingleDensity = false;
        var hasHighDensity = false;
        foreach (var rawLine in File.ReadLines(cueFilePath))
        {
            var text = rawLine.TrimStart();
            if (!text.StartsWith("REM", StringComparison.Ordinal))
                continue;

            text = text[3..].TrimStart();
            if (text.StartsWith("SINGLE-DENSITY AREA", StringComparison.Ordinal))
                hasSingleDensity = true;
            else if (text.StartsWith("HIGH-DENSITY AREA", StringComparison.Ordinal))
                hasHighDensity = true;

            if (hasSingleDensity && hasHighDensity)
                return true;
        }

        return false;
    }

    /// <summary>
    ///     Applies MAME's <c>is_gdrom</c> layout fixups to a Redump multi-cue TOC. Each track's
    ///     pregap moves onto the end of the previous track as a split region (those sectors are
    ///     physically stored ahead of its own file) and is excluded from this track's file region,
    ///     then the transition from the single-density to the high-density area pads the last
    ///     single-density track up to LBA 45000 — those pad frames are zero-filled
    ///     (<see cref="CdTrack.PadFrames" />), exactly like chdman.
    /// </summary>
    /// <param name="tracks">The parsed tracks, in track order.</param>
    private static void ApplyGdRomLayout(List<CdTrack> tracks)
    {
        for (var i = 1; i < tracks.Count; i++)
        {
            var track = tracks[i];
            var pregap = track.Pregap;
            if (pregap > 0)
            {
                var previous = tracks[i - 1];
                previous.Frames += pregap;
                previous.SplitFrames += pregap;
                tracks[i - 1] = previous;

                track.FileOffset += (long)pregap * (track.DataSize + track.SubSize);
                track.Frames -= pregap;
                track.Index01 -= pregap;
            }

            track.Pregap = 0;
            track.PgType = 0;
            tracks[i] = track;
        }

        for (var i = 1; i < tracks.Count; i++)
        {
            var track = tracks[i];
            if (track.MulticueArea == HighDensity && tracks[i - 1].MulticueArea == SingleDensity)
            {
                track.PhysicalFrameOffset = HighDensityStartLba;

                var previous = tracks[i - 1];
                var gap = HighDensityStartLba - (previous.Frames + previous.PhysicalFrameOffset);
                previous.Frames += gap;
                previous.PadFrames = gap;
                tracks[i - 1] = previous;
            }
            else
            {
                track.PhysicalFrameOffset = tracks[i - 1].PhysicalFrameOffset + tracks[i - 1].Frames;
            }

            tracks[i] = track;
        }
    }

    private static long GetFileSize(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Couldn't find bin file [{path}]", path);

        return new FileInfo(path).Length;
    }

    /// <summary>
    ///     Parses a CUE sheet track type string (e.g. "MODE1/2048", "AUDIO", "MODE2_RAW") and sets
    ///     the track's <see cref="CdTrack.TrackType" /> and <see cref="CdTrack.DataSize" /> accordingly.
    ///     Matches MAME's <c>parse_track_type</c>.
    /// </summary>
    /// <param name="typeString">The track type token from the CUE sheet.</param>
    /// <param name="track">The track to update.</param>
    /// <exception cref="InvalidDataException">The track type is not recognized.</exception>
    internal static void ParseTrackType(string typeString, ref CdTrack track)
    {
        switch (typeString)
        {
            case "MODE1":
            case "MODE1/2048":
                track.TrackType = CdTrackType.Mode1;
                track.DataSize = 2048;
                break;
            case "MODE1_RAW":
            case "MODE1/2352":
                track.TrackType = CdTrackType.Mode1Raw;
                track.DataSize = 2352;
                break;
            case "MODE2":
            case "MODE2/2336":
                track.TrackType = CdTrackType.Mode2;
                track.DataSize = 2336;
                break;
            case "MODE2_FORM1":
            case "MODE2/2048":
                track.TrackType = CdTrackType.Mode2Form1;
                track.DataSize = 2048;
                break;
            case "MODE2_FORM2":
            case "MODE2/2324":
                track.TrackType = CdTrackType.Mode2Form2;
                track.DataSize = 2324;
                break;
            case "MODE2_FORM_MIX":
                track.TrackType = CdTrackType.Mode2FormMix;
                track.DataSize = 2336;
                break;
            case "MODE2_RAW":
            case "MODE2/2352":
            case "CDI/2352":
                track.TrackType = CdTrackType.Mode2Raw;
                track.DataSize = 2352;
                break;
            case "AUDIO":
                track.TrackType = CdTrackType.Audio;
                track.DataSize = 2352;
                break;
            default:
                throw new InvalidDataException($"Unknown track type [{typeString}]");
        }
    }

    /// <summary>
    ///     Parses a CUE sheet subcode type string ("RW" or "RW_RAW") and sets the track's
    ///     <see cref="CdTrack.SubType" /> and <see cref="CdTrack.SubSize" /> accordingly.
    ///     Matches MAME's <c>parse_subtype</c>.
    /// </summary>
    /// <param name="subTypeString">The subcode type token from the CUE sheet.</param>
    /// <param name="track">The track to update.</param>
    internal static void ParseSubType(string subTypeString, ref CdTrack track)
    {
        switch (subTypeString)
        {
            case "RW":
                track.SubType = CdSubType.Normal;
                track.SubSize = CdConstants.MaxSubcodeData;
                break;
            case "RW_RAW":
                track.SubType = CdSubType.Raw;
                track.SubSize = CdConstants.MaxSubcodeData;
                break;
            default:
                track.SubType = CdSubType.None;
                track.SubSize = 0;
                break;
        }
    }

    /// <summary>
    ///     Validates a .WAV file (PCM, stereo, 44100 Hz, 16-bit) and returns the audio
    ///     data length in bytes and its offset within the file. Matches MAME's
    ///     <c>parse_wav_sample</c>.
    /// </summary>
    private static (long Length, long Offset) ParseWavSample(string fileName)
    {
        using var fs = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read);
        var fileSize = fs.Length;
        long offset = 0;

        if (!string.Equals(ReadFourCc(fs, offset), "RIFF", StringComparison.Ordinal))
            throw new InvalidDataException($"Could not find RIFF header ({fileName})");

        offset += 4;
        ReadU32Le(fs, ref offset);
        if (!string.Equals(ReadFourCc(fs, offset), "WAVE", StringComparison.Ordinal))
            throw new InvalidDataException($"Could not find WAVE header ({fileName})");

        offset += 4;

        // seek until we find a format tag
        long length;
        while (true)
        {
            var tag = ReadFourCc(fs, offset);
            offset += 4;
            length = ReadU32Le(fs, ref offset);
            if (string.Equals(tag, "fmt ", StringComparison.Ordinal))
                break;

            offset += length;
            if (offset >= fileSize)
                throw new InvalidDataException($"Could not find fmt tag ({fileName})");
        }

        // format must be PCM
        if (ReadU16Le(fs, ref offset) != 1)
            throw new InvalidDataException(
                $"Unsupported WAV format - only PCM is supported ({fileName})"
            );
        // only stereo is supported
        if (ReadU16Le(fs, ref offset) != 2)
            throw new InvalidDataException(
                $"Unsupported number of channels - only stereo is supported ({fileName})"
            );
        // sample rate
        if (ReadU32Le(fs, ref offset) != 44100)
            throw new InvalidDataException(
                $"Unsupported samplerate - only 44100 is supported ({fileName})"
            );
        // bytes/second and block alignment are ignored
        offset += 6;
        // bits/sample
        if (ReadU16Le(fs, ref offset) != 16)
            throw new InvalidDataException(
                $"Unsupported bits/sample - only 16 is supported ({fileName})"
            );
        // seek past any extra data
        offset += length - 16;

        // seek until we find a data tag
        while (true)
        {
            var tag = ReadFourCc(fs, offset);
            offset += 4;
            length = ReadU32Le(fs, ref offset);
            if (string.Equals(tag, "data", StringComparison.Ordinal))
                break;

            offset += length;
            if (offset >= fileSize)
                throw new InvalidDataException($"Could not find data tag ({fileName})");
        }

        return (length, offset);
    }

    private static string ReadFourCc(Stream stream, long position)
    {
        stream.Position = position;
        var buffer = new byte[4];
        if (stream.Read(buffer, 0, 4) != 4)
            throw new InvalidDataException("Unexpected end of WAV file");

        return Encoding.ASCII.GetString(buffer);
    }

    private static uint ReadU32Le(Stream stream, ref long offset)
    {
        stream.Position = offset;
        var buffer = new byte[4];
        if (stream.Read(buffer, 0, 4) != 4)
            throw new InvalidDataException("Unexpected end of WAV file");

        offset += 4;
        return buffer[0]
               | ((uint)buffer[1] << 8)
               | ((uint)buffer[2] << 16)
               | ((uint)buffer[3] << 24);
    }

    private static ushort ReadU16Le(Stream stream, ref long offset)
    {
        stream.Position = offset;
        var buffer = new byte[2];
        if (stream.Read(buffer, 0, 2) != 2)
            throw new InvalidDataException("Unexpected end of WAV file");

        offset += 2;
        return (ushort)(buffer[0] | (buffer[1] << 8));
    }

    /// <summary>
    ///     Splits a CUE line into tokens, honoring single and double quotes
    ///     (matching MAME's <c>tokenize</c> helper).
    /// </summary>
    private static List<string> Tokenize(string line)
    {
        return CdImageParser.Tokenize(line);
    }
}