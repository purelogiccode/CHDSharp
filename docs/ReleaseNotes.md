# CHDSharp Release Notes

## CHDSharp v1.4.0

### Full chdman CLI argument parity

The CLI (binary renamed `CHDSharp`) now accepts **every `chdman` subcommand** with the same
option names and exit-code conventions:

`info`, `verify`, `createraw`, `createhd`, `createcd`, `createdvd`, `createld`,
`extractraw`, `extracthd`, `extractcd`, `extractdvd`, `extractld`, `copy`, `addmeta`,
`delmeta`, `dumpmeta`, `listtemplates`.

Common options mirror `chdman` (`--input/-i`, `--output/-o`, `--inputparent/-ip`,
`--outputparent/-op`, `--compression/-c`, `--hunksize/-hs`, `--unitsize/-us`,
`--numprocessors/-np`, `--force/-f`, `--verbose/-v`, `--chs`, `--sectorsize/-ss`, …) and the
convenience commands from earlier versions (directory scan, `--list`, `--random`, `--parent`,
`--toc`, `--cue`, `--classify`, `--detect`, `--hash`, `--batch`) are all still available.

### Full chdman battle-test parity (587/587)

The new `CHDSharpBattleTest` harness exhaustively cross-checks the CHDSharpLib **decoder**
and the CHDSharp.Encoder **encoder** against MAME's `chdman.exe` on a deterministic corpus
of raw and CD images — `chdman create*`, `verify`, `info`, extract parity, and **byte-identical
encode** checks for every writable codec, plus delta/parent, CD, and A/V laserdisc scenarios.
The full suite passes **587/587 checks**. It can also scan real-world `*.chd` folders
(`--real <dir>`) to battle-test any collection. See [Testing](testing.md).

### Byte-for-byte parity with chdman for all codecs

Every encoder output now matches MAME's `chdman` byte-for-byte, closing the three
previously non-exact paths:

- **`createld` (laserdisc AVHuff)** — fixed an off-by-four AVCC `idx1` base offset that
  shifted audio by one sample and misaligned video; the AVHuff codec now correctly
  compresses zero-padded single-frame hunks; and the mono-FLAC encoder now evaluates
  every fixed-predictor order per frame (matching libFLAC 1.4.3's behaviour for the
  mono/48 kHz avhu path).
- **`cdzs` (CD Zstandard)** — the in-repo `VendoredZSTD` port (a C-to-C# port of the
  zstd 1.5.5 tree that MAME bundles) now emits frames byte-identical to C zstd for the
  same hunk buffers, replacing the previous NuGet `ZstdSharp.Port` dependency.
- **`zstd` (raw Zstandard)** — same fix as `cdzs`; the encoder's `zstd` output is now
  byte-identical to `chdman createraw -c zstd`.

### VendoredZSTD replaces ZstdSharp.Port NuGet dependency

The `ZstdSharp.Port` NuGet package has been replaced by an in-repo `VendoredZSTD` project
containing a full C-to-C# port of the zstd 1.5.5 source tree (the same version MAME
bundles). Both the encoder and decoder are included. The library now has **zero external
runtime NuGet dependencies** — every codec is vendored in-repo.

### 38 bugs fixed from deep code review

A deep code review across the CLI, library, and battle harness fixed 38 bugs. Highlights:

- **Exit codes & error handling** — the CLI now returns proper exit codes (`0/1/3`),
  `CheckFile(Stream)` catches `IOException`, and `VerifyList` logs the exception type verbatim.
- **Encoding fixes** — `createcd` defaults to `cdlz,cdzl,cdfl`; `createhd --input` routes to
  `EncodeRaw`; `verify --inputparent` is passed to `CheckFileWithParent`; all create/copy
  commands require `--force` to overwrite; `dumpmeta --force` is implemented; `extractcd
  --splitbin` delegates to the library's per-track writer and `--outputbin` updates `.gdi`
  for GD-ROM; `LzmaStream.Seek` returns the correct position.
- **Concurrency** — the parallel-verification master error is now thread-safe
  (`StrongBox` + `Interlocked`), `DisposeAsync` drains per-thread codec states, and the
  read-ahead manager no longer over-releases its semaphore.
