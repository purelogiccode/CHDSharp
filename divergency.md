# CHDSharp vs chdman — known divergences

This document records every place where CHDSharp (library, CLI, and battle-harness tests)
does not behave byte-for-byte or exit-for-exit like MAME's `chdman` (reference:
`References/mame-mame0289/src/tools/chdman.cpp` and `src/lib/util/aviio.cpp`).

Status legend:

| Status | Meaning |
|---|---|
| Fixed | divergence eliminated; verified byte-identical / exit-parity by the battle suite |
| Known | real divergence remains; not (yet) asserted by the battle suite |
| Test-only | divergence is in the harness itself, not in shipped code |

---

## 1. `info` / `verify` accepted duplicate `-i` (Fixed)

**Where:** `CHDSharpCli/Program.cs`

**Why it diverged:** `Main` dispatched

```csharp
InfoTest(ParseInput(cmdArgs, 0), cmdArgs.Skip(1).ToArray());
```

`ParseInput` consumed the *first* `-i <file>` and dropped it from the option list, so the
strict per-command option validator (`InfoTest` / `VerifyTest`) only ever saw one `-i` and
never triggered chdman's "Multiple parameters of the same type" error. The battle harness
recorded the checks as a SKIP with "info duplicate not enforced" / "verify duplicate not
enforced by CLI (known divergence)".

**chdman behaviour:** `chdman info -i a.chd -i a.chd` prints the usage text and
`Error: Multiple parameters of the same type specified` and exits 1.

**Fix:** both commands now receive the full argument vector (`VerifyTest(cmdArgs)` /
`InfoTest(cmdArgs)`) and parse the input file themselves via `ParseInput(args, 0)`, so the
duplicate option walk sees every `-i`/`--input`. `InfoTest` also returns `bool` and the
dispatch maps it to the process exit code (`infoOk ? 0 : 1`), matching chdman's exit-1 on
any validation error instead of the old unconditional `return 0`.

**Verified:** `info duplicate -i -> error`, `verify duplicate -i -> error parity with
chdman` now PASS; `info invalid option`/`missing param`/`non-existent file` still PASS
with exit parity.

---

## 2. `createhd --size` suffix handling (Fixed)

**Where:** `CHDSharpCli/Program.cs` (`CreateHdTest`, `TryParseScanSize`)

**Why it diverged:** chdman parses `--size` with `sscanf("%I64u")` (chdman.cpp:2035), which
reads **leading digits and silently ignores any trailing characters**. CHDSharp used a
strict "whole string must be digits" parser (`TryParsePlainUlong`), so:

| input | chdman | CHDSharp (before) |
|---|---|---|
| `-s 512K` | 512 bytes (suffix ignored) | `Error: Invalid size specified` |
| `-s 1M` | 1 byte → "Data size 1 is not divisible by sector size 512" (exit 1) | `Error: Invalid size specified` (exit 0) |

Because of this, the battle test `createhd suffix handling 512K` could not produce a file
chdman could also produce and was recorded as SKIP (header read failed).

**Fix:** replaced the strict parser with `TryParseScanSize`, which scans forward over digit
characters and ignores the rest exactly like `sscanf`. `-s 512K`, `-s 1M`, `-s 1G` etc. now
produce byte-identical CHDs (or the identical "not divisible" error) as chdman.

**Verified:** `createhd suffix 512K (chdman quirk parity)` PASS (byte-identical; logical
size 2048).

---

## 3. `createhd` blank-image size vs guessed CHS (Fixed)

**Where:** `CHDSharpCli/Program.cs`, `CHDSharpLib/Encoder/MetadataWriter.cs`

**Why it diverged:** chdman's `createhd` for a blank image (no input file) computes
`guess_chs(filesize, sector_size)` (chdman.cpp:1119, called at 2095) and then creates the
CHD with `logical size = totalsectors * sector_size`, i.e. **rounded up** to the CHS
product. CHDSharp passed the raw `--size` straight through to `CreateBlank`, producing a
*different logical size* for small images:

| `--size` | chdman CHS / logical | CHDSharp (before) |
|---|---|---|
| 512 | 1×2×2 = 2048 | 512 |
| 1048576 | 4×16×32 = 1048576 | 1048576 (same) |

The metadata `GDDD` was also written from the guessed geometry so the CHD headers differed,
not just the size.

**Fix:** added a public `MetadataWriter.GuessChs(ulong totalBytes, uint bytesPerSector)`
helper (exact replica of chdman's `guess_chs`, preferring 63→2 sectors/track and 16→2
heads, looping the sector count until a valid split exists). The blank branch now computes
`cylinders * heads * sectors * sectorSize` from the guess and passes that to
`CreateBlank`, and the `GDDD` metadata is derived from the same guess.

**Verified:** `createhd blank --size 1M (none)` byte-identical; `createhd suffix 512K`
byte-identical with logical size 2048.

---

## 4. `createhd -op parent differential` battle test (Test-only, Fixed)

**Where:** `CHDSharpBattleTest/BattleHarness.CliFull.cs`

**Why it was skipped:** the test created its parent CHDs with `-s 1M`. Under chdman this
value is scanned as **1 byte** (see divergence 2), producing

