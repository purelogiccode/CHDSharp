---
layout: default
---

# Architecture

This page describes the solution layout and the internal design of the library.

---

## Solution layout

| Project | Kind | Purpose |
|---------|------|---------|
| `CHDSharpLib` | Library | The CHD reader and encoder (NuGet package `CHDSharp`). Everything in this wiki's API pages lives here. The encoder subsystem (`CHDSharp.Encoder` namespace) creates V5 CHDs from raw binaries and CD images (CUE/GDI/ISO/TOC/NRG) with `chdman`-matched output — CRC16, SHA1, Deflate wrappers, V5 map compressor (`MapCompressor`, `Huffman16_8`), V5 header writer, all 10 writable codecs (zlib/zstd/lzma/huff/flac/cdzl/cdlz/cdzs/cdfl + avhu + none), SELF dedup, delta parents, CHT2/CHGD/GDDD/DVD metadata. 100% pure C#. See [Encoder](encoder.md). |
| `CHDSharpCli` | Console | CLI CHD manager (binary: `CHDSharp`). Full `chdman` subcommand parity. |
| `CHDSharpTest` | xUnit | Unit + corpus tests (602 tests, 30 CHD fixtures). |
| `CHDSharpTestGen` | Console | Deterministic corpus generator driving vintage `chdman`/`hdcomp` binaries. |
| `CHDSharpTester` | WPF | Interactive batch verification, cross-checked against `chdman`. |
| `CHDSharpEncoderTest` | xUnit | Encoder tests (434) with chdman cross-validation. |
| `CHDSharpBattleTest` | Console | Battle harness: 2611/2611 (deterministic) + 3003/3003 (real-world) checks vs `chdman` on CHD corpora. |


All projects share versioning and analyzer settings via `Directory.Build.props`.

---

## Library architecture

```
┌────────────────────────────────────────────────────────────┐
│                      Public API                             │
│   Chd (static)              ChdFile (instance)             │
│   CheckFile · CheckHeader   Open · ReadHunk · Read ·         │
│   CheckFileWithParent ·     ReadSector · ReadFrame ·        │
│   IsChdFile · Classify      Precache · GetMetadata ·        │
│                             Metadata · Tracks · Extract     │
├────────────────────────────────────────────────────────────┤
│  CHDHeaders        Parse V1–V5 headers + all map formats    │
│  CHDBlockRead      Dispatch a map entry → codec delegate    │
│                     + repeat-block caching + CRC checks     │
│  CHDReaders        The 10 decompression delegates           │
│  CHDReadersAVHuff  AVHuff delegate (audio + video)          │
│  CHDMetaData       Metadata chain traversal + SHA1 hashing  │
│  ChdTocParser      CD/GD-ROM track (TOC) parsing            │
├────────────────────────────────────────────────────────────┤
│  Utils/  CRC · CRC16 · BitStream · HuffmanDecoder ·         │
│          HuffmanDecoderRLE · BigEndian · ArrayPool ·        │
│          cdRom · CdRomAddress (MSF↔LBA)                     │
│  LZMA/   LzmaStream · LzmaDecoder · RangeCoder · LzOutWindow│
│  Flac/   AudioDecoder · FlacFrame · BitReader · LPC · ...   │
│  Models/ ChdCodecState · ChdHeader · MapEntry · records     │
├────────────────────────────────────────────────────────────┤
│  VendoredZSTD (project, pure C#)                            │
└────────────────────────────────────────────────────────────┘
```

### Layer responsibilities

**`Chd` (static)** — verification entry points (`CheckFile`, `CheckFileWithParent`), header sniffing (`CheckHeader`, `IsChdFile`), full header DTO reads (`ReadHeader`, `ReadHeaderAsync`), classification (`Classify`), plus global settings (`LoggerFactory`, `TaskCount`).

**`ChdFile` (instance)** — random access. Owns the stream (optionally), the parsed `ChdHeader`, a `ChdCodecState`, an optional parent `ChdFile`, and lazy metadata/tracks caches. All `Read*`/`Extract*`/`Generate*` operations live here. Also supports `OpenAsStream()` for seekable stream access, `ConfigureReadAhead()` for background pre-decompression, and `ParentResolver` for lazy parent resolution.

**`ChdHeaders`** — reads and validates each version's header, then parses the map:

- V1/V2: 8-byte packed entries with self-hunk dedup detection.
- V3/V4: 16-byte entries (CRC32, length, flags incl. NO_CRC).
- V5: uncompressed map (4-byte entries) or compressed map (16-byte header + Huffman/RLE bitstream), including the RLE/promotion pseudo-type expansion.

**`ChdBlockRead`** — per-hunk dispatch. Handles:

