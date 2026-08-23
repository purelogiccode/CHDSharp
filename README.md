[![.NET](https://img.shields.io/badge/.NET-8.0_|_9.0_|_10.0-blueviolet)](https://dotnet.microsoft.com/)
[![NuGet](https://img.shields.io/nuget/v/CHDSharp?color=blue)](https://www.nuget.org/packages/CHDSharp/)
[![License](https://img.shields.io/badge/license-MIT-green)](LICENSE)
[![Tests](https://img.shields.io/badge/tests-xUnit-brightgreen)](#tests)

# CHDSharp

**Pure C# CHD (Compressed Hunks of Data) reader and writer** — the disk-image format used by [MAME](https://www.mamedev.org/) for arcade hard disks, CD/GD-ROMs, DVDs, and laserdisc A/V content.

Supports every CHD format version (V1–V5), all 10 compression codecs, parent/child differential chains, parallel verification, metadata, extraction, and CHD creation — with **zero native dependencies** and a **100% byte-for-byte match with MAME `chdman`**.

> Fork of [RomVault/CHDSharp](https://github.com/RomVault/CHDSharp) by [Gordon Jefferyes](https://github.com/gjefferyes) — extended with Zstd, AVHuff, V5 compressed maps, random access, async APIs, parent/child chaining, parallel verification, seekable stream, span reads, read-ahead decompression, lazy parent resolution, encoding capabilities, and a comprehensive test suite.

---

## Installation

```bash
dotnet add package CHDSharp
```

Targets `net8.0`, `net9.0`, and `net10.0`. Zero native dependencies — every codec (zlib, lzma, huffman, flac, zstd, AVHuff) is implemented in pure C#.

---

## Quick Start

### Reading CHDs

```csharp
using CHDSharp;
using CHDSharp.Models;

// Quick check: is this a valid CHD?
if (Chd.IsChdFile("game.chd", out uint version))
    Console.WriteLine($"Detected CHD version {version}");

// Read the header without opening the full file
var headerResult = Chd.ReadHeader("game.chd");
if (headerResult.IsSuccess)
{
    Console.WriteLine($"Version: {headerResult.Header.Version}");
    Console.WriteLine($"Hunk size: {headerResult.Header.HunkBytes} bytes");
    Console.WriteLine($"Total size: {headerResult.Header.TotalBytes} bytes");
    Console.WriteLine($"SHA-1: {headerResult.Header.Sha1Hex}");
}

// Full verification (parallel, deep decompress every hunk)
using var stream = File.OpenRead("game.chd");
var result = Chd.CheckFile(stream, "game.chd", deepCheck: true);
Console.WriteLine(result.IsSuccess
    ? $"V{result.Version}  SHA1: {result.Sha1Hex}"
    : $"Error: {result.Error.GetMessage()}");
```

### Random Access

```csharp
var err = ChdFile.Open("game.chd", out var chd);
using (chd)
{
    // Inspect metadata (game name, disc label, etc.)
    foreach (var meta in chd.Metadata)
        Console.WriteLine(meta);  // e.g. "GAME: gauntlet"

    // Parse CD/GD-ROM track layout (TOC)
    if (chd.Tracks is { } tracks)
        foreach (var track in tracks)
            Console.WriteLine($"Track {track.TrackNumber}: {track.GetTypeString()}");

    // Read hunk #42
    var hunk = new byte[chd.HunkBytes];
    chd.ReadHunk(42, hunk);

    // Read arbitrary byte range (crosses hunk boundaries automatically)
    var buf = new byte[1024];
    chd.Read(offset: 0x10000, buf, 0, buf.Length);

    // Decompress the entire image at once
    chd.ReadAllBytes(out var image);

    // Zero-copy Span<byte> reads
    Span<byte> span = stackalloc byte[512];
    chd.ReadHunk(0, span);
    chd.Read(0x1000, span, span.Length);
}
```

### Seekable Stream

```csharp
var err = ChdFile.Open("game.chd", out var chd);
using (chd)
{
    using var stream = chd.OpenAsStream();
    stream.Seek(0x10000, SeekOrigin.Current);
    var buffer = new byte[4096];
    stream.Read(buffer, 0, buffer.Length);
}
```

### Async API

```csharp
var (_, chd) = await ChdFile.OpenAsync("game.chd");
await using (chd)
{
    var hunk = new byte[chd.HunkBytes];
    await chd.ReadHunkAsync(0, hunk);

    var buf = new byte[1024];
    await chd.ReadAsync(0x10000, buf, 0, buf.Length);
}
```

### Parent/Child CHDs

```csharp
// Verify a child CHD against its parent
var result = Chd.CheckFileWithParent("child.chd", "parent.chd");

// Open with parent
var err = ChdFile.Open("child.chd", out var child, parentPath: "parent.chd");
using (child)
{
    child.ReadAllBytes(out var image);
}

// Lazy parent resolution (resolve by hash on first read)
var (_, lazyChd) = ChdFile.Open("child.chd", out var chd2,
    parentResolver: hash => FindParentByHash(hash));
```

### CD Sector Reads

```csharp
var err = ChdFile.Open("game.chd", out var chd);
using (chd)
{
    // Read by LBA (Logical Block Address)
    var sector = new byte[2352]; // raw CD sector
    chd.ReadSector(0, sector);

    // Read by MSF (Minute:Second:Frame, BCD format)
    chd.ReadSectorMsf(0x000200, sector); // 00:02:00

    // Read full 2448-byte frame (2352 data + 96 subchannel)
    var frame = new byte[2448];
    chd.ReadFrame(0, frame);

    // Convert between MSF and LBA
    int lba = CdRomAddress.MsfToLba(0x000200);
    uint msf = CdRomAddress.LbaToMsf(lba);
}
```

### Progress Reporting & Cancellation

```csharp
using var cts = new CancellationTokenSource();

var progress = new Progress<ChdProgress>(p =>
    Console.WriteLine($"Verified {p.HunksProcessed}/{p.TotalHunks} hunks"));

var result = Chd.CheckFile("game.chd", deepCheck: true,
    progress: progress, cancellationToken: cts.Token);
```

---

## Writing CHDs (Encoding)

The encoder is part of the same library — no separate package needed.

### Raw Binary → CHD

```csharp
using CHDSharp.Encoder;

// Simplest form — default codec (zlib), auto hunk/unit sizes
ChdEncoder.EncodeRaw("game.bin", "game.chd");

// Custom codecs (tried per hunk; smallest output wins)
ChdEncoder.EncodeRaw("game.bin", "game.chd",
    codecTags: ChdCodecs.ParseCodecTags("zlib,zstd,lzma"));

// Custom hunk/unit sizes
ChdEncoder.EncodeRaw("game.bin", "game.chd",
    hunkBytes: 65536, unitBytes: 4096);

// Uncompressed CHD (-c none)
ChdEncoder.EncodeRaw("game.bin", "game.chd",
    codecTags: [CodecTags.None]);
```

### CD Image → CHD

```csharp
// From CUE sheet
ChdEncoder.EncodeCd("game.cue", "game.chd");

// From GDI, ISO, TOC, or NRG
ChdEncoder.EncodeCd("game.gdi", "game.chd");
ChdEncoder.EncodeCd("game.iso", "game.chd");
```

### Blank HD CHD

```csharp
// Zero-filled CHD with auto-derived CHS geometry
ChdEncoder.CreateBlank("blank.chd", 100 * 1024 * 1024UL); // 100 MB

// Explicit CHS geometry
ChdEncoder.CreateBlankWithChs("blank.chd",
    cylinders: 1024, heads: 16, sectors: 63, sectorSize: 512);
```

### Re-compress Existing CHD

```csharp
// Re-compress with Zstd
ChdEncoder.Copy("old.chd", "new.chd",
    codecTags: [CodecTags.Zstd]);

// Preserve legacy metadata (no upgrade)
ChdEncoder.Copy("old.chd", "new.chd",
    codecTags: [CodecTags.Zstd],
    options: new ChdEncodeOptions { NoMetadataUpgrade = true });
```

### Delta (Parent) CHD

```csharp
// Create a differential child against a parent
ChdEncoder.EncodeRaw("game.bin", "game.chd",
    options: new ChdEncodeOptions { ParentPath = "base.chd" });
```

### Progress Reporting During Encoding

```csharp
var options = new ChdEncodeOptions
{
    TaskCount = 8,
    HunkCompleted = p => Console.WriteLine(
        $"hunk {p.HunkIndex,6}/{p.HunkCount}  {p.CodecName,-5} " +
        $"{p.RawBytes,8} -> {p.StoredBytes,8} B  ({p.Ratio:P1})")
};

ChdEncoder.EncodeRaw("game.bin", "game.chd", options: options);
```

### CLI Encoding

```bash
# Raw binary -> CHD
CHDSharp createraw -o out.chd -i in.bin [-c zlib,zstd,lzma,none] [-hs 65536] [-us 4096] [-np 8] [-op parent.chd] [-v]

# CD image -> CHD (CUE/GDI/ISO/TOC/NRG)
CHDSharp createcd -o out.chd -i in.cue [-c zlib,zstd,lzma,none] [-np 8] [-op parent.chd] [-v]

# Blank HD CHD
CHDSharp createhd -o out.chd --size 104857600 [-chs 1024,16,63] [-ss 512] [-c zlib] [-v]

# DVD CHD
CHDSharp createdvd -o out.chd -i in.iso [-c lzma,zlib,huff,flac] [-v]

# Re-compress an existing CHD
CHDSharp copy -o out.chd -i in.chd [-c zlib,zstd,lzma,none] [-np 8] [-ip parent.chd] [-op parent.chd] [-v]
```

---

## Features

### Reading

- **Any CHD, any version** — V1–V5 headers, every internal map format (self-hunk dedup, CRC32 maps, CRC16/compressed/RLE maps, uncompressed V5 maps, unit-based parent references)
- **All 10 codecs** — zlib, lzma, huffman, flac, zstd, AVHuff, plus the four CD-aware variants (`cdzl`, `cdlz`, `cdfl`, `cdzs`) with ECC/sync regeneration
- **Random access** — `ReadHunk()`, `Read()` (byte ranges across hunk boundaries), `EnumerateHunks()`, `ReadAllBytes()`
- **LBA/MSF sector reads** — `ReadSector()`, `ReadSectorMsf()`, and `ReadFrame()` address CD/GD-ROM sectors or full 2448-byte frames by logical block address
- **Async API** — `OpenAsync`, `ReadHunkAsync`, `ReadAsync`, `IAsyncDisposable`
- **Seekable stream** — `OpenAsStream()` returns a read-only, seekable `Stream` over the decompressed image
- **Span\<byte\> reads** — `ReadHunk(uint, Span<byte>)` and `Read(ulong, Span<byte>, int)` for zero-copy paths
- **Read-ahead decompression** — `ConfigureReadAhead(int)` enables background pre-decompression of upcoming hunks
- **Lazy parent resolution** — `ParentResolver` callback resolves parents by SHA1/MD5 hash on first read
- **Header DTO without opening** — `Chd.ReadHeader()` returns the full parsed header without keeping the file open
- **Parallel verification** — multi-threaded `CheckFile()` with bounded memory and configurable worker count
- **Parent/child chains** — transparent differential CHD support with wrong-parent detection
- **Metadata** — tag/index query API (`GetMetadata`) plus the full entry list; IDNT (ATA IDENTIFY), KEY (encryption), CIS (PCMCIA) metadata support
- **Track info & extraction** — CD/GD-ROM TOC parsing (`ChdTrackInfo`), CUE/GDI descriptor generation, whole-image extraction (`.bin`/`.cue`, `.iso`, `.img`, `.raw`, `.gdi`)
- **Platform/game detection** — 11 systems (PS1, Saturn, Dreamcast, etc.)
- **Pluggable logging** — `Microsoft.Extensions.Logging` integration, silent by default

### Writing

- **Raw binary → CHD** — `ChdEncoder.EncodeRaw()` with auto or custom hunk/unit sizes
- **CD image → CHD** — `ChdEncoder.EncodeCd()` from CUE, GDI, ISO, TOC, or NRG
- **Blank HD CHD** — `ChdEncoder.CreateBlank()` / `CreateBlankWithChs()` for zero-filled images
- **Re-compression** — `ChdEncoder.Copy()` re-compresses existing CHDs with new codecs
- **Delta children** — create differential CHDs against a parent (`-ip`)
- **All 10 codecs** — zlib, zstd, lzma, huff, flac, cdzl, cdlz, cdzs, cdfl, none; best-per-hunk selection
- **SELF deduplication** — COMPRESSION_SELF with SELF_0/SELF_1 map promotion
- **Metadata cloning** — all source entries preserved during copy
- **Metadata upgrade** — legacy CHCD/CHTR/CHGT → modern CHT2/CHGD during copy (matching chdman)
- **Parallel encoding** — 1–64 workers, deterministic output regardless of worker count
- **100% chdman match** — byte-identical output vs `chdman` for all codecs

---

## Support Matrix

### Format Versions

| Version | Header | Map | Status |
|---------|--------|-----|--------|
| V1 | 76 bytes | Self-hunk dedup | ✅ |
| V2 | 80 bytes | Self-hunk dedup | ✅ |
| V3 | 120 bytes | CRC32 map, self-hunk | ✅ |
| V4 | 108 bytes | CRC32 map, parent chain | ✅ |
| V5 | 124 bytes | CRC16 / compressed / RLE, parent/unit chain | ✅ |

### Codecs

| Codec | FourCC | CD Variant | Read | Write |
|-------|--------|------------|:----:|:-----:|
| Zlib (Deflate) | `zlib` | `cdzl` | ✅ | ✅ |
| LZMA | `lzma` | `cdlz` | ✅ | ✅ |
| Huffman | `huff` | — | ✅ | ✅ |
| FLAC | `flac` | `cdfl` | ✅ | ✅ |
| Zstd | `zstd` | `cdzs` | ✅ | ✅ |
| AVHuff | `avhu` | — | ✅ | ✅ |

---

## vs libchdr

| Feature | libchdr 0.3.0 (C) | CHDSharp (C#) |
|---------|:---:|:---:|
| V1–V5 headers | ✅ | ✅ |
| 9 of 10 codecs | ✅ | ✅ |
| AVHuff (`avhu`) | ❌ | ✅ |
| Parent/child chains | ✅ | ✅ |
| Random access | ✅ | ✅ |
| Byte-range reads | ❌ (hunk-only) | ✅ |
| LBA/MSF sector reads | ❌ | ✅ |
| Full-image verification | ❌ | ✅ parallel |
| Metadata read/write | ✅ / ❌ | ✅ / ✅ |
| Extraction | ❌ | ✅ |
| Async API | ❌ | ✅ |
| CHD creation | ❌ | ✅ |
| Native dependencies | zlib, LZMA SDK, zstd, dr_flac | **none** |

See [docs/libchdr-comparison.md](docs/libchdr-comparison.md) for the full parity analysis.

---

## Library Comparison

CHDSharp vs the two other independent CHD implementations, MAME's reference `chdman` (0.288), and libchdr 0.3.0.

| Capability | CHDSharp | chd-rs 0.3.4 (Rust) | CHDlite 0.2.1 (C++) | `chdman` (MAME) | libchdr 0.3.0 (C) |
|---|:---:|:---:|:---:|:---:|:---:|
| **Reading** | | | | | |
| Read V1–V5 | ✅ | ✅ | 🟡 V3–V5 | ✅ | ✅ |
| All 10 codecs (decode) | ✅ | ✅ | ✅ | ✅ | 🟡 9 of 10 |
| Parent/child chains | ✅ | ✅ | ✅ | ✅ | ✅ |
| Per-hunk CRC verification | ✅ | 🟡 opt-in | ✅ | ✅ | 🟡 |
| Full-image verify | ✅ parallel | 🟡 raw SHA1 | ✅ | ✅ | ❌ |
| Track/TOC parsing | ✅ | 🟡 | ✅ | ✅ | ❌ |
| **Writing** | | | | | |
| Write V5 | ✅ | ❌ | ✅ | ✅ | ❌ |
| All 10 codecs (encode) | ✅ | ❌ | ✅ | ✅ | ❌ |
| Delta/parent CHD | ✅ | ❌ | ✅ | ✅ | ❌ |
| CHD→CHD copy | ✅ | ❌ | ✅ | ✅ | ❌ |
| **API** | | | | | |
| Byte-range reads | ✅ | ✅ | ✅ | — | ❌ |
| LBA/MSF sector reads | ✅ | ❌ | ✅ | — | ❌ |
| Thread-safe random access | ✅ | ❌ | ❌ | — | ❌ |
| Async I/O | ✅ | ❌ | 🟡 | — | ❌ |
| Parallel verification | ✅ | ❌ | ❌ | ❌ | ❌ |
| Parallel encoding | ✅ | ❌ | ✅ | ✅ | ❌ |
| **Extras** | | | | | |
| Extraction | ✅ | 🟡 | ✅ | ✅ | ❌ |
| Platform detection | ✅ | ❌ | ✅ | ❌ | ❌ |
| Native dependencies | **none** | none | zlib-ng/zstd/lzma/flac | zlib/lzma/flac | zlib/LZMA/zstd/flac |

---

## Logging

By default the library is silent. Set `Chd.LoggerFactory` before any other call to enable logging:

```csharp
using Serilog;
using Serilog.Extensions.Logging;

Chd.LoggerFactory = new SerilogLoggerFactory(
    new LoggerConfiguration()
        .MinimumLevel.Debug()
        .WriteTo.Console()
        .CreateLogger());
```

---

## CLI

The CLI tool is named `CHDSharp` (e.g. `CHDSharp.exe` on Windows) and accepts the same subcommands as MAME's `chdman`:

```bash
# Display CHD information
CHDSharp info -i game.chd

# Verify a CHD
CHDSharp verify -i game.chd

# Create CHDs
CHDSharp createraw -o out.chd -i in.bin [-c zlib,zstd,lzma,none] [-hs 65536] [-us 4096] [-np 8] [-op parent.chd] [-v]
CHDSharp createcd -o out.chd -i in.cue [-c zlib,zstd,lzma,none] [-np 8] [-op parent.chd] [-v]
CHDSharp createhd -o out.chd --size N [-c zlib,zstd,lzma,none] [-chs C,H,S] [-ss N] [-np 8] [-v]
CHDSharp createdvd -o out.chd -i in.iso [-c lzma,zlib,huff,flac] [-v]
CHDSharp createld -o out.chd -i in.avi [-c avhu] [-v]

# Extract CHDs
CHDSharp extractraw -o out.bin -i in.chd
CHDSharp extractcd -o out.cue -i in.chd
CHDSharp extractdvd -o out.iso -i in.chd
CHDSharp extractld -o out.avi -i in.chd

# Re-compress
CHDSharp copy -o out.chd -i in.chd [-c zstd] [-ip parent.chd] [-op parent.chd]

# Metadata
CHDSharp addmeta -i game.chd -t GAME -vt "gauntlet"
CHDSharp delmeta -i game.chd -t GAME
CHDSharp dumpmeta -i game.chd -t GAME

# List hard disk templates
CHDSharp listtemplates

# Help
CHDSharp help
CHDSharp help createcd
```

Additional convenience commands (CHDSharp extensions):

```bash
# Verify all .chd files in directories (recursive)
CHDSharp D:\CHD

# Verify paths from a text file
CHDSharp --list chd_paths.txt

# Random-access self-test on a single CHD
CHDSharp --random game.chd

# Verify a child CHD against its parent
CHDSharp --parent child.chd parent.chd

# Print CD/GD-ROM table of contents
CHDSharp --toc game.chd

# Generate CUE sheet for CD CHDs
CHDSharp --cue game.chd

# Classify CHD media type
CHDSharp --classify game.chd

# Detect game platform
CHDSharp --detect game.chd

# Compute content hashes
CHDSharp --hash game.chd --hashes sha1,sha256,crc32 --result json

# Batch extract/create
CHDSharp --batch input-dir output-dir --action extract
```

---

## Documentation

The full wiki lives in [`docs/`](docs/README.md):

| Page | Description |
|------|-------------|
| [Getting Started](docs/getting-started.md) | Install, first program, CLI tour |
| [CHD Format Reference](docs/chd-format.md) | On-disk format: headers, maps, metadata, hashing |
| [Codecs](docs/codecs.md) | All 10 decompression codecs |
| [Architecture](docs/architecture.md) | Solution layout, library design, data flow |
| [API Reference](docs/api-reference.md) | Complete `Chd`, `ChdFile`, and all public models |
| [Encoder](docs/encoder.md) | CHD creation, codecs, dedup, chdman validation |
| [Verification](docs/verification.md) | Full/parallel and header-only verification |
| [Extraction](docs/extraction.md) | TOC parsing, CUE/GDI/ISO extraction |
| [Metadata](docs/metadata.md) | Reading and querying CHD metadata tags |
| [Parent/Child CHDs](docs/parent-child-chds.md) | Differential CHDs, unit-based references |
| [Performance](docs/performance.md) | Throughput, parallelism tuning, caching |
| [Testing](docs/testing.md) | The xUnit suite, corpus, generators |
| [Troubleshooting](docs/troubleshooting.md) | Common errors, known limitations |

---

## Tests

| Project | Type | Description |
|---------|------|-------------|
| `CHDSharpTest` | xUnit | Unit + corpus tests (468 tests, 30 CHD fixtures) |
| `CHDSharpEncoderTest` | xUnit | Encoder tests (350 tests, chdman cross-validation) |
| `CHDSharpTester` | WPF | Interactive batch verification against `chdman` |
| `CHDSharpTestGen` | Console | Deterministic corpus generator |

```bash
# Run all tests
dotnet test

# Run only encoder tests
dotnet test CHDSharpEncoderTest/

# Run large file integration tests
dotnet test CHDSharpEncoderTest/ --filter "FullyQualifiedName~LargeFileValidationTests"

# Regenerate corpus (requires chdman binaries)
dotnet run --project CHDSharpTestGen
```

---

## Building

```bash
git clone https://github.com/purelogiccode/CHDSharp.git
cd CHDSharp
dotnet build -c Release

# NuGet package
dotnet pack -c Release CHDSharpLib/
```

Requires [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or later. Works on Windows, Linux, and macOS.

---

## Project Layout

```
CHDSharp/
├── CHDSharpLib/            The library (NuGet: CHDSharp)
│   ├── CHD.cs              Main entry point (Chd class)
│   ├── CHDFile.cs          Random-access reader (ChdFile)
│   ├── CHDHeaders.cs       V1–V5 header parsing
│   ├── CHDReaders.cs       Codec decompression
│   ├── CHDMetaData.cs      Metadata read/write
│   ├── ChdTocParser.cs     CD/GD-ROM TOC parsing
│   ├── CueConverter.cs     CUE sheet generation
│   ├── DiscDetector.cs     Platform/game detection
│   ├── Encoder/            CHD creation (ChdEncoder)
│   │   ├── ChdEncoder.cs   Main encoder API
│   │   ├── ChdCodec.cs     Codec implementations
│   │   ├── HunkProcessor.cs Parallel compression pipeline
│   │   ├── MapCompressor.cs V5 compressed map writer
│   │   ├── ParentMap.cs    Delta/parent CHD support
│   │   ├── Models/         Encoder models (ChdEncodeOptions, etc.)
│   │   └── Interfaces/     IChdCodec interface
│   ├── Models/             Public models (ChdHeaderInfo, ChdError, etc.)
│   └── Utils/              Internal utilities (CRC, Huffman, BitStream)
├── CHDSharpCli/            CLI tool (binary: CHDSharp)
├── CHDSharpTest/           Unit + corpus tests
├── CHDSharpEncoderTest/    Encoder tests
├── CHDSharpTester/         WPF interactive tester
├── CHDSharpTestGen/        Corpus generator
├── CHDSharpBench/          Benchmarks
├── CHDSharpBattleTest/     Battle test harness
├── VendoredZLib/           Pure C# zlib port
├── VendoredLZMA/           LZMA SDK C# port
└── VendoredFlac/           Pure C# FLAC encoder/decoder
```

---

## License

MIT License — see [LICENSE](LICENSE.txt).

The `VendoredFlac` component (FLAC encoder/decoder) is based on [CUETools.Flake](https://github.com/gchudov/cuetools.net) and is licensed under the [GNU Lesser General Public License v2.1](https://www.gnu.org/licenses/old-licenses/lgpl-2.1.html). See [LICENSE.txt](LICENSE.txt) for full details.

### Special Thanks

**Gordon Jefferyes ([@gjefferyes](https://github.com/gjefferyes))** — the original author of [RomVault/CHDSharp](https://github.com/RomVault/CHDSharp), which this project is forked from. Gordon built the foundational C# CHD reader (V1–V5 headers, zlib/lzma/huffman/flac codecs, and a custom LZMA/FLAC stack) that this project extends with Zstd, AVHuff, parallel verification, async APIs, metadata support, encoding capabilities, and comprehensive testing.

### Acknowledgments

- **[MAME](https://www.mamedev.org/)** — CHD format specification and `chdman` reference implementation
- **[libchdr](https://github.com/rtissera/libchdr)** — C reference library by Romain Tisseraud
- **[ZstdSharp.Port](https://github.com/oleg-st/ZstdSharp)** — pure C# Zstd decompressor by Oleg Stepanischev

---

* **Donate:** [support the developer](https://www.purelogiccode.com/donate)
* **Star this repo on [GitHub](https://github.com/purelogiccode/CHDSharp)**