```
Data size 1 is not divisible by sector size 512
Fatal error occurred: 1
```

so the reference parent could not be created and the check bailed with SKIP
("chdman parent failed"). With the earlier CHDSharp the CLI *created* a file (or an
error), so the two were also not comparable.

**Fix:** all `-1M` size usages in the createhd suite were changed to the plain value
`1048576` (which both tools accept and produce identical output), so the differential
child-vs-parent parity now actually runs: both parents produced, a random raw child is
created with `-op` for each implementation, and exit codes are compared.

**Verified:** `createhd -op parent differential` PASS (also duplicate `-s`, missing param,
invalid option, verbose, hunksize/np and slice checks).

---

## 5. `createld` full parity battle test (Fixed, plus real AVI writer bug)

**Where:** `CHDSharpBattleTest/BattleHarness.CliFull.cs`,
`CHDSharpLib/Encoder/AviWriter.cs`

**Why it was skipped:** the suite had no AVI input available — `Directory.GetFiles(_workDir,
"*.avi")` never matched anything, so every check after "createld missing input" was
replaced by an explicit SKIP (`no AVI available`).

**Fix (harness):** the MAME regression test trees under
`References/mame-mame0289/regtests/chdman/input/` ship two tiny laserdisc AVIs
(`createld_avi_uyvy_3_frames_no_audio/in.avi`, `createld_avi_yuv2_3_frames_no_audio/in.avi`).
`FindLdSampleAvi()` walks up to the repo root and uses the first one found (falling back to
any pre-existing `*.avi` under the battle work dir). The suite now runs full parity:

- `createld with AVI (CLI)`
- `createld AVHU parity (byte-identical)` — ours vs chdman, byte-for-byte
- `createld verify (ours via chdman)` — chdman validates our CHD
- `createld extract parity (extractld ours vs chdman)` — round-trip AVI byte-identical
- `-hs` alias, `-isf -if` slice, duplicate `-c` error parity

**Real bug uncovered:** enabling the round-trip test exposed that
`ExtractLaserDisc`/`AviWriter` did not reproduce chdman's AVI output. Differences:

| field | chdman (`aviio.cpp`) | CHDSharp (before) |
|---|---|---|
| video strh `fccHandler` | `'DIB '` (0x20424944) | `'YUY2'` |
| video strh `dwSuggestedBufferSize` | `width*height*4` | 0 |
| strh `dwQuality` | 10000 | 0 |
| strh `rcFrame` right/bottom | `width`/`height` | 0 |
| avih `dwFlags` | `AVIF_HASINDEX \| AVIF_ISINTERLEAVED` (0x110) at offset 12 (note: offset **12**, not 8) | `AVIF_HASINDEX` at offset 8 |
| avih `dwStreams` | `m_streams.size()` (0 for no audio, 2 for audio) | hard-coded 1 |
| per-stream `indx` reservation | fixed `24 + 16*MAX_AVI_SIZE_IN_GB/4` = 4120 bytes, rewritten as `'JUNK'` when empty | no reservation (max ~130 bytes strl) |
| video strf `biSizeImage` | `width*height*(depth+7)/8` in **uint32 math** (multiplication before division: 624*352*23/8 = 631488) | `width*height*2` = 439296 |
| audio strl | omitted entirely when `audio_channels == 0` | always written (empty audio stream) |
| finalize | `dwLength` patch **only when audio>0** | patched `_audioStrhLengthPos` unconditionally, which for no-audio wrote 4 zero bytes at file offset 0, destroying the `'RIFF'` fourcc |

Note the fixed reserved `indx` size makes the file *harder* than the 3-frame AVI needs,
which is intentional: chdman always reserves it (it later rewrites or JUNKs it), so
byte-parity requires the same reservation.

**Fix:** `AviWriter` now writes the exact chdman layout: `'DIB '` handler, quality/rcFrame
fields, correct avih flags offset and stream count, the fixed `indx`/`'JUNK'` reservation
per stream, the uint32-math `biSizeImage`, no audio strl when there is no audio, and the
conditional frame-count patch.

**Verified:** extractld output is byte-identical to chdman (`identical=True`, 1322320
bytes in the battle), and `createld` also produces byte-identical CHDs (376 bytes) from the
MAME regression AVI.

---

## Known remaining divergences (not yet closed)

These are real behavioural differences that the battle suite currently **tolerates** rather
than asserts; closing them would require the commands to report failure through their exit
code and/or the encoder to round the raw-input CHS like chdman.

1. **Create-command error exit codes.** When `createhd`/`createld`/`createcd`/`createdvd`
   hit a validation or I/O error they log `--createld failed`, `Error: ...` etc. and stop,
   but `Main` still returns 0 because the command handlers do not signal success/failure.
   chdman returns 1. (The `info`/`verify` commands were already converted to `bool` for
   this reason.) Example observed:
   `chdman createhd -s 1M` → exit1; `CHDSharp createhd -s 1M` → prints
   `Error: Data size 1 is not divisible by sector size 512` but exits 0.
   Battle checks only assert exit0 (success) or "exit>=1 or error text", so they pass.

