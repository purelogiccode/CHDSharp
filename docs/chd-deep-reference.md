---
layout: default
---

# CHD Deep Reference

> **Source:** This page is the audited and expanded form of [`References/CHDInfo.md`](https://github.com/purelogiccode/CHDSharp/blob/main/References/CHDInfo.md), which was derived from the MAME 0.289 sources (`References/mame-mame0289/src/lib/util/chd.{h,cpp,codec.*}`) and cross-checked against CHDSharp's reader (`CHDSharpLib/CHDHeaders.cs:13`, `CHDSharpLib/CHDBlockRead.cs`, `CHDSharpLib/CHDMetaData.cs`) and encoder (`CHDSharpLib/Encoder/MapCompressor.cs`, `CHDSharpLib/Encoder/Huffman16_8.cs`). Audit notes are inline as **⚠ Correction**.

For the concise on-disk format, see [CHD Format Reference](chd-format.md). For codec internals, see [Codecs](codecs.md). For the write path, see [Encoder](encoder.md).

---

## 1. Overview

CHD (Compressed Hunks of Data) is MAME's lossless compressed disk image format, created by **Aaron Giles** in **March 2002** (MAME 0.59). Originally called "Compressed Hard Disk," it was designed to store arcade hard disk images with integrity verification. The format expanded to cover CD-ROMs (V3, 2003), laserdiscs (V4, 2009), and DVDs (2023), becoming the universal non-ROM media container for MAME.

A CHD file stores a logical disk image split into fixed-size blocks called **hunks**. Each hunk can be stored uncompressed, compressed with one of several codecs, deduplicated (`COMPRESSION_SELF` — copy from another hunk in the same file), or inherited from a parent CHD (`COMPRESSION_PARENT` — delta/incremental images).

**Key source files (MAME 0.289):**

| File | Purpose |
|------|---------|
| `chd.h` | V1–V5 header definitions, `chd_file` / `chd_file_compressor` |
| `chd.cpp` | Core I/O: `create`, `open`, `read_hunk`, `compress_continue`, `compress_v5_map`, `decompress_v5_map`, metadata, hashing |
| `chdcodec.h` / `chdcodec.cpp` | Codec tags, `chd_codec_list`, `chd_compressor_group::find_best_compressor` |
| `chdman.cpp` | CLI orchestrator (`do_create_*`) — not part of the on-disk format |

All multi-byte values on disk are **big-endian**. The file starts with the magic `MComprHD` (`0x4D436F6D70724844`).

---

## 2. Version history (V1 → V5)

### V1 (Legacy)

- **Header size:** 76 bytes (`chd.h:48`, `CHDHeaders.cs:143`)
- **Compression:** `CHDCOMPRESSION_NONE` (0) or `CHDCOMPRESSION_ZLIB` (1) (`chd.h:54`)
- **Map entries:** 8 bytes, packed `uint64_t` — **[44-bit file offset][20-bit length]** (`chd.h:60`)
- **Hashing:** MD5 of raw data + parent MD5 only
- **Hard-coded geometry:** `cylinders`, `heads`, `sectors` for HD images (512-byte sectors)
- **No metadata, no SHA1**
- **Tag:** `MComprHD`

### V1 → V2

- **Header grows to 80 bytes** — adds `seclen` (bytes per sector) at offset 76 (`chd.h:79`)
- Everything else identical to V1 (same compression types, same packed map)

### V2 → V3 (Major redesign)

- **Header:** 120 bytes (`chd.h:101`, `CHDHeaders.cs:292`)
- **Removed** hard-coded CHS fields; **added** `logicalbytes` (64-bit, `chd.h:94`), `metaoffset` (`chd.h:95`), `hunkbytes` (`chd.h:98`), plus SHA1 pair (`sha1`, `parentsha1`, `chd.h:99`)
- **Retained** MD5 for backward compatibility
- **New compression:** `CHDCOMPRESSION_ZLIB_PLUS` (type 2, `chd.h:107`) — **⚠ Correction:** CHDInfo described this as "zlib with mini/small-data optimization"; in MAME it adds secondary-codec support for CD audio (FLAC) — type `V34_MAP_ENTRY_TYPE_2ND_COMPRESSED` (6) is decompressed with the secondary codec (`CHDHeaders.cs:304` `InitSecondaryCodec`, `CHDBlockRead.cs:Compressiontype2Nd`).

- **New map (16 bytes per entry):**

```
[0-7]   uint64_t offset     file offset of data
[8-11]  uint32_t crc32      CRC-32 of uncompressed data
[12-13] uint16_t length_lo  lower 16 bits of compressed length  \
[14]    uint8_t  length_hi  upper  8 bits  — together a 24-bit length  (CHDHeaders.cs:331 reads [12] <<8 | [13] <<0 | [14] <<16)
[15]    uint8_t  flags      type (low nibble) | NO_CRC (0x10)
```

- **Entry types (flags & 0x0F, `chd.cpp:52`):** `0` invalid, `1` compressed (codec 0), `2` uncompressed, `3` mini (repeat 8-byte pattern), `4` self, `5` parent, `6` 2nd compressed (secondary codec).
- **Flag `0x10`** = `V34_MAP_ENTRY_FLAG_NO_CRC` — no CRC present; CHDSharp skips validation when set (`CHDHeaders.cs:339`).

### V3 → V4

- **Header shrinks to 108 bytes** (`chd.h:134`) — drops MD5; keeps SHA1 only
- **SHA1 semantics change (`chd.h:131`):**
  - `sha1` at offset 48: **combined** raw+metadata SHA1
  - `parentsha1` at offset 68: combined parent SHA1
  - **New** `rawsha1` at offset 88: raw data SHA1 only
- Same 16-byte map as V3; adds `CHDCOMPRESSION_AV` (type 3, `chd.h:142`) for laserdisc.

### V4 → V5 (current default)

- **Header grows to 124 bytes** (`chd.h:162`, `chd-format.md:40`)
- **Up to 4 codecs** identified by 4-char tags:

```
[16] compressors[0]  e.g. 'lzma'
[20] compressors[1]  e.g. 'zlib'
[24] compressors[2]  e.g. 'huff'
[28] compressors[3]  e.g. 'flac'   (all zero → uncompressed, including map)
```

- **Removed** single `compression` field — each hunk picks the best of the 4 via `find_best_compressor` (`chdcodec.cpp:707`)
- **Added** `mapoffset` (`chd.h:155`) — map is **not** at a fixed position; written at end of file
- **Added** `unitbytes` (`chd.h:158`) — sub-hunk granularity for parent references
- **No flags field** — `parentsha1 == 0` means standalone (`chd.h:164`)
- **Two map modes:**

#### V5 uncompressed map (4 bytes per entry)

```
[0-3] uint32_t offset   (CHDHeaders.cs:479 `offsetWord`)
```

`0` → take hunk from parent (or zero-fill if standalone); otherwise `fileOffset = offsetWord * hunkbytes` (`CHDHeaders.cs:503`). **⚠ Correction:** CHDInfo's diagram "Entry 0 = 1, Entry 1 = 2…" is illustrative; real offsets are byte offsets, and the map itself lives at `mapoffset` (typically right after the header for uncompressed images).

#### V5 compressed map — expanded entry (12 bytes, `chd.h:184`)

```
[0]     uint8_t  compression   codec 0–3 / NONE(4)/SELF(5)/PARENT(6)
[1-3]   UINT24   complength    compressed length (big-endian)
[4-9]   UINT48   offset        file offset (big-endian; for PARENT this is a unit index)
[10-11] uint16_t crc16         CRC-16-CCITT of uncompressed data
```

#### V5 compressed map — on-disk header (16 bytes, `chd.h:171`)

```
[0-3]   uint32_t length         compressed byte length of the bitstream payload
[4-9]   UINT48   datastart      file offset of the first data block
[10-11] uint16_t crc            CRC-16 of the **uncompressed** 12-byte-per-hunk map
[12]    uint8_t  lengthbits     bits per complength field
[13]    uint8_t  hunkbits       bits per self-reference field
[14]    uint8_t  parentunitbits bits per parent-unit field
[15]    uint8_t  reserved
[16+]            Huffman+RLE encoded map entries (bitstream)
```

#### V5 per-hunk types (live + pseudo, `chd.cpp:63`)

| Value | Name | Meaning |
|-------|------|---------|
| 0 | `TYPE_0` | codec #0 |
| 1 | `TYPE_1` | codec #1 |
| 2 | `TYPE_2` | codec #2 |
| 3 | `TYPE_3` | codec #3 |
| 4 | `NONE` | uncompressed (`length == hunkbytes`) |
| 5 | `SELF` | copy from another hunk in this file (`offset` = hunk index) |
| 6 | `PARENT` | copy from parent **unit** (`offset` = unit index) |
| 7 | `RLE_SMALL` | run of 3 repeats (total run 4) up to 18 repeats (run 19) → `[RLE_SMALL][count-3]` |
| 8 | `RLE_LARGE` | longer runs → `[RLE_LARGE][(count-19)>>4][(count-19)&15]`, up to 274 repeats per triplet, iterated |
| 9 | `SELF_0` | same as last `SELF` |
| 10 | `SELF_1` | last `SELF` + 1 |
| 11 | `PARENT_SELF` | same hunk as parent hunk `hunknum` |
| 12 | `PARENT_0` | same as last `PARENT` |
| 13 | `PARENT_1` | last `PARENT` + `units_per_hunk` |

> **⚠ Correction vs CHDInfo §5:** CHDInfo wrote runs `1–2` individually, `3–18` → `RLE_SMALL`, `19–290` → `RLE_LARGE`. MAME actually encodes **repeats beyond the first** (`chd.cpp:2130`). `RLE_SMALL` covers **4–19 total repetitions** (3–18 repeats), `RLE_LARGE` covers **20–275** per triplet (19–274 repeats, iterated for longer runs). CHDInfo's `290` upper bound is off by ~15. Promotion to `SELF_0/1` and `PARENT_*` happens **before** RLE (`chd.cpp:2098`), not after.

---

## 3. Codecs

### General codecs (any data, `chdcodec.h:156`)

| Tag | Name | Description |
|-----|------|-------------|
| `zlib` | Deflate | Raw deflate (no zlib header, `windowBits = -MAX_WBITS`, level 9) |
| `zstd` | Zstandard | Single-frame ZSTD (CHDSharp vendors pure-C# ZSTD) |
| `lzma` | LZMA | Headerless single-call LZMA; properties `lc=3, lp=0, pb=2`, dict = `hunkbytes` |
| `huff` | Huffman | Custom 8-bit static Huffman with delta-RLE pre-pass; tree itself Huffman-encoded (`HuffmanDecoder.cs`) |
| `flac` | FLAC | Raw FLAC frames, 16-bit signed, 1–2 channels, preceded by 1-byte endianness marker `'L'`/`'B'` (`Flac/`) |

### CD-specific codecs (`chdcodec.h:163`)

| Tag | Name | Description |
|-----|------|-------------|
| `cdzl` | CD Deflate | Split CD frames → sector data (zlib) + subcode (zlib); ECC/sync stripped and regenerated (`chd_cd_compressor<zlib,zlib>`) |
| `cdzs` | CD Zstandard | Sector data (zstd) + subcode (**zstd**, not zlib) (`<zstd,zstd>`) |
| `cdlz` | CD LZMA | Sector data (lzma) + subcode (zlib) (`<lzma,zlib>`) |
| `cdfl` | CD FLAC | FLAC for audio sectors, zlib for data sectors; sync-header handling |

> **⚠ Correction:** CHDInfo's "CD Codec Sub-processing" step 4 said subcode is always zlib; for `cdzs` the subcode codec is **zstd** (`chdcodec.cpp:542`). CD frame handling is per-track: sizes vary by track type (Mode1 2048, Mode1Raw 2352, Mode2 variants 2336/2048…, Audio 2352); the `2352 = 2048 + 304` split is the maximum, not universal.

### A/V codec

| Tag | Name | Description |
|-----|------|-------------|
| `avhu` | A/V Huffman | Laserdisc A/V: audio = FLAC or delta-Huffman, video = delta-RLE + Huffman (`CHDReadersAVHuff.cs`, `AvHuffCodec.cs`) |

See [Codecs](codecs.md) for CHDSharp's delegate table (`CHDReaders.cs:FindBlockReaders`) and per-codec notes.

---

## 4. Creation workflow (MAME `chdman` → CHDSharp `ChdEncoder`)

High-level `chdman createhd` flow, mapped to CHDSharp where applicable:

```
User: chdman createhd -i input.raw -o output.chd -hs 4096 -c lzma,zlib,huff,flac

1. chdman.cpp: do_create_hd()
   ├── open input file
   ├── parse compression string → [lzma, zlib, huff, flac]  (ChdEncoder.cs:ValidateCompression)
   ├── create chd_file_compressor subclass (CHDSharp: ChdEncoder.Encode* reads via ISource)
   └── create_output_chd() + compress_common()

2. chd_file::create() → create_common()  (chd.cpp: ~create_common, CHDSharp: ChdEncoder.CreateCommon)
   ├── validate: not open, 1–4 codecs, no gaps (NONE only trailing), hunkbytes % unitbytes == 0
   └── build 124-byte V5 header in memory (big-endian):

       [0-7]    "MComprHD"
       [8-11]   124
       [12-15]  5
       [16-19]  'lzma'   compressors[0]
       [20-23]  'zlib'   compressors[1]
       [24-27]  'huff'   compressors[2]
       [28-31]  'flac'   compressors[3]
       [32-39]  logicalbytes
       [40-47]  0        mapoffset (0 while writing compressed)
       [48-55]  0        metaoffset
       [56-59]  hunkbytes
       [60-63]  unitbytes
       [64-83]  rawsha1  (zeros, filled by set_raw_sha1)
       [84-103] sha1     (zeros, filled via metadata_update_hash)
       [104-123] parentsha1 (zeros or parent's sha1)

   ├── write header at offset 0, then parse back to init fields
   ├── if uncompressed: reserve map area at V5_HEADER_SIZE
   └── create_open_common() — allocate decompressors, raw map (hunkcount * 12, init 0xFF), compressed buffer, cache

3. compress_begin()  (chd.cpp, CHDSharp: MapCompressor.cs)
   ├── walking_parent = (parent != null)
   ├── reset hash maps (parent_map, current_map — CRC16 → SHA1 → index)
   ├── allocate work buffer: 256 hunks  (chd.h:568 WORK_BUFFER_HUNKS = 256) — ⚠ CHDInfo wrote 257
   ├── create chd_compressor_group per worker thread
   └── reset write cursor (datastart = header + map reservation)

4. compress_continue() loop:

   A. walking_parent: async_read → async_walk_parent — CRC16+SHA1 per **unit**, add to parent_map
   B. compressing: async_read → read_data()
      └── async_compress_hunk per hunk:
          ├── CRC16 + SHA1 of raw hunk
          ├── check current_map (dedup), then parent_map
          └── else find_best_compressor() — try each codec in order, keep shortest output

      flush loop:
          ├── SELF hit   → hunk_copy_from_self()
          ├── PARENT hit → hunk_copy_from_parent()
          └── else      → hunk_write_compressed() — append compressed bytes, write 12-byte raw map entry, add to current_map, update running SHA1 (compsha1)

   C. done: set_raw_sha1() → write rawsha1 at header offset 64; compress_v5_map() → Huffman+RLE map, write 16-byte map header at mapoffset, update header field at offset 40
```

### File layout after creation

```
[0]            V5 header (124 bytes)
[124]          compressed hunk data block 0 (variable length)
[...]          block 1 …
[...]          metadata linked list (if any)
[mapoffset]    compressed V5 map (16-byte header + bitstream) — at EOF
```

For uncompressed V5, `mapoffset == V5_HEADER_SIZE` and the map is `hunkcount * 4` bytes of offset words, followed by raw data (`CHDHeaders.cs:444`).

---

## 5. V5 compressed map encoding (detail, `chd.cpp:2071` + `CHDHeaders.cs:509`)

### Step 0 — Promote self/parent references (before RLE)

Consecutive `SELF` to `last_self` → `SELF_0`, to `last_self+1` → `SELF_1`; `PARENT` to same hunk as `hunknum` → `PARENT_SELF`, to `last_parent` → `PARENT_0`, to `last_parent + units_per_hunk` → `PARENT_1` (`chd.cpp:2098`). Only the residual unpromoted refs contribute to `max_self`/`max_parent` for bit-width sizing.

### Step 1 — RLE compress the type stream

Raw map's byte 0 per hunk is the compression type. Since the codec choice changes slowly, runs are RLE-encoded **as repeats beyond the first occurrence**:

- `count < 3` repeats (total run 1–3) — emit `count` copies of `lastcomp` directly (`chd.cpp:2138`)
- `3 ≤ count ≤ 18` repeats (run 4–19) — emit `[RLE_SMALL][count-3]` (`chd.cpp:2142`)
- `count > 18` — emit `[RLE_LARGE][(count-19)>>4][(count-19)&15]` for up to 274 repeats, looping (`chd.cpp:2148`)

### Step 2 — Huffman encode types

Huffman encoder for up to 16 symbols, max code length 8 (`chd.cpp:2083`). Tree is exported via `export_tree_rle` into the bitstream, then each RLE symbol is `encode_one` (`chd.cpp:2188`).

### Step 3 — Per-entry auxiliary data (inline, bitstream, `chd.cpp:2196`)

After the type stream, for each hunk in order:

- `TYPE_0..3`: `[lengthbits bits of complength][16 bits of CRC16]` — **offset is implicit** (`curoffset` accumulates `length`, seeded with `datastart`); not stored per-entry.
- `NONE` (4): `[16 bits of CRC16]` (`length == hunkbytes` implicit)
- `SELF` (5): `[hunkbits bits of hunk index]`
- `SELF_0/1`, `PARENT_SELF`, `PARENT_0/1` — no bits (value derived from `lastSelf`/`lastParent`)
- `PARENT` (6): `[parentbits bits of unit index]`

### Step 4 — Bitstream buffer quirk (small maps)

MAME sizes the buffer as `(8*16 + (12 + max(lengthbits+16, hunkbits, parentunitbits)) * hunkcount) / 8 + 1` **including** the 16-byte header (`chd.cpp:2176`). For tiny `hunkcount` this is smaller than `tree_rle + encoded types + auxiliary bits`. MAME's `bitstream_out` silently drops whole trailing bytes while `flush()` still counts them toward `length`; dropped bytes read back as zeroes, so when a dropped byte is non-zero the stored map's header CRC16 no longer matches and the file is unreadable — even by `chdman` (reproducible with a single-hunk `createraw -hs 65536` over random data). CHDSharp's `MapCompressor.cs` replicates the allocation and byte-accurate clipping when the result is well-formed, and falls back to a full-size buffer when clipping would corrupt the map. See [chd-format.md §4](chd-format.md#v5-compressed-map) and `MapCompressorTests.cs`.

### Step 5 — Header + CRC

Write the 16-byte header (`length`, `datastart`, `crc`, `lengthbits`, `hunkbits`, `parentunitbits`), then the bitstream payload. On read, CHDSharp reconstructs the 12-byte-per-hunk raw map and verifies `CRC16(rawmap) == header.crc` (`CHDHeaders.cs:678`).

---

## 6. Metadata system

### Metadata header (16 bytes, `chd.cpp:1594`)

```
[0-3]   uint32_t metatag    4-char tag (e.g. 'GDDD', 'CHTR', 'DVD ')
[4]     uint8_t  flags      bit 0: CHD_MDFLAGS_CHECKSUM (0x01)
[5-7]   UINT24   length     payload length (big-endian)
[8-15]  uint64_t next       file offset of next entry (0 = end)
```

`flags & CHD_MDFLAGS_CHECKSUM` means the entry participates in the overall SHA1 (`chd.cpp:1734`). Length is `0 … <16 MiB` (`chd.cpp:1545`); the historical minimum `≥1` is waived for the `DVD ` marker. chdman's `write_metadata(DVD_METADATA_TAG,0,"")` calls the `std::string` overload which passes `input.length() + 1`, so the actual payload written is one NUL byte (length 1). CHDSharp matches this behavior (`MetadataWriter.cs:BuildDvdMetadata`).

### Standard tags (`chd.h:212`)

| Tag | Meaning | Format |
|-----|---------|--------|
| `GDDD` | Hard-disk geometry | `"CYLS:%d,HEADS:%d,SECS:%d,BPS:%d"` (`HARD_DISK_METADATA_FORMAT`) |
| `IDNT` | ATA IDENTIFY | raw 512-byte response |
| `KEY ` | Hard-disk key | opaque |
| `CIS ` | PCMCIA CIS | opaque |
| `CHCD` | Legacy CD-ROM | binary track records |
| `CHTR` | CD-ROM tracks v1 | `"TRACK:%d TYPE:%s SUBTYPE:%s FRAMES:%d"` |
| `CHT2` | CD-ROM tracks v2 | adds `PREGAP`, `PGTYPE`, `PGSUB`, `POSTGAP` (`CDROM_TRACK_METADATA2_FORMAT`) |
| `CHGT` | Legacy GD-ROM | — |
| `CHGD` | GD-ROM tracks | `"TRACK:%d TYPE:%s SUBTYPE:%s FRAMES:%d PAD:%d PREGAP:%d PGTYPE:%s PGSUB:%s POSTGAP:%d"` (`GDROM_TRACK_METADATA_FORMAT`) |
| `DVD ` | DVD-ROM marker | empty (CHDSharp) — see note above |
| `AVAV` | A/V metadata | `"FPS:%d.%06d WIDTH:%d HEIGHT:%d INTERLACED:%d CHANNELS:%d SAMPLERATE:%d"` |
| `AVLD` | A/V laserdisc | packed VBI per frame |

### Overall SHA1 (V4+, `chd.cpp:1720`)

```
overall_sha1 = SHA1( rawsha1 (20 bytes) || sorted_concatenation( SHA1( tag[4] || payload ) for each entry with CHECKSUM ) )
```

Tag is written big-endian (`put_u32be` in the hash path). Entries are sorted by the SHA1 itself (`metadata_hash_compare`, `chd.cpp:1734`). CHDSharp recomputes this in `CHDMetaData.cs` / `ChdHashing.cs` and during verification (`verification.md`).

---

## 7. Hashing & integrity

| Hash | Scope | Used in |
|------|-------|---------|
| MD5 | raw data | V1–V3 (`md5`/`parentmd5` fields) |
| SHA1 (`rawsha1`) | raw decompressed data | V3–V5 (V3: `sha1` field is raw; V4+: `rawsha1` field, `chd.h:88`) |
| SHA1 (`sha1`) | combined | V4–V5: rawsha1 + checksummed metadata (see §6) |
| CRC32 | per-hunk | V3/V4 map entries (`CHDHeaders.cs:331`) |
| CRC16-CCITT | per-hunk + map | V5: per-hunk CRC in expanded map + CRC of the uncompressed map in the compressed-map header |

### Deduplication

Each hunk gets `(CRC16, SHA1)` per unit or per hunk (`hash_pair`, `chd.h:509`). Two maps are maintained while writing: `parent_map` (units of the parent) and `current_map` (hunks already written). Before compression, both are probed; a hit emits `SELF` or `PARENT` instead of compressed data (`compress_continue`, `chd.cpp: ~2724`).

CHDSharp's reader validates `CRC16`/`CRC32` after decompression unless `NO_CRC` (V3/V4) or the entry is `SELF`/`PARENT`/`ZERO` (`CHDBlockRead.cs`).

---

## 8. Parent/child (delta) CHDs

- A child stores only hunks that differ; identical hunks become `PARENT` references (`COMPRESSION_PARENT`, offset = **unit index**, not hunk index).
- `unitbytes` enables fine-grained diffs: e.g., `hunkbytes=4096`, `unitbytes=512` → 8 units per hunk.
- Child header stores parent's combined SHA1 in `parentsha1` (`chd.h:161`). `parentsha1 == 0` means standalone.
- **Unaligned PARENT reads:** when `unit % units_per_hunk != 0`, CHDSharp stitches two parent hunks (tail of hunk N + head of N+1, `CHDBlockRead.cs` unit-based path).
- When a `PARENT` entry is encountered and no parent was supplied, CHDSharp returns `Chderrrequiresparent` (`CHD.cs`). If the supplied parent's `sha1`/`rawsha1` doesn't match `parentsha1`, it returns `Chderrinvalidparent` (`CHDHeaders.cs:ValidateParent`).

See [Parent/Child CHDs](parent-child-chds.md).

---

## 9. Minimal valid CHD (writer's guide)

A valid V5 CHD needs only a header, a map, and hunks at the offsets the map describes; metadata is optional (`metaoffset == 0`). This is the **absolute simplest** well-formed file:

### 9.1 Simplest uncompressed, no metadata

```
┌─────────────────────────────────────────────────────────────┐
│ HEADER (124 bytes)                                          │
│  "MComprHD" | 124 | 5 | 0,0,0,0 | logicalbytes |           │
│  mapoffset = 124 | metaoffset = 0 | hunkbytes | unitbytes  │
│  rawsha1 = 0 | sha1 = 0 | parentsha1 = 0                    │
├─────────────────────────────────────────────────────────────┤
│ UNCOMPRESSED MAP (hunkcount * 4 bytes) at mapoffset         │
│  Each entry: uint32_t offsetWord                            │
│    0          → zero/parent hunk                            │
│    otherwise  → fileOffset = offsetWord * hunkbytes         │
│  (Data starts at mapoffset + hunkcount*4)                   │
├─────────────────────────────────────────────────────────────┤
│ RAW DATA (hunkcount * hunkbytes)                            │
└─────────────────────────────────────────────────────────────┘
```

CHDSharp's `uncompressed_v5_map` (`CHDHeaders.cs:454`) accepts this; `ValidateMapBounds` then checks `[offset, offset+length)` lies within the file.

### 9.2 Simplest compressed (single codec, e.g., zlib)

```
┌─────────────────────────────────────────────────────────────┐
│ HEADER (124 bytes)                                          │
│  "MComprHD" | 5 | ['zlib',0,0,0] | logicalbytes | 0 | 0    │
│  hunkbytes | unitbytes | sha1 = 0                           │
├─────────────────────────────────────────────────────────────┤
│ COMPRESSED HUNK 0 (variable)                                │
│ COMPRESSED HUNK 1                                           │
│ ...                                                         │
├─────────────────────────────────────────────────────────────┤
│ COMPRESSED MAP at mapoffset (16-byte header + bitstream)    │
└─────────────────────────────────────────────────────────────┘
```

### 9.3 Minimal C# recipe (compressed V5, single codec)

```csharp
// Phase 1 — write V5 header (124 bytes, big-endian, all SHA1s zero is valid)
WriteTag("MComprHD"); WriteU32Be(124); WriteU32Be(5);
WriteU32Be(CodecTag("zlib")); WriteU32Be(0); WriteU32Be(0); WriteU32Be(0);
WriteU64Be(logicalBytes); WriteU64Be(0L); // mapoffset placeholder
WriteU64Be(0L); // metaoffset
WriteU32Be(hunkBytes); WriteU32Be(unitBytes);
WriteZeros(20); // rawsha1
WriteZeros(20); // sha1
WriteZeros(20); // parentsha1

// Phase 2 — for each hunk, compress (e.g., raw Deflate), record (offset, complength, crc16)
for each hunk:
    compressed = DeflateCompress(hunk); // raw, no zlib header, windowBits = -15
    crc16 = Crc16Ccitt(hunk);
    if (compressed.Length < hunkBytes)
        WriteBytes(compressed), rawMap[h*12+0]=0, rawMap[1..3]=complength, offset=curoffset, crc16=crc16;
    else
        WriteBytes(hunk),       rawMap[h*12+0]=4 (NONE), offset=curoffset, crc16=crc16;

// Phase 3 — build raw map (hunkcount * 12, big-endian PutUInt24Be/PutUInt48Be/PutUInt16Be)

// Phase 4 — compress the map:
//   a. promote SELF/PARENT → SELF_0/1, PARENT_SELF/0/1 before RLE
//   b. RLE-encode types (see §5 Step 1)
//   c. size bitstream as (8*16 + (12 + max(lenbits+16,hunkbits,parentbits))*hunkcount)/8 +1 including header
//   d. Huffman tree RLE → encode types → auxiliary data per §5 Step 3
//   e. append to EOF; mapoffset = file length before append

// Phase 5 — patch header:
//   Seek(40); WriteU64Be(mapoffset);
//   rawsha1 = SHA1(rawData); Seek(64); WriteBytes(rawsha1);
```

**⚠ Minimal-simplification notes vs CHDInfo §9:**

- CHDInfo wrote `DVD` metadata as a single null byte; chdman actually writes via `std::string("") + NUL` = one NUL byte (length 1). CHDSharp matches this (`MetadataWriter.cs:BuildDvdMetadata`). Tests assert `Assert.Single` + `Assert.Equal(0x00, ...)` (`RawEncodeMetadataTests.cs:144`).
- CHDInfo wrote `If compressors[0] == 0 → uncompressed (including map)` correctly; CHDSharp enforces `IsValidCodec` on each non-zero slot (`CHDHeaders.cs:413`).
- `hunkbytes` must be `>0`, `≤ 128 MiB` in CHDSharp (`CHDHeaders.cs:13`, shared limit), and `hunkbytes % unitbytes == 0`; MAME's comment says "512k maximum" (`chd.h:157`) but the encoder validates the weaker `%` condition and CHDSharp allows larger hunks for testing. `mapoffset`, `logicalbytes`, and `totalhunks = ceil(logicalBytes / hunkBytes)` must be consistent.
- The uncompressed-map data start is **not** `124` unconditionally; it is `mapoffset + hunkcount*4`. Each `offsetWord` encodes `fileOffset / hunkbytes`, not `hunkIndex + 1`.
- Using `COMPRESSION_NONE` for every hunk (uncompressed data) still requires a **compressed** V5 map if any codec slot is non-zero; only `compressors == [0,0,0,0]` selects the uncompressed 4-byte map.
- Skipping `SHA1` (writing zeros) yields a valid file that simply fails `deep` verification (`Chderrcantverify`), which is allowed.

Libraries for a minimal C# writer: `System.IO.Compression.DeflateStream` (raw), a 20-line `Crc16Ccitt`, `System.Security.Cryptography.SHA1`, plus a Huffman encoder (~150 lines) and bitstream (~80 lines) for the map — or skip compressed maps entirely and emit an uncompressed image.

---

## 10. Glossary

| Term | Definition |
|------|------------|
| **Hunk** | Fixed-size block (e.g., 4096 bytes). A CHD splits the logical image into N hunks. |
| **Unit** | Subdivision of a hunk (e.g., 512 bytes for HD sectors). Fine-grained parent references. |
| **Map** | Array with one entry per hunk; tells where each hunk's data lives and how it is stored. |
| **Codec** | Algorithm compressing individual hunks; V5 supports up to 4, best chosen per hunk. |
| **`SELF`** | "This hunk is identical to hunk X in the same file" — deduplication. |
| **`PARENT`** | "This hunk is identical to parent unit X" — delta. |
| **`NONE`** | Stored uncompressed. |
| **`ZERO`** | Unallocated hunk in an uncompressed V5 image with no parent — reads as zeroes. |
| **Metadata** | Tagged blobs in a linked list (`GDDD`, `CHT2`, `AVAV`, `DVD `, …). |
| **`rawsha1`** | SHA1 of the entire raw (decompressed) image. |
| **`sha1`** | Combined SHA1: `SHA1(rawsha1 || sorted hashes of checksummed metadata)`. |

---

*Audit performed 2026-08-28 against `References/mame-mame0289` and CHDSharp `main` at `CHDHeaders.cs` / `chdcodec.h` / `chd.cpp`. Corrections above are byte-accurate to the cited lines.*
