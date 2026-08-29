# CHDSharp Release Notes

## CHDSharp v1.4.2

Final byte-parity gaps closed against MAME 0.289. Battle harness stays green: **2907/2907 synthetic + 3003/3003 real-world checks**.

### LZMA raw-encode byte parity — MAME compression work-buffer replication

The raw encoder now replicates `chd.cpp`'s 1 MiB compression work buffer (`RingBufferedRawReader`): a 256-hunk ring filled in 128-hunk batches (`async_read`), where ring slots past EOF or past a short final batch keep the stale bytes of the cycle that previously occupied them. Hunks are compressed from the ring — stale tail bytes included — while the raw SHA-1 folds only the valid bytes, exactly like `m_compsha1.append(dest, numbytes)`. This fixes the LZMA byte-parity hash-update-order bug: `createraw` on inputs whose length is not a multiple of the hunk size now produces byte-identical files and identical SHA-1 to `chdman`. Companion fixes in `FlacLpcMath`, `LibFlacEncoder`, `LzBinTree` and `LzmaEncoder` keep the remaining codec paths byte-exact.

### `createhd` size / CHS quirks (D-parity)

- Size parsing now matches `chdman.cpp:2035` — `sscanf("%I64u")` reads leading digits only and silently ignores trailing characters (`"512K"` = 512 bytes).
- Small sizes are rounded **up** to the guessed-CHS product (`guess_chs` parity): the CHD logical size is exactly `cylinders * heads * sectors * bytes_per_sector`, and hunks past the source's data are encoded from the work buffer's stale slots (see above), matching `chdman createhd -i`.
- `InfoTest`/`VerifyTest` now receive the full arg list so strict option validation (duplicate `-i/--input` detection etc.) matches `chdman`'s `core_options` parser; `info` returns exit code 1 on failure.

### Laserdisc AVI byte parity

`createld` / `extractld` AVI output is now byte-identical to `chdman` (`AviWriter` fixes).

### DVD metadata payload correction

`DVD ` metadata is written as exactly one NUL byte (length 1), not an empty payload — `chd.h:351`'s `std::string` overload passes `input.length() + 1`, so `write_metadata(DVD_METADATA_TAG, 0, "")` stores a single NUL. This corrects the v1.4.1 note (which documented an empty payload).

### CLI fixes

- `createraw` accepts `--hunk-size` / `--unit-size` aliases alongside `--hunksize` / `--unitsize` / `-hs` / `-us`.
- `extractcd` `%t` filename-template regex rewritten with named groups (escaped `%%` and width/sign specifiers handled correctly).
- `hash` command help text corrected (`--result text|json|sfv`, `--tracks`, no phantom `count` argument).
- New `ChdEncodeOptions.LogicalLengthBytes` override for byte-parity encoding of over-/under-sized `createhd` sources.

### Tooling and testing

- `CHDSharpBattleTest` multi-targets `net8.0;net9.0;net10.0` with `LangVersion=latest` and copies the built CLI next to the battle exe for self-contained parity runs (`BattleHarness.CliFull.cs` — full `chdman` CLI suite, 2463 lines).
- Formatting and style pass across CLI, encoder, and vendored codecs; analyzer warnings silenced (MA0008 StructLayout, unused-variable suppressions).
- `divergency.md` removed; `Missmatch.md` now records the battle result: **2907 checks — 2907 passed, 0 failed**.

### Documentation

- New `docs/cli-commands.md` (413 lines) — full `chdman`/CHDSharp argument tables for every command; `CHDSharpCli/README.md` expanded.
- CHD format history added to `docs/chd-format.md`; language simplified and stale DVD metadata notes corrected.
- Wiki home (`docs/README.md`) rewritten with CHD history; `ReleaseNotes` added to sidebar and `_config.yml`; docs site URL fixed; Peterson Fernandes added to key contributors.

### NuGet Package

```
dotnet add package CHDSharp --version 1.4.2
```

Targets `net8.0`, `net9.0`, `net10.0`. Pure C# — zero native dependencies.

### CLI Binaries

Pre-built self-contained single-file executables (binary: `CHDSharp`):

| Binary | Platform | Architecture |
|--------|----------|-------------|
| `CHDSharp_win-x64_v1.4.2.zip` | Windows | x64 |
| `CHDSharp_win-arm64_v1.4.2.zip` | Windows | ARM64 |
| `CHDSharp_linux-x64_v1.4.2.zip` | Linux | x64 |
| `CHDSharp_linux-arm64_v1.4.2.zip` | Linux | ARM64 |
| `CHDSharpTester_win-x64_v1.4.2.zip` | Windows | x64 |
| `CHDSharpTester_win-arm64_v1.4.2.zip` | Windows | ARM64 |

---

## CHDSharp v1.4.1

Complete `chdman` parity — 16 audited discrepancies (D1–D16) plus EdgeGaps §1–§3 fixed and verified against MAME 0.289. All 2907/2907 synthetic + 3003/3003 real-world battle checks pass.

### Fixed `createhd -i` missing `GDDD` metadata (D2)

