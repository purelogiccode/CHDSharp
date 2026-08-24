---
layout: default
---

# CHD creation (CHDSharp.Encoder)

The encoder is part of `CHDSharpLib` under the `CHDSharp.Encoder` namespace. It writes **CHD v5**
files from raw binaries and CD images (CUE/GDI/ISO/TOC/NRG), re-compresses existing CHDs
(`Copy`), creates differential (delta) children against a parent, and writes
uncompressed CHDs (`-c none`) — producing files that are **byte-for-byte identical to
`chdman`** for every writable codec (zlib, zstd, lzma, huff, flac, the four CD variants,
and `avhu` via `createld`), pass `chdman verify`, and extract back
identically via `chdman extractraw`. The library is **100% pure C#** (no native DLLs)
and runs identically on Windows and Linux.

Full API docs and project layout: see `CHDSharpLib/Encoder/`.


---

## Capabilities

| | |
|---|---|
| Raw encode | `ChdEncoder.EncodeRaw(source, chdPath, hunkBytes, unitBytes, codecTags, options)` |
| CD encode | `ChdEncoder.EncodeCd(cuePath, chdPath, hunkBytes, unitBytes, codecTags, options)` |
| Copy / re-compress | `ChdEncoder.Copy(sourceChd, chdPath, codecTags, options)` — any V1–V5 source, metadata cloned |
| Laserdisc encode | `ChdEncoder.EncodeLaserDisc(aviPath, chdPath)` — AVI → V5 laserdisc CHD (AVHuff: delta-RLE Huffman video + FLAC audio), interlace detection, VBI metadata capture, frame range selection |
| Laserdisc extract | `ChdEncoder.ExtractLaserDisc(chdPath, aviPath)` — V5 laserdisc CHD → AVI (YUY2 video + PCM audio), interlaced field assembly, frame range selection |
| Input formats | raw binary; CUE/BIN, GDI, ISO, TOC (cdrdao-style), NRG (Nero); AVI (YUY2/VYUY/UYVY + PCM); existing CHD files |
| Codecs | `zlib` (default), `zstd`, `lzma`, `huff`, `flac`, `cdzl`, `cdlz`, `cdzs`, `cdfl`, `none` — up to 4 per file, smallest output per hunk |
| Deduplication | SELF references (CRC/SHA-1 keyed), with SELF_0/SELF_1 map promotion |
| Delta (parent) CHDs | `ChdEncodeOptions.ParentPath` — COMPRESSION_PARENT refs, unit-split windows, chdman `-op` parity |
| Uncompressed CHD | `-c none` — V5 raw map, hunk-aligned raw data, zero hunks skipped, chdman byte-identical |
| Metadata | CHT2 (CD), CHGD (GD-ROM), GDDD (HDD), DVD entries, AVAV/AVLD (laserdisc), IDNT (ATA IDENTIFY), KEY (encryption), CIS (PCMCIA), checksummed, combined SHA-1 |
| CD audio | byte-swapped to big-endian (as stored on disc), tracks padded to 4-frame boundaries |
| Ratio logging | per-hunk callback (`ChdEncodeOptions.HunkCompleted`) — never changes output |

```csharp
using CHDSharp.Encoder;

ChdEncoder.EncodeRaw("game.bin", "game.chd");                       // raw, zlib
ChdEncoder.EncodeCd("game.cue", "game.chd");                        // CD, zlib
ChdEncoder.EncodeRaw("game.bin", "game.chd", 65536, 4096,
    ChdCodecs.ParseCodecTags("zlib,zstd,lzma"),
    new ChdEncodeOptions { HunkCompleted = p => Console.WriteLine(
        $"hunk {p.HunkIndex}/{p.HunkCount} {p.CodecName} {p.Ratio:P1}") });
ChdEncoder.Copy("old.chd", "new.chd", codecTags: [CodecTags.Zstd]); // re-compress
ChdEncoder.EncodeRaw("game.bin", "game.chd", 4096, 512,
    options: new ChdEncodeOptions { ParentPath = "base.chd" });     // delta child
ChdEncoder.EncodeRaw("game.bin", "game.chd", codecTags: [CodecTags.None]); // uncompressed
```

Callbacks fire in hunk order and are purely observational — encoding with a callback
produces byte-identical output to encoding without one.

---

## Validation

The encoder is validated against `chdman.exe` v0.288 and the CHDSharpLib reader
(434 tests per target framework in `CHDSharpEncoderTest`):

- `chdman info` reports the file without errors; `chdman verify` passes (raw + overall SHA-1).
- `chdman extractraw` of encoder output is byte-identical to the source (raw) and to
  `chdman createcd` output on the same CUE/BIN (CD).
- For repeated/alternating corpora the encoder's CHD files are **byte-for-byte identical
  to `chdman createraw -c zlib`** — deduplication and map encoding match MAME exactly.
- `-c none` output is **byte-for-byte identical to `chdman createraw -c none`** (including
  zero-hunk skipping), and `chdman verify` (exit 0) + `extractraw` round-trip it.