- **Robustness** — `ReadHunk(Span)` caches hunks on success, `FindRepeatedBlocks` validates
  map offset bounds, `ValidateSizeLimits` rejects `Unitbytes == 0`, `MemoryMappedFile` is
  disposed when view creation fails, and `extractraw` writes to a temp file before renaming.
- **Diagnostics** — progress callbacks use `Interlocked`, version display uses `Build` instead
  of `MinorRevision`, bug reports scrub PII, and API keys are no longer stored in plain text.
- **CLI helpers** — `--batch` filters by correct extensions, `Checkdir` filters exact `.chd`
  extension and skips symlinks, `help` covers all missing commands, and `addmeta` rejects
  non-ASCII tag names.

### Flaky-test fixes for .NET 8/9/10

Stabilized flaky tests so the full suite passes consistently on every target framework.

### Cleaner vendored code

Resolved all compiler, analyzer, and Rider-inspection warnings across `VendoredZSTD`
and `VendoredZlib`, including XML doc-comment and constant compile errors, and removed the
outdated `References/MissingFeatures.md`.

### Breaking changes

- **CLI binary renamed** — the CLI executable is now `CHDSharp` (e.g. `CHDSharp.exe`) instead
  of `CHDSharpCli`. Subcommand syntax changed to the `chdman` style (`CHDSharp createcd -o
  out.chd -i in.cue`); the old `--create/--createcd/--createhd/--copy` forms are replaced.
- **`ZstdSharp.Port` package removed** — code that referenced it directly must now use the
  library's own codecs; no action is needed for normal library use.

### NuGet Package

```
dotnet add package CHDSharp --version 1.4.0
```

### CLI Binaries

Pre-built self-contained single-file executables (binary renamed from `CHDSharpCli` to `CHDSharp`):

| Binary | Platform | Architecture |
|--------|----------|-------------|
| `CHDSharp_win-x64_v1.4.0.zip` | Windows | x64 |
| `CHDSharp_win-arm64_v1.4.0.zip` | Windows | ARM64 |
| `CHDSharp_linux-x64_v1.4.0.zip` | Linux | x64 |
| `CHDSharp_linux-arm64_v1.4.0.zip` | Linux | ARM64 |
| `CHDSharpTester_win-x64_v1.4.0.zip` | Windows | x64 |
| `CHDSharpTester_win-arm64_v1.4.0.zip` | Windows | ARM64 |

---

# CHDSharp v1.3.0 Release Notes

## Overview

CHDSharp v1.3.0 merges the encoder subsystem into the main library, adds hard disk metadata support, hardens security, and fixes critical decoder bugs. The library now provides full CHD read/write capabilities in a single NuGet package with zero native dependencies.

## What's New

### Merged Encoder into CHDSharpLib

The `CHDSharpEncoder` project has been merged into `CHDSharpLib`. The library now includes full CHD creation/encoding capabilities (`ChdEncoder`) alongside the existing reader. No separate package needed.

- **Raw binary encoding** -- `ChdEncoder.EncodeRaw()` with auto or custom hunk/unit sizes
- **CD image encoding** -- `ChdEncoder.EncodeCd()` from CUE, GDI, ISO, TOC, or NRG
- **Blank HD creation** -- `ChdEncoder.CreateBlank()` / `CreateBlankWithChs()` for zero-filled images
- **Re-compression** -- `ChdEncoder.Copy()` re-compresses existing CHDs with new codecs
- **Delta/parent CHDs** -- Create differential CHDs against a parent
- **All 10 codecs** -- zlib, zstd, lzma, huff, flac, cdzl, cdlz, cdzs, cdfl, none; best-per-hunk selection
- **Parallel encoding** -- 1-64 workers, deterministic output regardless of worker count
- **100% chdman match** -- Byte-identical output vs `chdman` for all codecs

### Hard Disk Metadata Support