2. **`createhd` with an input file (raw) - CHS rounding.** chdman derives the CHS geometry
   for a *raw-input* `createhd` from `guess_chs(input_size, sector_size)` and creates the
   CHD at `totalsectors * sector_size`, rounding sub-CHS-exact inputs up; CHDSharp's
   raw-input branch encodes at the input's exact byte length. For multi-MB inputs (the
   common case) the sizes coincide, but a raw file whose length is not a product of
   guessed CHS values would produce a different logical size and different CHD.**
   (The blank branch was fixed in divergence 3; this is the same rule applied to the
   input-branch.)**

3. **`createhd -c <codec>` for blank images.** chdman rejects any compression on a blank
   image (`Blank hard disk images must be uncompressed`); CHDSharp accepts and writes the
   hunk with the compressed codec (while still producing empty hunks). Not currently
   exercised by the battle suite.

4. **`info duplicate -i` etc. - command HELP text differs slightly** from chdman's (they
   share semantics and exit codes, but the exact usage-banner text is different).
   Intentional; not asserted.

---

## Battle suite status

```
TOTAL 2907 checks: 2907 passed, 0 failed, 0 skipped

Battle finished in ~178s. Result: ALL PASSED
```

The five former SKIPs (`info duplicate`, `verify duplicate`, `createhd suffix 512K`,
`createhd -op parent differential`, `createld no AVI sample`) are now regular PASS checks.
See `CHDSharpBattleTest/` for the harness and `battle-<timestamp>/report.txt` under the
test output directory for the per-check detail.






# CHDSharp 1.4.1 — Divergency Report

> **Date:** 2026-08-28
> **CHDSharp:** v1.4.1 (`CHDSharp.exe` `2240921 B`, `73c58f9`)
> **chdman:** v0.289 (`mame0289`)
> **Corpus:** 56 CHDs from `H:\CHDTest` (40 CD, 10 DVD, 3 HDD, 3 GD-ROM, 65 GiB logical)
> **Harness:** `CHDBattleTest` (`CSharp_BatchConvertToCHD\CHDBattleTest`)
> **Results:** `H:\CHDBattleResults_141\results.csv` (1177 rows, 56 files, 5h37m)
> **Previous:** `H:\CHDBattleResults\results.csv` (v1.4.0, 2026-08-26, 1177 rows, 56 files)

---

## 1. Executive Summary

v1.4.1 fixes **2 of 3** original discrepancies (D1 `extractcd`, D2 `createhd`) and introduces **2 new divergences**
(`copy:zstd` 18/56, `createdvd:zstd` 10/10). All 448 cross-verifications pass — **zero data loss**.
Remaining failures are container-level byte differences; decompressed data is always identical.

| Parity | v1.4.0 | v1.4.1 | Status |
|---|---|---|---|
| `extractraw` | 56/56 | 56/56 | preserved |
| `extractcd` | **0/43** | **43/43** | **FIXED (D1)** |
| `extractdvd` | 10/10 | 10/10 | preserved |
| `extracthd` | 3/3 | 3/3 | preserved |
| `createhd` | **0/3** | **3/3** | **FIXED (D2)** |
| `createcd:cdzl` | 25/43 | 25/43 | unchanged (D3) |
| `copy:zstd` | 56/56 | **38/56** | **REGRESSION** |
| `createdvd:zstd` | 10/10 | **0/10** | **REGRESSION** |
| verify (all 448) | 448/448 | 448/448 | preserved |

---

## 2. FIXED — D1: `extractcd` (43 CDs + 3 GD-ROMs)

**v1.4.0:** 0/43 parity — every CD `disc.bin` was different (CHDSharp wrote 2448-byte raw frames
including subcode; chdman wrote cooked 2352-byte sectors).

**v1.4.1:** 43/43 parity — `CHDSharp extractcd` now defaults to cooked output
(`CHDSharpCli/Program.cs:5746` `cooked=true`), matching `chdman extractcd` byte-for-byte.

**Spot check — `Akai Shizuku - The Legend of Heroes IV (Japan).chd`:**

| Tool | `disc.bin` size | SHA-256 |
|---|---|---|
| chdman 0.289 | 8,772,960 B | `7F8DAA307355...` |
| CHDSharp 1.4.1 | 8,772,960 B | `7F8DAA307355...` |
| CHDSharp 1.4.0 | 9,136,006 B | `0DD10E30CAAB...` |

**Fix:** `CHDSharpLib/CHDFile.cs:3505` `ExtractToDirectory(..., cooked: true)` now strips the
96-byte subcode tail and writes `track.DataSize` per frame. `--raw`/`--raw-frames` flag available
to restore old 2448-byte behavior.

---

## 3. FIXED — D2: `createhd` missing `GDDD` metadata (3 HDDs)

**v1.4.0:** 0/3 parity — every HDD CHD was exactly **51 bytes smaller** than chdman output because
the `GDDD` geometry metadata tag was missing.

**v1.4.1:** 3/3 parity — all three HDDs now byte-identical to chdman.