`CHDSharp createhd -i image.img -o out.chd` now synthesizes the `GDDD` hard-disk geometry tag
(`CYLS:…HEADS:…SECS:…BPS:…`) via `MetadataWriter.BuildHardDiskMetadata`, matching
`chdman createhd -i` byte-for-byte. The 51-byte delta on all 3 HDD test images is gone and
`chdman info` shows identical `GDDD` on both products. Honours `-isb`/`-ish`/`-ib`/`-ih` slicing when present.

### Fixed `extractcd` cooked vs raw frame convention (D1)

`CHDSharpLib.ChdFile.ExtractToDirectory(..., cooked: true)` now writes cooked sectors
(`track.DataSize` per frame, 2048/2352/…, subcode omitted, audio byte-swapped) and
`CHDSharp extractcd` defaults to cooked (with `--raw`/`--raw-frames` to keep 2448-byte frames).
The 43 CD + 3 GD-ROM `extractcd` battleground cases flip from `0/43` to `43/43` parity
and `disc.bin` sizes match `chdman` (e.g. Akai Shizuku `8,773,030 B`).

### Documented `createcd -c cdzl` compressed-bytes divergence (D3)

`cdzl`/`cdfl` on audio-bearing discs can pick different FLAC subframes than MAME's
native `libFLAC`; the container bytes may differ while Data SHA-1/overall SHA-1 and
`chdman verify` remain identical. `docs/encoder.md` now carries the caveat and the
validation table notes that 25/43 `createcd:cdzl` products are byte-identical while the
remaining 18 are logical-parity only.

### GD-ROM Redump parity (D12)

Ported `chdman has_physical_pregap` / `padframes` / `splitframes` fixup for GD-ROM Redump CUE/BIN.
`ChdTrackInfo` now exposes `SplitFrames` / `PhysFrameOfs`, `CHDFile` adds `TryWriteGdRomTrackCooked` with cross-track reads and the 45000-frame high-density boundary skip, and `extractcd` emits `REM SINGLE-DENSITY AREA` / `REM HIGH-DENSITY AREA`. `MODE_GDI` (pad-aware LBA) vs `MODE_CUEBIN` (split CUE with pad/split) are now handled per `chdman` (`is_splitbin = mode==GDI || --splitbin || (is_gdrom && mode==CUEBIN)`).

### CLI strictness parity (D13)

Option parsing now matches `chdman` verbatim: unknown / duplicate / missing-parameter errors, per-command valid sets (`createraw` rejects `-tp`/`-d`, etc.), `isb`/`ish`/`ib`/`ih` mutual exclusion (`Start offset cannot be in both…`), and `parse_number` trailing-`B` handling (`10MB`). Error phrasing (`Required parameters missing`, `Multiple parameters…`) matches `chdman.cpp`.

### `copy` per-type defaults and parent handling (D14–D16, EdgeGaps 1.1–1.3)

- `copy` now uses `get_compression_defaults:2426` per media type — HDD/DVD → `lzma,zlib,huff,flac`, CD/GD → `cdlz,cdlz,cdfl`, laserdisc → `avhu` (`ChdEncoder.GetDefaultCopyCodecs`).
- Parent hunk-size inheritance and factor check (`parse_hunk_size:1331`, `hunk % input && input % hunk`) for `createraw`/`createhd`/`createcd`/`createdvd`/`createld`/`copy`.
- `createhd` template+parent / `chs`+parent guards (`do_create_hd:1980/1998`), `IDENT` CHS extraction from ATA bytes 2/6/12 (`>=16_514_064 → cyl=0`), `GDDD` fallback, `filesize % sectorsize` check and `guess_chs` parity.
- `info --verbose` now shows per-codec `SELF`/`PARENT`/`MINI` hunk buckets and `verify` throttles to 0.5 s with `Error:` to `stderr` (`report_error:950` parity).

### DVD empty payload, extraction buffering and audio-swap parity

- `MetadataWriter.BuildDvdMetadata` now returns an empty payload (length 0) per `chdman write_metadata(DVD_METADATA_TAG,0,"")` instead of a single `0x00` byte.
- `CHDFile` extraction now uses a 32 MiB temp buffer aligned to `outputFrameSize` with batch writes, correct audio byte-swap (CUEBIN always, GDI only `Version>4` per `chdman 2959/2994`), and proper subcode preservation for `MODE_NORMAL` (`.toc`) vs warn/omit for `CUEBIN`/`GDI`.

### Hard-disk templates and defaults (D4–D11)

- `listtemplates` now shows all 17 templates (added 4× Quantum Fireball CR).
- `createraw`/`createhd` defaults now correctly use `s_default_raw_compression` / `s_default_hd_compression` (`lzma,zlib,huff,flac`) and enforce blank HD `none` only.
- `createraw` unit-size / hunk-size defaults, 16 B–1 MiB limits and granularity checks match `chdman`.
- `addmeta --valuetext` no longer appends a trailing NUL (`text.size()` parity) and `extractcd --outputbin` now requires `%t` when `is_splitbin`.