- `Copy` outputs pass `chdman verify` and extract byte-identically (standalone, child-source,
  and delta-child variants).
- Delta children made from chdman-made parents pass `chdman verify -ip` and byte-identical
  `extractraw -ip`.
- **`cdzs` is byte-identical to chdman**: the in-repo `VendoredZSTD` port (a C-to-C# port of
  the zstd 1.5.5 tree that MAME bundles) emits the same frame bytes as C zstd for the same
  hunk buffers, so the old "managed zstd trailing byte" caveat is gone.
- **`createld` output is byte-identical to `chdman createld`**: the AVI reader, AVHuff
  encoder, and the mono-FLAC audio path (exhaustive per-frame subframe search) all match
  MAME byte-for-byte, so laserdisc CHDs round-trip exactly.
- **100 MB+ integration tests** (`LargeFileValidationTests`) encode 100 MB raw and
  ~100 MB CD images, then check `chdman verify`, `extractraw` SHA-1 vs. the source, and a
  deep CHDSharpLib `CheckFile`:

```bash
dotnet test CHDSharpEncoderTest/ --filter "FullyQualifiedName~LargeFileValidationTests"
```

---

## Performance

Encoding runs a **producer→worker→consumer pipeline** (`HunkProcessor.CompressAll`, the
same shape as the library's parallel `CheckFile`): a single producer reads the raw hunks
and maintains the running raw SHA-1, `N` workers (default `Chd.TaskCount`, 1–64, override
via `ChdEncodeOptions.TaskCount` or CLI `-t`) hash and compress each hunk with private,
persistent codec instances, and a single consumer writes blocks and map entries strictly
in hunk order. Every codec is deterministic and dedup/offset assignment stays sequential,
so the worker count can never change the output bytes (`ParallelEncodeTests` asserts
byte-identical output across task counts).

Measured on a 24-core machine (512 MB mixed corpus, zlib): **5.1× faster with 8 workers**
vs. 1 (5.0 s → 0.98 s, identical 179 MB output).

For tuning and measurement today:

- `ChdEncodeOptions.TaskCount` (or CLI `-t N`) controls the worker count per encode; the
  default follows `Chd.TaskCount`, the same knob that tunes parallel verification.
- Per-hunk compression-ratio logging (`ChdEncodeOptions.HunkCompleted`, CLI `-v`).
- Memory is bounded: raw hunks and compressed results circulate through fixed-size pools
  sized by the worker count, so multi-GB sources encode without proportional RAM growth.

## CLI

```bash
CHDSharp createraw -o out.chd -i in.bin [-c zlib,zstd,lzma,none] [-hs 65536] [-us 4096] [-np 8] [-op parent.chd] [-tp id] [-d] [-v]
CHDSharp createcd -o out.chd -i in.cue [-c zlib,zstd,lzma,none] [-hs N] [-us N] [-np 8] [-op parent.chd] [-v]
CHDSharp createhd -o out.chd [--size N | -i in.img] [-c zlib,zstd,lzma,none] [-hs N] [-us N] [-chs C,H,S] [-ss N] [--ident ident.bin] [-np 8] [-v]
CHDSharp createdvd -o out.chd -i in.iso [-c lzma,zlib,huff,flac] [-hs N] [-np 8] [-op parent.chd] [-v]
CHDSharp createld -o out.chd -i in.avi [-c avhu] [-isf N] [-if N] [-hs N] [-np 8] [-v]
CHDSharp extractld -o out.avi -i in.chd [-isf N] [-if N]
CHDSharp extractraw -o out.bin -i in.chd
CHDSharp extractcd -o out.cue -i in.chd
CHDSharp extractdvd -o out.iso -i in.chd
CHDSharp listtemplates
CHDSharp copy -o out.chd -i in.chd [-c zlib,zstd,lzma,none] [-np 8] [-ip parent.chd] [-op parent.chd] [-v]
```

All commands deep-verify the result with CHDSharpLib before exiting.

## Status

All chdman-reachable features are implemented: all 10 writable codecs (including `avhu`
via `createld`/`extractld`), NRG/GDI/ISO/TOC/CUE input, AVI input/output for laserdisc,
predefined HDD geometry templates (`--listtemplates`, `-tp <id>`), metadata editing
(`SetMetadata`/`DeleteMetadata` + CLI `addmeta`/`delmeta`), IDNT/KEY/CIS metadata,
CUE style conversion / Redump matching (`CueConverter`), platform detection with smart
codec presets (`-c auto`), and byte-exact map clipping parity. `createld` output is now
**byte-for-byte identical to `chdman createld`** — the AVI reader, AVHuff encoder, and
mono-FLAC audio path all match MAME exactly — and `cdzs` output is byte-identical to
chdman's too (the vendored in-repo zstd port matches C zstd frames). `extractld` decodes
laserdisc CHDs back to playable AVI files. No encoding-level parity gaps remain.
