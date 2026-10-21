using CHDSharp.Encoder;

namespace CHDSharpEncoderTest;

/// <summary>Verifies the ISO/GDI/TOC parsers and the extension-based dispatcher.</summary>
public class CdImageParserTests : IDisposable
{
    private readonly string _dir;

    public CdImageParserTests()
    {
        _dir = Path.Combine(
            Path.GetTempPath(),
            "cd_image_parser_tests_" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, true);
        }
        catch
        {
            // ignored
        }
    }

    // ----- ISO -----

    [Fact]
    public void Iso_2048Sectors_IsMode1()
    {
        var path = WriteFile("data.iso", new byte[2048 * 100]);
        var toc = IsoParser.Parse(path);

        Assert.Single(toc.Tracks);
        Assert.Equal(CdTrackType.Mode1, toc.Tracks[0].TrackType);
        Assert.Equal(2048, toc.Tracks[0].DataSize);
        Assert.Equal(100, toc.Tracks[0].Frames);
        Assert.Equal(path, toc.Tracks[0].FileName);
        Assert.False(toc.Tracks[0].Swap);
    }

    [Fact]
    public void Iso_2336Sectors_IsMode2()
    {
        var path = WriteFile("data.iso", new byte[2336 * 50]);
        var toc = IsoParser.Parse(path);

        Assert.Equal(CdTrackType.Mode2, toc.Tracks[0].TrackType);
        Assert.Equal(2336, toc.Tracks[0].DataSize);
        Assert.Equal(50, toc.Tracks[0].Frames);
    }

    [Fact]
    public void Iso_2352Sectors_IsMode2Raw()
    {
        var path = WriteFile("data.iso", new byte[2352 * 40]);
        var toc = IsoParser.Parse(path);

        Assert.Equal(CdTrackType.Mode2Raw, toc.Tracks[0].TrackType);
        Assert.Equal(2352, toc.Tracks[0].DataSize);
        Assert.Equal(40, toc.Tracks[0].Frames);
    }

    [Fact]
    public void Iso_UnrecognizedSize_Throws()
    {
        var path = WriteFile("bad.iso", new byte[1000]);
        Assert.Throws<InvalidDataException>(() => IsoParser.Parse(path));
    }

    // ----- GDI -----

    [Fact]
    public void Gdi_TwoTracks_WithLbaGap_ComputesPadFrames()
    {
        // track 1: 100 frames at LBA 0; track 2 (audio): 50 frames at LBA 200 → 100 pad frames
        WriteFile("track01.bin", new byte[2352 * 100]);
        WriteFile("track02.raw", new byte[2352 * 50]);
        var path = WriteFile(
            "game.gdi",
            """
            2
            1 0 4 2352 "track01.bin" 0
            2 200 0 2352 "track02.raw" 0
            """
        );

        var toc = GdiParser.Parse(path);

        Assert.NotEqual(0u, toc.Flags & CdTocFlags.GdRom);
        Assert.Equal(2, toc.Tracks.Count);
        Assert.Equal(CdTrackType.Mode1Raw, toc.Tracks[0].TrackType);
        Assert.Equal(200, toc.Tracks[0].Frames); // 100 data + 100 pad
        Assert.Equal(100, toc.Tracks[0].PadFrames);
        Assert.Equal(CdTrackType.Audio, toc.Tracks[1].TrackType);
        Assert.Equal(50, toc.Tracks[1].Frames);
        Assert.Equal(0, toc.Tracks[1].PadFrames);
        Assert.True(toc.Tracks[1].Swap, "GDI audio tracks must be byte-swapped");
    }

    [Fact]
    public void Gdi_2048Sectors_IsMode1()
    {
        WriteFile("track01.bin", new byte[2048 * 60]);
        var path = WriteFile(
            "game.gdi",
            """
            1
            1 0 4 2048 "track01.bin" 0
            """
        );

        var toc = GdiParser.Parse(path);

        Assert.Equal(CdTrackType.Mode1, toc.Tracks[0].TrackType);
        Assert.Equal(2048, toc.Tracks[0].DataSize);
        Assert.Equal(60, toc.Tracks[0].Frames);
    }

    [Fact]
    public void Gdi_ThreeTracks_Contiguous()
    {
        WriteFile("track01.bin", new byte[2352 * 300]);
        WriteFile("track02.raw", new byte[2352 * 100]);
        WriteFile("track03.raw", new byte[2352 * 100]);
        var path = WriteFile(
            "game.gdi",
            """
            3
            1 0 4 2352 "track01.bin" 0
            2 300 0 2352 "track02.raw" 0
            3 400 0 2352 "track03.raw" 0
            """
        );

        var toc = GdiParser.Parse(path);

        Assert.Equal(3, toc.Tracks.Count);
        Assert.Equal(300, toc.Tracks[0].Frames);
        Assert.Equal(0, toc.Tracks[0].PadFrames);
        Assert.Equal(100, toc.Tracks[1].Frames);
        Assert.Equal(100, toc.Tracks[2].Frames);
    }

    [Fact]
    public void Gdi_MissingTracks_Throws()
    {
        WriteFile("track01.bin", new byte[2352 * 10]);
        var path = WriteFile(
            "game.gdi",
            """
            2
            1 0 4 2352 "track01.bin" 0
            """
        );

        Assert.Throws<InvalidDataException>(() => GdiParser.Parse(path));
    }

    [Fact]
    public void Gdi_WrongParamCount_Throws()
    {
        var path = WriteFile(
            "game.gdi",
            """
            1
            1 0 4 2352 "track01.bin"
            """
        );

        Assert.Throws<InvalidDataException>(() => GdiParser.Parse(path));
    }

    [Fact]
    public void Gdi_UnknownTrackType_Throws()
    {
        var path = WriteFile(
            "game.gdi",
            """
            1
            1 0 3 2352 "track01.bin" 0
            """
        );

        Assert.Throws<InvalidDataException>(() => GdiParser.Parse(path));
    }

    [Fact]
    public void Gdi_MissingDataFile_Throws()
    {
        var path = WriteFile(
            "game.gdi",
            """
            1
            1 0 4 2352 "nope.bin" 0
            """
        );

        Assert.Throws<FileNotFoundException>(() => GdiParser.Parse(path));
    }

    // ----- TOC -----

    [Fact]
    public void Toc_DataTrack_WithMsfLength()
    {
        WriteFile("data.bin", new byte[2048 * 120]);
        var path = WriteFile(
            "disc.toc",
            """
            TRACK MODE1
            DATAFILE "data.bin" 0 01:36:00
            """
        );

        var toc = TocParser.Parse(path);

        Assert.Single(toc.Tracks);
        Assert.Equal(CdTrackType.Mode1, toc.Tracks[0].TrackType);
        Assert.Equal(2048, toc.Tracks[0].DataSize);
        Assert.Equal(7200, toc.Tracks[0].Frames); // 01:36:00 = 96 s = 7200 frames
        Assert.Equal(Path.Combine(_dir, "data.bin"), toc.Tracks[0].FileName);
        Assert.Equal(0L, toc.Tracks[0].FileOffset);
    }

    [Fact]
    public void Toc_AudioTrack_WithStartPregap()
    {
        WriteFile("audio.wav", new byte[2352 * 50]);
        var path = WriteFile(
            "disc.toc",
            """
            TRACK AUDIO
            AUDIOFILE "audio.wav" 0 00:00:50
            START 00:02:00
            """
        );

        var toc = TocParser.Parse(path);

        Assert.Single(toc.Tracks);
        Assert.Equal(CdTrackType.Audio, toc.Tracks[0].TrackType);
        Assert.Equal(50, toc.Tracks[0].Frames);
        Assert.Equal(150, toc.Tracks[0].Pregap);
        Assert.False(toc.Tracks[0].Swap, "TOC files must not be byte-swapped by default");
    }

    [Fact]
    public void Toc_BareStart_MeansZeroPregap()
    {
        WriteFile("audio.wav", new byte[2352 * 50]);
        var path = WriteFile(
            "disc.toc",
            """
            TRACK AUDIO
            AUDIOFILE "audio.wav" 0 00:00:50
            START
            """
        );

        var toc = TocParser.Parse(path);

        Assert.Single(toc.Tracks);
        Assert.Equal(0, toc.Tracks[0].Pregap);
    }

    [Fact]
    public void Toc_SwapFlag_AndDecimalOffset()
    {
        WriteFile("data.bin", new byte[2352 * 60]);
        var path = WriteFile(
            "disc.toc",
            """
            TRACK AUDIO
            FILE "data.bin" SWAP #2352 00:00:59
            """
        );

        var toc = TocParser.Parse(path);

        Assert.True(toc.Tracks[0].Swap);
        Assert.Equal(2352L, toc.Tracks[0].FileOffset);
        Assert.Equal(59, toc.Tracks[0].Frames);
    }

    [Fact]
    public void Toc_OffsetAndLength_AreDistinguished()
    {
        WriteFile("data.bin", new byte[2352 * 100]);
        var path = WriteFile(
            "disc.toc",
            """
            TRACK AUDIO
            AUDIOFILE "data.bin" 00:00:10 00:00:50
            """
        );

        var toc = TocParser.Parse(path);

        // offset 10 frames in bytes + length 50 frames
        Assert.Equal(10L * 2352, toc.Tracks[0].FileOffset);
        Assert.Equal(50, toc.Tracks[0].Frames);
    }

    [Fact]
    public void Toc_UnknownTrackType_Throws()
    {
        var path = WriteFile(
            "disc.toc",
            """
            TRACK MYSTERY
            DATAFILE "data.bin" 0 00:01:00
            """
        );

        Assert.Throws<InvalidDataException>(() => TocParser.Parse(path));
    }

    // ----- dispatcher -----

    [Fact]
    public void Dispatcher_Cue_RoutesToCueParser()
    {
        var cue = WriteFile(
            "disc.cue",
            "FILE \"game.bin\" BINARY\n  TRACK 01 MODE1/2352\n    INDEX 01 00:00:00\n"
        );
        WriteFile("game.bin", new byte[2352 * 20]);

        var toc = CdImageParser.Parse(cue);

        Assert.Equal(0u, toc.Flags);
        Assert.Equal(CdTrackType.Mode1Raw, toc.Tracks[0].TrackType);
        Assert.Equal(20, toc.Tracks[0].Frames);
    }

    [Fact]
    public void Cue_GdiCue_AppliesHighDensityAreaLayout()
    {
        WriteFile("track01.bin", new byte[2352 * 450]);
        WriteFile("track02.bin", new byte[2352 * 676]);
        WriteFile("track03.bin", new byte[2352 * 100]);
        var cue = WriteFile(
            "disc.cue",
            """
            REM SINGLE-DENSITY AREA
            FILE "track01.bin" BINARY
              TRACK 01 MODE1/2352
                INDEX 01 00:00:00
            FILE "track02.bin" BINARY
              TRACK 02 AUDIO
                INDEX 00 00:00:00
                INDEX 01 00:02:00
            REM HIGH-DENSITY AREA
            FILE "track03.bin" BINARY
              TRACK 03 MODE1/2352
                INDEX 01 00:00:00
            """
        );

        var toc = CueParser.Parse(cue);

        Assert.NotEqual(0u, toc.Flags & CdTocFlags.GdRom);
        // track 2's 150-frame pregap moves onto track 1 as a split region and out of track 2's file
        Assert.Equal(600, toc.Tracks[0].Frames);
        Assert.Equal(150, toc.Tracks[0].SplitFrames);
        Assert.Equal(150L * 2352, toc.Tracks[1].FileOffset);
        Assert.Equal(0, toc.Tracks[1].Pregap);
        // the high-density transition pads track 2 up to LBA 45000 (zero-filled pad frames)
        Assert.Equal(600, toc.Tracks[1].PhysicalFrameOffset);
        Assert.Equal(43874, toc.Tracks[1].PadFrames);
        Assert.Equal(44400, toc.Tracks[1].Frames);
        Assert.Equal(45000, toc.Tracks[2].PhysicalFrameOffset);
    }

    // ----- multisession cues (REM SESSION / LEAD-IN / LEAD-OUT) -----

    [Fact]
    public void Cue_SingleBinMultisession_TrimsAndPadsInterSessionData()
    {
        WriteFile("disc.bin", new byte[2352 * 550]);
        var cue = WriteFile(
            "disc.cue",
            """
            REM SESSION 01
            FILE "disc.bin" BINARY
              TRACK 01 MODE2/2352
                INDEX 01 00:00:00
            REM LEAD-OUT 00:01:25
            REM SESSION 02
              TRACK 02 MODE2/2352
                INDEX 01 00:02:00
            """
        );

        var toc = CueParser.Parse(cue);

        Assert.NotEqual(0u, toc.Flags & CdTocFlags.Multisession);
        // the absolute lead-out (100 frames) trims track 1 from 150 to 100 frames and becomes
        // the 50-frame duration to the next track; that gap is zero-filled on track 1 and
        // skipped in the file for track 2
        Assert.Equal(0, toc.Tracks[0].Session);
        Assert.Equal(100, toc.Tracks[0].Frames);
        Assert.Equal(50, toc.Tracks[0].PadFrames);
        Assert.Equal(50, toc.Tracks[0].LeadOut);
        Assert.Equal(1, toc.Tracks[1].Session);
        Assert.Equal(400, toc.Tracks[1].Frames);
        Assert.Equal(150L * 2352, toc.Tracks[1].FileOffset);
    }

    [Fact]
    public void Cue_SingleBinMultisession_ImgFileKeepsDummyData()
    {
        WriteFile("disc.img", new byte[2352 * 550]);
        var cue = WriteFile(
            "disc.cue",
            """
            REM SESSION 01
            FILE "disc.img" BINARY
              TRACK 01 MODE2/2352
                INDEX 01 00:00:00
            REM LEAD-OUT 00:01:25
            REM SESSION 02
              TRACK 02 MODE2/2352
                INDEX 01 00:02:00
            """
        );

        var toc = CueParser.Parse(cue);

        // DiscImageCreator .img images carry no dummy data: the lead-out still trims track 1,
        // but the gap is not converted into pad frames and track 2 keeps reading it
        Assert.Equal(100, toc.Tracks[0].Frames);
        Assert.Equal(0, toc.Tracks[0].PadFrames);
        Assert.Equal(450, toc.Tracks[1].Frames);
        Assert.Equal(100L * 2352, toc.Tracks[1].FileOffset);
    }

    [Fact]
    public void Cue_MultiBinMultisession_SynthesizesLeadInAndLeadOut()
    {
        WriteFile("t1.bin", new byte[2352 * 300]);
        WriteFile("t2.bin", new byte[2352 * 300]);
        var cue = WriteFile(
            "disc.cue",
            """
            REM SESSION 01
            FILE "t1.bin" BINARY
              TRACK 01 MODE1/2352
                INDEX 01 00:00:00
            REM SESSION 02
            FILE "t2.bin" BINARY
              TRACK 02 MODE1/2352
                INDEX 01 00:00:00
            """
        );

        var toc = CueParser.Parse(cue);

        Assert.NotEqual(0u, toc.Flags & CdTocFlags.Multisession);
        // standard values are synthesized: 1m30s lead-out for the first session, 1m00s lead-in
        // for the new session's first track
        Assert.Equal(6750, toc.Tracks[0].LeadOut);
        Assert.Equal(4500, toc.Tracks[1].LeadIn);
    }

    [Fact]
    public void Cue_MultiBinMultisession_SessionPregapFeedsLeadIn()
    {
        WriteFile("t1.bin", new byte[2352 * 300]);
        WriteFile("t2.bin", new byte[2352 * 300]);
        var cue = WriteFile(
            "disc.cue",
            """
            REM SESSION 01
            FILE "t1.bin" BINARY
              TRACK 01 MODE1/2352
                INDEX 01 00:00:00
            REM SESSION 02
            REM PREGAP 00:01:00
            FILE "t2.bin" BINARY
              TRACK 02 MODE1/2352
                INDEX 01 00:00:00
            """
        );

        var toc = CueParser.Parse(cue);

        // the session pregap becomes the new session's lead-in, so the standard 4500-frame
        // lead-in is not synthesized
        Assert.Equal(75, toc.Tracks[1].LeadIn);
        Assert.Equal(6750, toc.Tracks[0].LeadOut);
    }

    [Fact]
    public void Cue_MultiBinThreeSessions_LaterSessionsUseShorterLeadOut()
    {
        WriteFile("t1.bin", new byte[2352 * 300]);
        WriteFile("t2.bin", new byte[2352 * 300]);
        WriteFile("t3.bin", new byte[2352 * 300]);
        var cue = WriteFile(
            "disc.cue",
            """
            REM SESSION 01
            FILE "t1.bin" BINARY
              TRACK 01 MODE1/2352
                INDEX 01 00:00:00
            REM SESSION 02
            FILE "t2.bin" BINARY
              TRACK 02 MODE1/2352
                INDEX 01 00:00:00
            REM SESSION 03
            FILE "t3.bin" BINARY
              TRACK 03 MODE1/2352
                INDEX 01 00:00:00
            """
        );

        var toc = CueParser.Parse(cue);

        // sessions after the first use the shorter 30s lead-out
        Assert.Equal(6750, toc.Tracks[0].LeadOut);
        Assert.Equal(2250, toc.Tracks[1].LeadOut);
        Assert.Equal(-1, toc.Tracks[2].LeadOut);
        Assert.Equal(4500, toc.Tracks[1].LeadIn);
        Assert.Equal(4500, toc.Tracks[2].LeadIn);
    }

    [Fact]
    public void Cue_WithoutSessions_LeavesLeadValuesUnset()
    {
        WriteFile("game.bin", new byte[2352 * 20]);
        var cue = WriteFile(
            "disc.cue",
            "FILE \"game.bin\" BINARY\n  TRACK 01 MODE1/2352\n    INDEX 01 00:00:00\n"
        );

        var toc = CueParser.Parse(cue);

        Assert.Equal(0u, toc.Flags & CdTocFlags.Multisession);
        Assert.Equal(-1, toc.Tracks[0].LeadIn);
        Assert.Equal(-1, toc.Tracks[0].LeadOut);
    }

    [Fact]
    public void Cue_MultisessionLeadOutBeforeTheTrack_MatchesMameInsteadOfGoingNegative()
    {
        WriteFile("disc.bin", new byte[2352 * 550]);
        var cue = WriteFile(
            "disc.cue",
            """
            REM SESSION 01
            FILE "disc.bin" BINARY
              TRACK 01 MODE2/2352
                INDEX 01 00:00:00
            REM SESSION 02
              TRACK 02 MODE2/2352
                INDEX 01 00:02:00
            REM LEAD-OUT 00:00:20
              TRACK 03 MODE2/2352
                INDEX 01 00:04:00
            """
        );

        // The lead-out (20 frames) lies before track 2's INDEX 00 (150 frames). chdman compares
        // its uint32_t frame count against the signed end frame, so the negative end frame wraps
        // to a huge value and the trim is skipped; the absolute lead-out then becomes the 20-frame
        // gap before track 3. The parser must produce that same layout instead of a negative
        // length (verified byte-identical to chdman createcd).
        var toc = CueParser.Parse(cue);

        Assert.Equal(150, toc.Tracks[0].Frames);
        Assert.Equal(150, toc.Tracks[1].Frames);
        Assert.Equal(20, toc.Tracks[1].LeadOut);
        Assert.Equal(20, toc.Tracks[1].PadFrames);
        Assert.Equal(230, toc.Tracks[2].Frames);
        Assert.Equal(320L * 2352, toc.Tracks[2].FileOffset);
    }

    [Fact]
    public void Cue_MultisessionZeroLeadOut_IsIgnoredLikeMame()
    {
        WriteFile("disc.bin", new byte[2352 * 20000]);
        var cue = WriteFile(
            "disc.cue",
            """
            REM SESSION 01
            FILE "disc.bin" BINARY
              TRACK 01 AUDIO
                INDEX 01 00:00:00
              TRACK 02 AUDIO
                INDEX 01 02:32:00
            REM LEAD-OUT 00:00:00
            REM SESSION 02
              TRACK 03 AUDIO
                INDEX 01 04:00:00
            """
        );

        // A zero lead-out used to trim track 2 to -11400 frames. chdman's unsigned comparison
        // skips the trim and keeps the absolute zero as a no-op gap, so the cue's indexes describe
        // the disc (verified byte-identical to chdman createcd).
        var toc = CueParser.Parse(cue);

        Assert.Equal(11400, toc.Tracks[0].Frames);
        Assert.Equal(6600, toc.Tracks[1].Frames);
        Assert.Equal(0, toc.Tracks[1].PadFrames);
        Assert.Equal(2000, toc.Tracks[2].Frames);
    }

    [Fact]
    public void Dispatcher_Gdi_RoutesToGdiParser()
    {
        WriteFile("track01.bin", new byte[2352 * 20]);
        var gdi = WriteFile("disc.gdi", "1\n1 0 4 2352 \"track01.bin\" 0\n");

        var toc = CdImageParser.Parse(gdi);

        Assert.NotEqual(0u, toc.Flags & CdTocFlags.GdRom);
        Assert.Equal(20, toc.Tracks[0].Frames);
    }

    [Theory]
    [InlineData("disc.iso")]
    [InlineData("disc.cdr")]
    [InlineData("disc.toast")]
    public void Dispatcher_IsoExtensions_RouteToIsoParser(string fileName)
    {
        var iso = WriteFile(fileName, new byte[2048 * 20]);

        var toc = CdImageParser.Parse(iso);

        Assert.Equal(0u, toc.Flags);
        Assert.Equal(CdTrackType.Mode1, toc.Tracks[0].TrackType);
        Assert.Equal(20, toc.Tracks[0].Frames);
    }

    [Fact]
    public void Dispatcher_Toc_RoutesToTocParser()
    {
        WriteFile("data.bin", new byte[2048 * 20]);
        var tocFile = WriteFile("disc.toc", "TRACK MODE1\nDATAFILE \"data.bin\" 0 00:01:00\n");

        var toc = CdImageParser.Parse(tocFile);

        Assert.Equal(0u, toc.Flags);
        Assert.Equal(CdTrackType.Mode1, toc.Tracks[0].TrackType);
        Assert.Equal(75, toc.Tracks[0].Frames); // 00:01:00 = 75 frames
    }

    [Fact]
    public void Dispatcher_UnknownExtension_RoutesToTocParser()
    {
        WriteFile("data.bin", new byte[2048 * 20]);
        var tocFile = WriteFile("disc.bin.cue2", "TRACK MODE1\nDATAFILE \"data.bin\" 0 00:01:00\n");

        var toc = CdImageParser.Parse(tocFile);

        Assert.Equal(CdTrackType.Mode1, toc.Tracks[0].TrackType);
        Assert.Equal(75, toc.Tracks[0].Frames);
    }

    // ----- helpers -----

    private string WriteFile(string name, string content)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private string WriteFile(string name, byte[] content)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, content);
        return path;
    }
}