| File | v1.4.0 chdman | v1.4.0 CHDSharp | v1.4.1 both | hash12 |
|---|---|---|---|---|
| `pc98-542mb.chd` | 74,879,706 | 74,879,655 | 74,879,706 | `6684F91C` |
| `a6plus.chd` | 104,213,820 | 104,213,769 | 104,213,820 | `AB7F3425` |
| `dvp-0027a.chd` | 3,317,212,283 | 3,317,212,232 | 3,317,212,283 | `1BB46616` |

**Spot check — `a6plus.chd` (`chdman info`):**
```
=== chdman product ===  Metadata: Tag='GDDD' Index=0 Length=35 bytes  CYLS:2012,HEADS:16,SECS:32,BPS:512.
=== CHDSharp product === Metadata: Tag='GDDD' Index=0 Length=35 bytes  CYLS:2012,HEADS:16,SECS:32,BPS:512.
```

**Fix:** `CHDSharpCli/Program.cs` `createhd --input` path now sets `encodeOptions.AutoClassify = true`,
which invokes `MetadataWriter.BuildHardDiskMetadata()` to synthesize the `GDDD` tag from the raw
file size and sector size.

---

## 4. NEW — `createdvd:zstd` — 10/10 DVDs, CHDSharp exactly 1 byte smaller

**Severity:** Low — container-only, all verifications pass, `Data SHA1` identical.

**Symptom:** Every DVD product from CHDSharp is exactly **1 byte smaller** than chdman's output.

| File | chdman B | CHDSharp B | delta | chdman hash12 | CHDSharp hash12 |
|---|---|---|---|---|---|
| `Alpha Mission (USA) (Minis).chd` | 5,546,409 | 5,546,408 | -1 | `97372329` | `DBE22F7D` |
| `3,2,1... SuperCrash! (USA) (En,Fr,De,Es,It) (Minis).chd` | 17,834,660 | 17,834,659 | -1 | `B267F8D6` | `1EC10499` |
| `2D Adventures of Rotating Octopus Character, The (USA) (Minis).chd` | 21,916,934 | 21,916,933 | -1 | `3641CF67` | `0CAB028C` |
| `Ballistic (USA, Europe).chd` | 147,368,490 | 147,368,489 | -1 | `ED742238` | `E14F096A` |
| `ballistic (usa, europe) (2).chd` | 147,368,490 | 147,368,489 | -1 | `ED742238` | `E14F096A` |
| `Aedis Eclipse - Generation of Chaos (USA).chd` | 535,614,188 | 535,614,187 | -1 | `276E4ED1` | `91A1633A` |
| `Adventure Time - Explore the Dungeon...chd` | 961,052,468 | 961,052,467 | -1 | `3C98193D` | `408195CD` |
| `3rd Birthday, The (USA).chd` | 1,235,983,569 | 1,235,983,568 | -1 | `38BB0307` | `2B85F58A` |
| `50 Cent - Bulletproof (USA, Europe).chd` | 5,365,431,518 | 5,365,431,517 | -1 | `B3518611` | `6C97E5AB` |
| `Ace Combat - Assault Horizon (USA).chd` | 8,164,484,214 | 8,164,484,213 | -1 | `D570B008` | `3FE3B807` |

**Root cause:** `chdman info -v` on `Alpha Mission`:
```
=== chdman product ===   Metadata: Tag='DVD '  Index=0  Length=1 bytes  .
=== CHDSharp product ===  Metadata: Tag='DVD '  Index=0  Length=0 bytes
```

chdman writes the `DVD ` metadata tag with **length 1** (a single `0x00` null byte). CHDSharp
writes it with **length 0** (empty payload). This is the sole source of the 1-byte delta.

The comment in `MetadataWriter.cs:198` is **incorrect** — it claims chdman uses
`write_metadata(DVD_METADATA_TAG, 0, "")` (length 0), but chdman actually writes 1 byte.

**Fix — `CHDSharpLib/Encoder/MetadataWriter.cs:206`:**
```csharp
// CURRENT (wrong):
Payload = []

// FIX (match chdman):
Payload = [0x00]
```

**Verification after fix:**
```powershell
$w="$env:TEMP\opencode\chk"; $b="...\chdbattle\bin\Release\net10.0"
& "$b\chdman.exe" createdvd -i "$w\disc.iso" -o "$w\m.chd" -c zstd -f
& "$b\CHDSharp.exe" createdvd -i "$w\disc.iso" -o "$w\s.chd" -c zstd -f
& "$b\chdman.exe" info -v -i "$w\m.chd"   # expect DVD Length=1
& "$b\chdman.exe" info -v -i "$w\s.chd"   # expect DVD Length=1 after fix
# sizes should match exactly
```

---

## 5. NEW — `copy:zstd` — 18/56 CDs, CHDSharp smaller (−42 to −9,218 B)

**Severity:** Low — container-only, all verifications pass, `Data SHA1`/`overall SHA1` identical.
CHDSharp is **always smaller** (better compression).

**Note:** v1.4.0 was 56/56 byte-identical on `copy:zstd`. The vendored ZSTD port changed between
versions (new `VendoredZSTD` replacing old `ZstdSharp.Port` NuGet).

**Affected files (same 18 that fail `createcd:cdzl`):**