### New CHD Deep Reference

New `docs/chd-deep-reference.md` — audited expansion of `References/CHDInfo.md` against MAME 0.289 (`chd.h`/`chd.cpp`/`chdcodec.h`) and `CHDSharpLib` (`CHDHeaders.cs`/`CHDBlockRead.cs`/`MapCompressor.cs`). 9 inline `⚠ Correction`s (ZLIB_PLUS secondary codec, `cdzs` subcode `zstd`, work buffer 256 vs 257, RLE thresholds `4–19`/`20–275`, promotion before RLE, implicit `datastart` offset, DVD empty payload, uncompressed-map `offsetWord`, `hunkbytes` limits) and a full V1–V5, codec, and map-encoding reference. Sidebar updated. `docs/chd-format.md` corrected to 0.289 and RLE description fixed.

### Tooling

Added `Meziantou.Analyzer 3.0.190` to every project (was `3.0.177` centrally) and fixed all analyzer warnings. Zero build warnings. `VendoredZSTD`/`VendoredZLib` style cleanups only — no codec behaviour change.

### NuGet Package

```
dotnet add package CHDSharp --version 1.4.1
```

Targets `net8.0`, `net9.0`, `net10.0`. Pure C# — zero native dependencies.

### CLI Binaries

Pre-built self-contained single-file executables (binary: `CHDSharp`):

| Binary | Platform | Architecture |
|--------|----------|-------------|
| `CHDSharp_win-x64_v1.4.1.zip` | Windows | x64 |
| `CHDSharp_win-arm64_v1.4.1.zip` | Windows | ARM64 |
| `CHDSharp_linux-x64_v1.4.1.zip` | Linux | x64 |
| `CHDSharp_linux-arm64_v1.4.1.zip` | Linux | ARM64 |
| `CHDSharpTester_win-x64_v1.4.1.zip` | Windows | x64 |
| `CHDSharpTester_win-arm64_v1.4.1.zip` | Windows | ARM64 |

---

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

### Full chdman battle-test parity (2611/2611)

The new `CHDSharpBattleTest` harness exhaustively cross-checks the CHDSharpLib **decoder**
and the CHDSharp.Encoder **encoder** against MAME's `chdman.exe` on a deterministic corpus
of raw and CD images — `chdman create*`, `verify`, `info`, extract parity, and **byte-identical
encode** checks for every writable codec, plus delta/parent, CD, and A/V laserdisc scenarios.
The full suite passes **2611/2611 checks** (synthetic corpus); a real-world scan of 56 CHDs
passes **3003/3003 checks**. It can also scan real-world `*.chd` folders
(`--real <dir>`) to battle-test any collection. See [Testing](testing.md).

### Byte-for-byte parity with chdman for all codecs

Every encoder output now matches MAME's `chdman` byte-for-byte, closing the three
previously non-exact paths:

- **`createld` (laserdisc AVHuff)** — fixed an off-by-four AVCC `idx1` base offset that
  shifted audio by one sample and misaligned video; the AVHuff codec now correctly
  compresses zero-padded single-frame hunks; and the mono-FLAC encoder now evaluates
  every fixed-predictor order per frame (matching libFLAC 1.4.3's behaviour for the
  mono/48 kHz avhu path).
- **`cdzs` (CD Zstandard)** — the in-repo `VendoredZSTD` port (ZstdSharp 0.7.6 source, a C-to-C# port of the
  zstd 1.5.5 tree that MAME bundles) now emits frames byte-identical to C zstd for the
  same hunk buffers, replacing the previous NuGet `ZstdSharp.Port` dependency.
- **`zstd` (raw Zstandard)** — same fix as `cdzs`; the encoder's `zstd` output is now
  byte-identical to `chdman createraw -c zstd`.

### VendoredZSTD replaces ZstdSharp.Port NuGet dependency

The `ZstdSharp.Port` NuGet package has been replaced by an in-repo `VendoredZSTD` project
containing a full C-to-C# port of the zstd 1.5.5 source tree (the same version MAME
bundles). Both the encoder and decoder are included. Every codec is vendored in-repo
(no native dependencies); the only runtime NuGet dependencies are the optional
`Microsoft.Extensions.Logging.Abstractions` logging abstraction and `System.IO.Hashing`.

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

- **Peterson Fernandes** -- extended Gordon Jefferyes' original CHD reader into a full-featured library: encoder, CLI, NuGet package, VendoredZSTD, async/stream APIs, parent/child chaining, parallel verification and encoding, V5 compressed maps, DVD/GD-ROM/laserdisc support, 100% `chdman` byte-parity, and a comprehensive test suite
- **Gordon Jefferyes** -- original C# CHDSharp reader (RomVault)
- **MAME** -- CHD format specification and `chdman` reference implementation
- **libchdr** (Romain Tisseraud) -- C reference library
- **ZstdSharp.Port** (Oleg Stepanischev) -- pure C# Zstd port, vendored in-repo as `VendoredZSTD`
- **CUETools.Flake** (Grigory Chudov) -- FLAC encoder (LGPL 2.1)