- **IDNT metadata** -- Read/write `IDNT` metadata (ATA IDENTIFY DEVICE response, 512 bytes) preserving original drive model, serial, CHS geometry, and firmware revision. Access via `ChdFile.IdentData` property. `--ident <path>` flag on `createhd` CLI command.
- **KEY metadata** -- Read/write `KEY ` metadata (encryption key) used by OG Xbox and other platforms with encrypted HDD contents. Access via `ChdFile.KeyData` property.
- **CIS metadata** -- Read/write `CIS ` metadata (Card Information Structure) used by PC Engine CD and other PCMCIA platforms. Access via `ChdFile.PcmciaCisData` property.

### Security Hardening

- **Bounded metadata string parsing** -- Hardened track metadata parsing against crafted payloads. TYPE/SUBTYPE/PGTYPE/PGSUB fields are capped at 15 characters. Track metadata payloads > 4 KiB are rejected. Embedded null bytes in payloads are rejected. Metadata entries > 64 KiB are rejected at the storage layer.
- **Deflate decoder infinite-loop guard** -- Added `here.bits == 0` guards in the inflate state machine. When a Huffman table entry has `bits=0`, the decoder transitions to `Bad` mode and returns `Z_DATA_ERROR` instead of looping indefinitely.

### New APIs

- **`ChdImageStream`** -- `ChdFile.OpenAsStream()` returns a read-only, seekable `Stream` wrapping the decompressed CHD. Supports `Read`, `ReadAsync`, `Seek`, `Position`, and `Length`.
- **`Span<byte>` read overloads** -- `ReadHunk(uint, Span<byte>)` and `Read(ulong, Span<byte>, int)` for zero-copy paths.
- **Threaded read-ahead decompression** -- `ChdFile.ConfigureReadAhead(int)` enables background pre-decompression of upcoming hunks.
- **Lazy parent resolution** -- `ParentResolver` callback resolves parents by SHA1/MD5 hash on first read.
- **CD/GD-ROM track (TOC) parsing** -- Full track layout via `Tracks` property with `ChdTrackInfo`, CUE/GDI generation, and extraction.
- **LBA/MSF sector reads** -- `ReadSector()`, `ReadSectorMsf()`, and `ReadFrame()` for CD/GD-ROM sector access.
- **`UnitBytes` property** -- Derives sector size from metadata for all CHD versions.

### Bug Fixes

- Fixed VendoredZLib `#else` compilation branches using incorrect field names (snake_case vs PascalCase)
- Fixed deflate decoder infinite-loop on crafted input (miniz 3.1.2 fix parity)

## Supported Platforms

- **Target frameworks**: `net8.0`, `net9.0`, `net10.0`
- **Operating systems**: Windows, Linux, macOS
- **Zero native dependencies** -- all codecs implemented in pure C#

## NuGet Package

```
dotnet add package CHDSharp --version 1.3.0
```

## CLI Binaries

Pre-built self-contained single-file executables (binary renamed from `CHDSharpCli` to `CHDSharp`):

| Binary | Platform | Architecture |
|--------|----------|-------------|
| `CHDSharp_win-x64_v1.3.0.zip` | Windows | x64 |
| `CHDSharp_win-arm64_v1.3.0.zip` | Windows | ARM64 |
| `CHDSharp_linux-x64_v1.3.0.zip` | Linux | x64 |
| `CHDSharp_linux-arm64_v1.3.0.zip` | Linux | ARM64 |
| `CHDSharpTester_win-x64_v1.3.0.zip` | Windows | x64 |
| `CHDSharpTester_win-arm64_v1.3.0.zip` | Windows | ARM64 |

## Upgrade Notes

- The `CHDSharpEncoder` project is no longer available as a separate package. All encoder functionality is now in `CHDSharpLib` under the `CHDSharp.Encoder` namespace.
- The `ChdEncoder` class replaces the old `CHDSharpEncoder.ChdEncoder` class.
- All existing reading APIs remain unchanged and fully backward compatible.

## Acknowledgments

- **Gordon Jefferyes** -- original C# CHDSharp implementation (RomVault)
- **MAME** -- CHD format specification and `chdman` reference implementation
- **libchdr** (Romain Tisseraud) -- C reference library
- **ZstdSharp.Port** (Oleg Stepanischev) -- pure C# Zstd port, vendored in-repo as `VendoredZSTD`
- **CUETools.Flake** (Grigory Chudov) -- FLAC encoder (LGPL 2.1)