| File | chdman B | CHDSharp B | delta | chdman hash12 | CHDSharp hash12 |
|---|---|---|---|---|---|
| `Akira (Europe).chd` | 6,710,654 | 6,705,481 | -5,173 | `1B1B4FF4` | `D64A941F` |
| `Akai Shizuku - The Legend of Heroes IV (Japan).chd` | 7,786,153 | 7,777,295 | -8,858 | `B1101DA6` | `EB792DF6` |
| `actdesu.chd` | 59,727,001 | 59,724,481 | -2,520 | `0B637935` | `BB4E6047` |
| `two shot diary (japan).chd` | 75,084,690 | 75,084,440 | -250 | `35487EF9` | `F403209C` |
| `3 ninjas kick back (usa).chd` | 283,428,982 | 283,420,784 | -8,198 | `B21D64F3` | `74078CC9` |
| `Arcade Gears Vol. 2 - Gun Frontier (Japan).chd` | 299,254,233 | 299,245,337 | -8,896 | `BDB6511A` | `F515728E` |
| `3 count bout (1995)(snk)(jp-us)[fire suplex].chd` | 342,295,578 | 342,286,815 | -8,763 | `CDBEAB6E` | `8B1E63D4` |
| `Chiki Chiki Boys (Japan).chd` | 495,715,004 | 495,710,251 | -4,753 | `91C91562` | `41F39959` |
| `Akiko Gold (Japan).chd` | 600,982,794 | 600,975,095 | -7,699 | `F8B4E3C4` | `8D88612C` |
| `amateur teikyou cd-rom (japan) (dos-v-you) (nec pc-fxga).chd` | 493,930,175 | 493,928,324 | -1,851 | `F5EBC41E` | `6FE71446` |
| `17 bit - collection for amiga cdtv (europe) (disc a) (the early classics) (2).chd` | 489,203,087 | 489,193,869 | -9,218 | `AFF390F0` | `95774E54` |
| `17 Bit - Collection for Amiga CDTV (Europe) (Disc A) (The Early Classics).chd` | 489,203,087 | 489,193,869 | -9,218 | `AFF390F0` | `95774E54` |
| `Metal Slug 2 (World) (En,Ja).chd` | 633,423,918 | 633,414,838 | -9,080 | `FA13BD75` | `806693FE` |
| `Club 3DO - Station Invasion (USA).chd` | 506,735,464 | 506,735,422 | -42 | `7F04A450` | `18211206` |
| `Aero Dancing - Todoroki Taichou no Himitsu Disc (Japan).chd` | 582,459,028 | 582,457,463 | -1,565 | `41F06242` | `5E1D9EF3` |
| `imsa racing.chd` | 498,206,218 | 498,206,176 | -42 | `B42724C4` | `C766E95A` |
| `Addams Family, The (USA) (Disc 2).chd` | 501,117,560 | 501,108,763 | -8,797 | `BE385326` | `4FAD3DD2` |
| `4 wheel thunder (euro).chd` | 956,792,093 | 956,790,625 | -1,468 | `251E4C1A` | `518E4086` |

**Verification:** Every single product passes both `chdman verify` and `CHDSharp verify`:
- `copy:zstd:verify-chdman[chdman-product]` — 56/56 OK
- `copy:zstd:verify-chdman[chdsharp-product]` — 56/56 OK
- `copy:zstd:verify-chdsharp[chdman-product]` — 56/56 OK
- `copy:zstd:verify-chdsharp[chdsharp-product]` — 56/56 OK

**Root cause:** `CHDSharpLib/Encoder/ChdCodec.cs:109` `new Compressor(Compressor.MaxCompressionLevel)`
— the vendored `VendoredZSTD/Compressor.cs` C# port of `zstd 1.5.5` produces slightly different
(more compact) frame bytes than MAME's native C `libzstd` for the same input. The compressed hunk
bytes differ at the frame level, but decompress to identical data.

**Fix:** Audit `VendoredZSTD/Compressor.cs` against MAME's `src/lib/util/chd_codec_zstd.cpp`:
- Compare `ZSTD_compress2` parameters: compression level, `windowLog`, `hashLog`, `checksumFlag`,
  `.contentSizeFlag`, frame header format (single-segment bit, window descriptor).
- Single-hunk repro to isolate the diff:
  ```powershell
  $w="$env:TEMP\opencode\chk"; $b="...\chdbattle\bin\Release\net10.0"
  & "$b\chdman.exe" copy -i "H:\CHDTest\Akira (Europe).chd" -o "$w\m.chd" -c zstd -f -np 1
  & "$b\CHDSharp.exe" copy -i "H:\CHDTest\Akira (Europe).chd" -o "$w\s.chd" -c zstd -f -np 1
  & "$b\chdman.exe" info -v -i "$w\m.chd"
  & "$b\chdman.exe" info -v -i "$w\s.chd"
  # Compare per-hunk compressed sizes in the Hunks table
  ```

---

## 6. UNCHANGED — D3: `createcd:cdzl` — 18/43 CDs, CHDSharp smaller (−65 to −9,098 B)

**Severity:** Low — container-only, all verifications pass, `Data SHA1`/`overall SHA1` identical.
CHDSharp is **always smaller** (better compression).

