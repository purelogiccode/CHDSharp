# CHDSharp Release Notes

## Unreleased

### Byte-for-byte parity with chdman for all codecs

Every encoder output now matches MAME's `chdman` byte-for-byte, including the three
previously non-exact paths:

- **`createld` (laserdisc AVHuff)** — fixed an off-by-four AVI `idx1` base offset that
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

### Bug fixes

- Fixed `AviReader` `idx1` chunk offset base: offsets are now correctly relative to the
  `'movi'` fourcc (matching MAME's `aviio.cpp` `parse_idx1_chunk` base), fixing audio/video
  misalignment in all AVI-based laserdisc encoding.
- Fixed `AvHuffCodec.Compress` rejecting zero-padded single-frame hunks: hunks whose
  trailing bytes are all zeroes (the common case for interlaced laserdisc fields with fewer
  samples than the maximum) are now correctly compressed instead of stored raw.

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