- compressed hunks (codec slot 0–3 or secondary codec),
- uncompressed, mini (8-byte pattern repeat), zero, self, and parent hunks,
- decompressed-block caching for repeated (self-referenced) blocks,
- CRC16/CRC32 validation after decompression.

**`ChdReaders` / `ChdReadersAVHuff`** — the codec delegates (see [Codecs](codecs.md)).

**`ChdMetaData`** — walks the metadata linked list (cycle-guarded, 1 MiB entry cap), exposes tags/data/flags, and computes the per-entry hashes used by the combined-SHA1 verification.

**`ChdTocParser`** — parses CD/GD-ROM track metadata in all variants (`CHT2`, `CHTR`, `CHGD`, `CHGT`, binary `CHCD`) into `ChdTrackInfo` records.

### Utility stack

| Utility | Used for |
|---------|----------|
| `CRC` / `CRC16` | CRC32 (V3/V4 map) and CRC16-CCITT (V5 map, hunk checks) |
| `BitStream` | Bit-level reading for Huffman codecs and the V5 map |
| `HuffmanDecoder` / `HuffmanDecoderRLE` | `huff` codec, AVHuff audio/video trees, V5 map decoding |
| `BigEndian` | Big-endian primitive readers/writers |
| `ArrayPool` | Bounded buffer pooling for parallel verification |
| `cdRom` | CD frame constants, ECC generation, TOC helpers |
| `LZMA/` | Self-contained LZMA SDK decoder port |
| `Flac/` | Self-contained FLAC decoder (frames, subframes, LPC, Rice) |

---

## Data flow

### Open

```
ChdFile.Open(path/stream, parent?)
  → CheckHeader (magic + version)
  → ReadHeaderV1..V5  (validate, read codec slots, hashes, geometry)
  → ValidateSizeLimits
  → ValidateParent (if child: compare parentmd5/parentsha1)
  → FindBlockReaders (codec → delegate table)
  → LinkSelfBlocks   (resolve SELF map entries)
  → ChdFile instance (lazy metadata/tracks, codec state)
```

### Read a hunk

```
ReadHunk(n)
  → map entry n
  → PARENT?  → read from parent (direct index or unit-based, possibly
               stitching two parent hunks)
  → SELF?    → follow to the real entry (cached result if repeated)
  → read compressed bytes from stream (or from the Precache buffer)
  → ChdBlockRead.ReadBlock:
       mini → repeat 8-byte pattern
       none → raw copy
       codec → delegate (reusable ChdCodecState)
  → CRC check (CRC32 V3/V4, CRC16 V5) unless NO_CRC/zero/self/cache-hit
```

### Parallel verification

`Chd.CheckFile(deepCheck: true)` runs a producer/worker/hasher pipeline:

```
producer      reads compressed hunks from the stream (BlockingCollection)
workers (N)   decompress hunks in parallel (ChdBlockRead), bounded by a
              semaphore to a ~512 MiB repeat-block cache budget
hasher        consumes decompressed hunks in order, feeds MD5/SHA1,
              validates per-hunk CRCs, releases buffers back to the pool
```

The number of workers is `Chd.TaskCount` (default 8, range 1–64); it must be set before calling `CheckFile`. `CheckFileWithParent` is the single-threaded variant that supports parent/child chains.

---

## Threading model

| Component | Thread safety |
|-----------|---------------|
| `Chd` static settings (`TaskCount`, `LoggerFactory`) | Mutable globally; snapshot per operation. Change before concurrent calls. |
| `ChdFile` instance | **Not thread-safe.** Shared stream + shared buffers; serialize all calls on one instance. |
| Multiple `ChdFile` instances | Safe to use in parallel on separate streams (including parent sharing when all access is single-threaded per instance). |
| `CheckFile` internals | Self-synchronized (locks, interlocked counters, `BlockingCollection`, semaphore). |

---

## Design notes

- **Reads and writes in one library** — `ChdFile` is a read-only random-access reader, while the encoder subsystem (`CHDSharp.Encoder`, e.g. `ChdEncoder.EncodeRaw`/`EncodeCd`/`Copy`) creates and re-compresses CHDs; both live in the same `CHDSharp` package.
- **Reusable scratch** — `ChdCodecState` keeps LZMA windows, zstd decompressors, FLAC decoders, and Huffman tables alive across hunks, which is the main reason sequential reads stay fast.
- **Error contract** — public APIs return `ChdError` codes rather than throwing (exceptions are caught at the `ReadHunk` boundary, logged, and mapped). See [Error Codes](error-codes.md).
- **Lazy loading** — metadata and tracks are parsed on first access and cached; `Open` stays cheap for header-only callers.