**Note:** The exact same 18 files fail both `copy:zstd` and `createcd:cdzl`. This is because
`copy` uses the same zstd codec on the compressed hunk data, and `createcd:cdzl` uses the same
zstd codec (via `CdCompoundCodec`) on data tracks plus `VendoredFlac` on audio tracks.

**Affected files:**

| File | chdman B | CHDSharp B | delta | chdman hash12 | CHDSharp hash12 |
|---|---|---|---|---|---|
| `Akira (Europe).chd` | 5,612,666 | 5,608,689 | -3,977 | `22AC999B` | `4E75AEB0` |
| `Akai Shizuku - The Legend of Heroes IV (Japan).chd` | 6,787,676 | 6,779,957 | -7,719 | `215DC44E` | `6A45C403` |
| `actdesu.chd` | 40,133,215 | 40,131,862 | -1,353 | `AB220EB7` | `C063D518` |
| `two shot diary (japan).chd` | 61,281,261 | 61,281,196 | -65 | `D593A19F` | `2F044634` |
| `3 ninjas kick back (usa).chd` | 274,941,478 | 274,933,440 | -8,038 | `D28FB2FF` | `421FB8B9` |
| `Arcade Gears Vol. 2 - Gun Frontier (Japan).chd` | 296,853,701 | 296,844,843 | -8,858 | `B0A043B0` | `67F2F1B9` |
| `3 count bout (1995)(snk)(jp-us)[fire suplex].chd` | 342,980,096 | 342,971,305 | -8,791 | `1E478107` | `D8AAD2B8` |
| `Chiki Chiki Boys (Japan).chd` | 494,374,572 | 494,370,736 | -3,836 | `4B4AE51D` | `B5A65384` |
| `Akiko Gold (Japan).chd` | 600,408,757 | 600,400,956 | -7,801 | `3BAFC136` | `92875B7A` |
| `amateur teikyou cd-rom (japan) (dos-v-you) (nec pc-fxga).chd` | 428,953,865 | 428,953,095 | -770 | `9855DFCF` | `5A17255C` |
| `17 bit - collection for amiga cdtv (europe) (disc a) (the early classics) (2).chd` | 430,499,092 | 430,490,899 | -8,193 | `583CF8EB` | `5F4A55E5` |
| `17 Bit - Collection for Amiga CDTV (Europe) (Disc A) (The Early Classics).chd` | 430,499,092 | 430,490,899 | -8,193 | `583CF8EB` | `5F4A55E5` |
| `Metal Slug 2 (World) (En,Ja).chd` | 628,135,611 | 628,126,513 | -9,098 | `36B02726` | `34F055BF` |
| `Club 3DO - Station Invasion (USA).chd` | 516,884,812 | 516,884,728 | -84 | `AF8DE829` | `24224B4F` |
| `Aero Dancing - Todoroki Taichou no Himitsu Disc (Japan).chd` | 494,605,969 | 494,605,356 | -613 | `64176EBC` | `03E63839` |
| `imsa racing.chd` | 500,667,752 | 500,667,684 | -68 | `76145B92` | `47D5D3F7` |
| `Addams Family, The (USA) (Disc 2).chd` | 502,283,047 | 502,274,193 | -8,854 | `82ED51CE` | `CEF20075` |
| `4 wheel thunder (euro).chd` | 914,094,254 | 914,093,294 | -960 | `F1568D9C` | `5DF3F8BE` |

**Root cause:** Two vendored codecs contribute to the difference:

1. **`VendoredZSTD`** — same as `copy:zstd` above. Data tracks compressed via `CdCompoundCodec`
   (`CHDSharpLib/Encoder/CdCompoundCodec.cs:1`) select `zlib` or `zstd` per hunk; the zstd
   path uses the same `VendoredZSTD/Compressor` that diverges on `copy`.

2. **`VendoredFlac`** — audio tracks compressed via `FlacCodec`
   (`CHDSharpLib/Encoder/FlacCodec.cs:46`) use `VendoredFlac/Encoder/LibFlacEncoder` which
   performs an exhaustive subframe search (fixed predictor orders 0-4, LPC). MAME's native
   `libFLAC 1.4.3` may pick slightly different subframes for the same audio data, producing
   different (but equally valid) compressed bytes.

**Fix options:**

- **Option A (chase byte parity):** Audit `VendoredFlac/Encoder/LibFlacEncoder` against MAME's
  `libFLAC` to ensure identical subframe selection (fixed predictor coefficients, LPC precision,
  Rice partition limits). This is high effort.

- **Option B (accept, document — recommended):** Update `docs/encoder.md` to note that
  `createcd -c cdzl` on audio-bearing discs may produce smaller but logically identical output.
  Change the battle test harness to check `Data SHA1` equality instead of file-hash equality
  for `createcd:cdzl` parity.

---

## 7. Preserved — No regressions

| Battle | Parity | Notes |
|---|---|---|
| `extractraw` | 56/56 MATCH | Raw hunk decompression unchanged |
| `extractcd` | 43/43 MATCH | Fixed in v1.4.1 (was 0/43) |
| `extractdvd` | 10/10 MATCH | DVD extraction unchanged |
| `extracthd` | 3/3 MATCH | HDD extraction unchanged |
| `createhd:zstd` | 3/3 MATCH | Fixed in v1.4.1 (was 0/3) |
| `verify` (all 448) | 448/448 OK | Every product passes both `chdman verify` and `CHDSharp verify` |

