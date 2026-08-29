# CHDSharp Wiki

Welcome to the **CHDSharp** documentation wiki.

CHDSharp is a **pure C# CHD (Compressed Hunks of Data) reader and writer** — the disk-image format used by [MAME](https://www.mamedev.org/) for arcade hard disks, CD/GD-ROMs, DVDs, and laserdisc A/V content. It supports every CHD format version (V1–V5), every compression codec ever shipped in a CHD (including Zstd and AVHuff), parent/child differential chains, parallel verification, metadata, TOC parsing, extraction, and CHD creation — with **zero native dependencies** and a **100% byte-for-byte match with MAME `chdman`**.

> This project is a fork of [RomVault/CHDSharp](https://github.com/RomVault/CHDSharp) by Gordon Jefferyes, extended with Zstd, AVHuff, V5 compressed maps, random access, async APIs, parent/child chaining, parallel verification, seekable stream, span reads, read-ahead decompression, lazy parent resolution, and a comprehensive test suite. The C reference implementation ([libchdr 0.3.0](https://github.com/rtissera/libchdr)) and the MAME 0.289 sources are used as the authoritative format references.

---

## About CHD

**CHD** was created by **Aaron Giles** in **March 2002** (MAME 0.59). Originally called *"Compressed Hard Disk,"* it was designed to store arcade hard disk images — the first game using CHD was *Wargods* (January 2003). The name was later backronymed to *"Compressed Hunks of Data"* as the format expanded to cover CD-ROMs (V3, November 2003), laserdiscs (V4, March 2009), and DVDs (2023).

The format's companion tool, **`chdman`**, started life as `hdcomp` (hard disk compressor) and was renamed when V3 broadened CHD's scope beyond hard drives. Today, `chdman` is the reference implementation — any tool that wants to be compatible must match its output byte-for-byte.

CHDSharp is a pure C# implementation that reads and writes every CHD version with 100% parity against `chdman` (MAME 0.289). See [CHD Format Reference](chd-format.md) for the full history and on-disk format.

---

## Quick facts

| | |
|---|---|
| Package | [`CHDSharp`](https://www.nuget.org/packages/CHDSharp/) |
| Targets | `net8.0`, `net9.0`, `net10.0` |
| Format versions | CHD V1 – V5 (read & write) |
| Codecs | `zlib`, `lzma`, `huff`, `flac`, `zstd`, `avhu` + CD variants `cdzl`, `cdlz`, `cdfl`, `cdzs` |
| Native dependencies | **none** |
| License | MIT (project code) / LGPL-2.1 (`VendoredFlac`) |
| Repository | https://github.com/purelogiccode/CHDSharp |

---

## Wiki contents

### Getting started

| Page | Description |
|------|-------------|
| [Getting Started](getting-started.md) | Install the package, write your first program, tour the CLI. |
| [Building](building.md) | Build the solution, create the NuGet package, publish the CLI. |

### Format & internals

| Page | Description |
|------|-------------|
| [CHD Format Reference](chd-format.md) | The on-disk format: headers, maps, metadata, hashing, delta CHDs — V1 through V5. |
| [CHD Deep Reference](chd-deep-reference.md) | Audited deep dive: V1–V5 history, codecs, creation workflow, V5 map quirk, metadata, hashing, parent/child — with `⚠ Correction`s vs the old reference (MAME 0.289). |
| [Codecs](codecs.md) | All ten decompression codecs: how they work and how each is implemented. |
| [Architecture](architecture.md) | Solution layout, library design, data flow, threading model. |

### API & usage

| Page | Description |
|------|-------------|
| [API Reference](api-reference.md) | Complete reference for `Chd`, `ChdFile`, and all public models/enums. |
| [Metadata](metadata.md) | Reading and querying CHD metadata tags (`GDDD`, `CHT2`, `AVAV`, …). |
| [Verification](verification.md) | Full/parallel and header-only verification, checksum semantics. |
| [Extraction](extraction.md) | TOC parsing, CUE/GDI/ISO/IMG/RAW extraction, classification. |
| [Parent/Child CHDs](parent-child-chds.md) | Differential CHDs, unit-based references, parent validation. |

### Operations

| Page | Description |
|------|-------------|
| [Performance](performance.md) | Throughput, parallelism tuning, caching, `Precache()`. |
| [Logging](logging.md) | Pluggable logging via `Microsoft.Extensions.Logging`. |
| [Error Codes](error-codes.md) | Every `ChdError` value and its meaning. |
| [Testing](testing.md) | The xUnit suite, the 30-file corpus, generators, and the WPF tester. |

### Writing CHDs

| Page | Description |
|------|-------------|
| [Encoder (CHD creation)](encoder.md) | `CHDSharp.Encoder`: raw/CD encoding, codecs, dedup, `chdman` validation, ratio logging. |

### Reference

| Page | Description |
|------|-------------|
| [Comparison with libchdr](libchdr-comparison.md) | Feature parity vs the C reference library, plus the five-way table (CHDSharp vs chd-rs vs CHDlite vs chdman vs libchdr). |
| [Troubleshooting & FAQ](troubleshooting.md) | Common errors, known limitations, and fixes. |
| [Release Notes](ReleaseNotes.md) | Version history and changelog. |

---

## Feature overview

- **Any CHD, any version** — V1–V5 headers, every internal map format (self-hunk dedup, CRC32 maps, CRC16/compressed/RLE maps, uncompressed V5 maps, unit-based parent references).
- **Header DTO without opening** — `Chd.ReadHeader()` returns the full parsed header (`ChdHeaderInfo`) without keeping the file open (libchdr `chd_read_header` parity); `CheckHeader`/`IsChdFile` remain for magic/version sniffing.
- **All 10 codecs** — zlib, lzma, huffman, flac, zstd, AVHuff, plus the four CD-aware variants (`cdzl`, `cdlz`, `cdfl`, `cdzs`) with ECC/sync regeneration.
- **Random access** — `ReadHunk()`, `Read()` (byte ranges across hunk boundaries), `EnumerateHunks()`, `ReadAllBytes()`.
- **LBA/MSF sector reads** — `ReadSector()`, `ReadSectorMsf()`, and `ReadFrame()` address CD/GD-ROM sectors or full 2448-byte frames by logical block address (pregap-aware mapping through the track table); `CdRomAddress` converts between BCD MSF and LBA.
- **Async API** — `OpenAsync`, `ReadHunkAsync`, `ReadAsync`, `IAsyncDisposable`.
- **Seekable stream** — `OpenAsStream()` returns a read-only, seekable `Stream` over the decompressed image.
- **Span\<byte\> reads** — `ReadHunk(uint, Span<byte>)` and `Read(ulong, Span<byte>, int)` for zero-copy paths.
- **Read-ahead decompression** — `ConfigureReadAhead(int)` enables background pre-decompression of upcoming hunks.
- **Lazy parent resolution** — `ParentResolver` callback resolves parents by SHA1/MD5 hash on first read.
- **Progress reporting** — optional `IProgress<ChdProgress>` on `CheckFile`, `CheckFileWithParent`, `ReadAllBytes`, `EnumerateHunks`, and `ExtractToDirectory`, reported after every decompressed hunk.
- **Cancellation** — optional `CancellationToken` on every long-running API (`Open`/`OpenAsync`, `Read`/`ReadAsync`, `ReadHunk`/`ReadHunkAsync`, `ReadAllBytes`, `CheckFile`, `CheckFileWithParent`, `ExtractToDirectory`), linked into the parallel verification pipeline; throws `OperationCanceledException`.
- **Parallel verification** — multi-threaded `CheckFile()` with bounded memory and configurable worker count.
- **Parent/child chains** — transparent differential CHD support with wrong-parent detection.
- **Metadata** — tag/index query API (`GetMetadata`) plus the full entry list; checksum-flag aware. IDNT (ATA IDENTIFY), KEY (encryption), CIS (PCMCIA) metadata support.
- **Track info & extraction** — CD/GD-ROM TOC parsing (`ChdTrackInfo`), CUE/GDI descriptor generation, legacy `CHGT` little-endian CDDA handling (`IsLittleEndianAudio`), and whole-image extraction (`.bin`/`.cue`, `.iso`, `.img`, `.raw`, `.gdi`).
- **Pluggable logging** — `Microsoft.Extensions.Logging` integration, silent by default.
- **100% chdman match** — cross-checked against `chdman` (MAME 0.289) via `info`, `verify`, `extractraw`, and the `CHDSharpBattleTest` harness (2611/2611 synthetic + 3003/3003 real-world checks passing). v1.4.1 closes the last 16 discrepancies: `createhd -i` GDDD, `extractcd` cooked/raw, GD-ROM Redump, `copy` per-type defaults, DVD empty payload, and strict CLI validation.
- **Full `chdman` CLI parity** — the CLI (`CHDSharp`) accepts every `chdman` subcommand with matching options and exit codes (strict validation parity as of v1.4.1).

---

## Support matrix

### Format versions

| Version | Header | Map | Notes |
|---------|--------|-----|-------|
| V1 | 76 bytes | 8-byte packed offset/length entries, self-hunk dedup | 512-byte sectors, MD5 only, no metadata |
| V2 | 80 bytes | Same as V1 | Adds `seclen` (bytes/sector) |
| V3 | 120 bytes | 16-byte entries with CRC32, self/parent hunks | Adds SHA1, metadata, `ZLIB_PLUS` |
| V4 | 108 bytes | Same as V3 | Adds `rawsha1`; combined SHA1 semantics |
| V5 | 124 bytes | CRC16 compressed map (Huffman+RLE) or uncompressed map | Up to 4 codecs, unit-based parent refs |

### Codecs

| Codec | FourCC | CD variant | C# implementation |
|-------|--------|------------|-------------------|
| Zlib (Deflate) | `zlib` | `cdzl` | `System.IO.Compression` |
| LZMA | `lzma` | `cdlz` | Custom pure-C# LZMA decoder |
| Huffman | `huff` | — | Custom pure-C# Huffman decoder |
| FLAC | `flac` | `cdfl` | Custom pure-C# FLAC decoder |
| Zstd | `zstd` | `cdzs` | `VendoredZSTD` (in-repo pure C# port of zstd 1.5.5) |
| AVHuff | `avhu` | — | Custom pure-C# A/V Huffman decoder |

---

## Related projects in this repository

| Project | Purpose |
|---------|---------|
| `CHDSharpLib` | The library itself (this wiki documents it). Includes the encoder subsystem (`CHDSharp.Encoder`). |
| `CHDSharpCli` | Command-line CHD manager (binary: `CHDSharp`). Full `chdman` subcommand parity. |
| `CHDSharpTest` | xUnit unit + corpus test suite (602 tests, 30 deterministic CHD files). |
| `CHDSharpEncoderTest` | xUnit encoder suite (434 tests) with chdman cross-validation. |
| `CHDSharpBattleTest` | Battle harness: 2611/2611 (deterministic) + 3003/3003 (real-world) checks vs `chdman`. |
| `CHDSharpTestGen` | Deterministic corpus generator (drives vintage `chdman` binaries). |
| `CHDSharpTester` | WPF interactive batch verifier cross-checked against `chdman`. |

---

## License

This is a combined work. The project code is **MIT**; `VendoredFlac` is **LGPL-2.1** (CUETools.Flake); `VendoredZLib` is **zlib-licensed**; `VendoredLZMA` is **public domain**; `VendoredZSTD` is **MIT** (based on Facebook zstd, BSD-3-Clause). See [LICENSE.txt](../LICENSE.txt) for the full third-party notice and obligations.

## Acknowledgments

- **Peterson Fernandes** — extended Gordon Jefferyes' original CHD reader into a full-featured library: encoder, CLI (`CHDSharp`), NuGet package, VendoredZSTD, async/stream APIs, parent/child chaining, parallel verification and encoding, V5 compressed maps, DVD/GD-ROM/laserdisc support, 100% `chdman` byte-parity, and a comprehensive test suite.
- **Gordon Jefferyes** — original C# CHDSharp reader (RomVault).
- **MAME** — CHD format specification and `chdman` reference implementation.
- **libchdr** (Romain Tisseraud) — C reference library, used for parity comparison.
- **ZstdSharp.Port** (Oleg Stepanischev) — pure C# Zstd port, vendored in-repo as `VendoredZSTD`; now matches MAME's zstd 1.5.5 frames byte-for-byte.