---

## 8. Summary of required fixes

### Fix 1 — `createdvd` 1-byte delta (trivial)

**File:** `CSharp_CHDSharp\CHDSharpLib\Encoder\MetadataWriter.cs:206`
**Change:** `Payload = []` → `Payload = [0x00]`
**Impact:** All 10 DVDs become byte-identical to chdman
**Risk:** None — matches actual chdman behavior (verified via `chdman info -v`)

### Fix 2 — `copy:zstd` frame divergence (medium)

**File:** `CSharp_CHDSharp\VendoredZSTD\Compressor.cs`
**Change:** Audit `ZSTD_compress2` parameters against MAME's `chd_codec_zstd.cpp`:
  - Compression level (must be `ZSTD_maxCLevel()`)
  - `windowLog`, `hashLog`, `chainLog`
  - `checksumFlag` (must match MAME's setting)
  - `contentSizeFlag` (must be set for CHD framing)
  - Frame header encoding (single-segment bit, window descriptor size)
**Impact:** 18 CDs become byte-identical on `copy:zstd`
**Risk:** Medium — may require per-hunk diff against chdman output

### Fix 3 — `createcd:cdzl` divergence (hard)

**File:** `CSharp_CHDSharp\VendoredFlac\Encoder\LibFlacEncoder.cs` (FLAC subframe selection)
  and/or `CSharp_CHDSharp\VendoredZSTD\Compressor.cs` (same as Fix 2)
**Change:** Align FLAC exhaustive subframe search with MAME's `libFLAC 1.4.3`
**Impact:** 18 CDs become byte-identical on `createcd:cdzl`
**Risk:** High — requires deep audit of LPC predictor, Rice coding, and partition order

### Alternative to Fix 3 — Accept logical parity

**Change:** Update `docs/encoder.md` caveat, change battle test to check `Data SHA1` instead
of file SHA-256 for `createcd:cdzl`. No code changes to codecs.
**Impact:** No byte-level improvement, but accurate documentation and test coverage

---

## 9. Spot-check reproduction commands

```powershell
$b="C:\Users\HomePC\Dropbox\source\repos\CSharp_BatchConvertToCHD\CHDBattleTest\bin\Release\net10.0"
$w="$env:TEMP\opencode\chk"; mkdir $w -Force | Out-Null

# Fix 1 — createdvd 1-byte delta
$iso="$w\disc.iso"
& "$b\chdman.exe" extractdvd -i "H:\CHDTest\Alpha Mission (USA) (Minis).chd" -o $iso -f
& "$b\chdman.exe" createdvd -i $iso -o "$w\m.chd" -c zstd -f
& "$b\CHDSharp.exe" createdvd -i $iso -o "$w\s.chd" -c zstd -f
& "$b\chdman.exe" info -v -i "$w\m.chd"   # DVD Length=1
& "$b\chdman.exe" info -v -i "$w\s.chd"   # DVD Length=0 (before fix), should be 1 after

# Fix 2 — copy:zstd frame diff
& "$b\chdman.exe" copy -i "H:\CHDTest\Akira (Europe).chd" -o "$w\m.chd" -c zstd -f -np 1
& "$b\CHDSharp.exe" copy -i "H:\CHDTest\Akira (Europe).chd" -o "$w\s.chd" -c zstd -f -np 1
& "$b\chdman.exe" info -v -i "$w\m.chd"   # compare per-hunk compressed sizes
& "$b\chdman.exe" info -v -i "$w\s.chd"

# Fix 3 — createcd:cdzl FLAC/zstd diff
& "$b\chdman.exe" extractcd -i "H:\CHDTest\Akira (Europe).chd" -o "$w\disc.cue" -f
& "$b\chdman.exe" createcd -i "$w\disc.cue" -o "$w\m.chd" -c cdzl -f -np 1
& "$b\CHDSharp.exe" createcd -i "$w\disc.cue" -o "$w\s.chd" -c cdzl -f -np 1
& "$b\chdman.exe" info -v -i "$w\m.chd"
& "$b\chdman.exe" info -v -i "$w\s.chd"
```

---

*Generated from `H:\CHDBattleResults_141\results.csv` (1177 rows, 56/56 files, 2026-08-28 23:15).*

---

## 10. LZMA SDK version mismatch — root cause of `LzBinTree` divergence (Fixed, Battle-verified)

**Date:** 2026-08-29
**Status:** Fixed, battle-verified

### Finding

The vendored LZMA library (`VendoredLZMA/`) is based on **LZMA SDK 26.02** (2026-06-25), located at
`References/LZMA SDK/lzma2602/`. MAME's chdman uses **LZMA SDK 23.01** (2023-03-14), located at
`References/mame-mame0289/3rdparty/lzma/C/`.

The SDK 26.02 C# `GetMatches` in `LzBinTree.cs` uses a **fundamentally different algorithm** from
the C reference `Bt4_MatchFinder_GetMatches` in `LzFind.c`. The vendored C# code was already modified
from the SDK to match the C reference, but had a hash-update-order bug that caused the SON array
to diverge.

### Root cause (Fixed)

In `Bt4_MatchFinder_GetMatches` (LzFind.c:1208), the C reference updates ALL hash tables
(h2, h3, **and hv/main**) BEFORE calling `SkipMatchesSpec` on the fast-path exit
(LzFind.c:1225-1227). The C# code updated the main hash table AFTER the d2/d3 checks and
after `Skip(1)` returned.

When `Skip(1)` was called from the fast-path (full-length d2/d3 match), it read a stale
main hash entry for the next position. If the next position hashed to the same bucket,
Skip's tree walk saw a different match candidate, went LEFT instead of RIGHT at some nodes,
and produced zero right children in the SON array. This caused `GetMatches` at pos=2500 to
miss the (28, dist=2351) candidate.

### Fix

`VendoredLZMA/LZ/LzBinTree.cs:GetMatches`: moved `_hash[_fixHashSize + hashValue] = Pos;`
before the d2/d3 fast-path checks, matching the C reference hash-update order.

### SDK 26.02 algorithm (original C# `GetMatches`)

```csharp
// maxLen starts at 1 (kStartMaxLen)
UInt32 maxLen = kStartMaxLen; // = 1

// Direct hash2 check: just check first byte, record match length 2
if (curMatch2 > matchMinPos)
    if (_bufferBase[_bufferOffset + curMatch2] == _bufferBase[cur])
    {
        distances[offset++] = maxLen = 2;
        distances[offset++] = _pos - curMatch2 - 1;
    }

// Direct hash3 check: just check first byte, record match length 3
if (curMatch3 > matchMinPos)
    if (_bufferBase[_bufferOffset + curMatch3] == _bufferBase[cur])
    {
        if (curMatch3 == curMatch2)
            offset -= 2;  // deduplicate
        distances[offset++] = maxLen = 3;
        distances[offset++] = _pos - curMatch3 - 1;
    }
```

### C reference algorithm (`Bt4_MatchFinder_GetMatches` in LzFind.c)

```c
// maxLen starts at 3
maxLen = 3;

// d2/d3 fast-path with UPDATE_maxLen extension
if (d2 < mmm && *(cur - d2) == *cur)
{
    distances[0] = 2;
    distances[1] = d2 - 1;
    distances += 2;
    if (*(cur - d2 + 2) == cur[2])
    {
        // d2 extends to 3+, fall through to UPDATE_maxLen
    }
    else if (d3 < mmm && *(cur - d3) == *cur)
    {
        d2 = d3;
        distances[1] = d3 - 1;
        distances += 2;
    }
    else
        break;

    UPDATE_maxLen       // extend match byte-by-byte
    distances[-2] = maxLen;
    if (maxLen == lenLimit) { SkipMatchesSpec(...); MOVE_POS_RET }
    break;
}
```

### Key differences

| Aspect | SDK 26.02 C# | C reference (LzFind.c) |
|--------|-------------|----------------------|
| `maxLen` initial | `kStartMaxLen` (= 1) | 3 (for BT4) |
| hash2 handling | Direct: check byte[0], record len=2 | d2 fast-path: check byte[0], check byte[2] for extension |
| hash3 handling | Direct: check byte[0], record len=3 | d3 fallback from d2, or standalone d3 |
| hash4 update | Before hash2/hash3 checks | After hash2/hash3 checks |
| Deduplication | `if (curMatch3 == curMatch2) offset -= 2` | d2 overwritten by d3 (`d2 = d3`) |
| Match extension | None (tree walk extends from maxLen) | `UPDATE_maxLen` macro extends before tree walk |
| Tree walk threshold | Records tree matches > maxLen (1, 2, or 3) | Records tree matches > maxLen (3) |

### Impact

The SDK algorithm would NOT produce byte-identical output to chdman either — it uses a fundamentally
different match-finding strategy. The vendored code was correctly modified to use the C reference's
d2/d3 fast-path algorithm, but a hash-update-order bug in `GetMatches` caused the SON binary tree
to build differently from C, resulting in 13/976 LZMA hunks being 1 byte larger. **Fixed and
battle-verified (2026-08-29).**

### Trace evidence

Full MF trace comparison (2104 ReadMatchDistances entries for a 4096-byte hunk):
- First **2067 entries are byte-identical** between C and C#
- **Divergence at entry 2068 (matchfinder pos=2500):**
  - C: `pairs=4: 15@0 28@2351` — tree walk finds 28-byte match at distance 2351
  - C#: `pairs=2: 15@0` — tree walk misses the 28@2351 candidate
- **SON array at pos=2500:**
  - C: `p2499=[2498,158]` — right child = 158 (enables walk to reach distance 2351)
  - C#: `p2499=[2498,0]` — right child = 0 (walk exits immediately)
- Right children in C follow pattern `right = pos - 2351` for positions 2480–2498
- These right children were set by Skip calls between positions 2228–2499

### Next steps

1. ✅ Run the battle suite to verify byte-identical LZMA output — **DONE** (2026-08-29,2935 checks, all LZMA tests pass,4 real CHDs verified)
2. Update battle suite to assert LZMA hunk-level equality (not just decompressed-data equality) — optional, low priority
