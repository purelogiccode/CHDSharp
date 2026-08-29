---
layout: default
---

# CHDman Parity — Battle Test Results

Every CHDSharp feature (library **decoder**, `CHDSharp.Encoder` **encoder**, and the `CHDSharp` **CLI**) is battle-tested head-to-head against MAME's `chdman.exe` (reference implementation, MAME 0.289). The tables on this page are the complete, current results of that battle — every suite, every check, and the exact claim it makes.

> **Latest result (dense corpus, seed 1337): 2907 / 2907 checks passed, 0 failed.** Run took ~207 s. All tables below reproduce deterministically via `CHDSharpBattleTest`.

```bash
# Reproduce everything on this page
dotnet run --project CHDSharpBattleTest

# Options: --quick (smoke), --seed <n>, --out <dir>, --cli <path> (default auto-resolve),
#          --chdman <path>, --real <dir> (battle-test a real CHD collection), --no-keep
```

The harness compares three layers on every input:

| Layer | What is compared | Guarantee |
|-------|------------------|-----------|
| `CHDSharpLib` (decode) | `Chd.CheckFile` / `CheckFileWithParent` (deep), `ReadAllBytes`, random-access `Read`, `ReadHunk`, `Chd.ReadHeader` vs `chdman info`/`verify` | Full decoder equivalence, byte-for-byte |
| `CHDSharp.Encoder` (encode) | `EncodeRaw` / `EncodeCd` / `Copy` vs `chdman createraw` / `createcd` / `copy` | **Byte-identical output files** — same headers, maps, and compressed payloads |
| `CHDSharp` CLI | every command and option vs `chdman` command line | Exit-code, output, and error-message parity (strict chdman validation) |

---

## Results overview

| Feature area | Suites | Checks | Result |
|--------------|-------:|-------:|:------:|
| Raw-image encoding (byte-identical) | 69 | 723 | ✅ 723/723 |
| CD-image encoding (byte-identical) | 21 | 210 | ✅ 210/210 |
| Delta (parent/child differential) | 1 | 19 | ✅ 19/19 |
| Copy / re-compression | 7 | 43 | ✅ 43/43 |
| Decoder (every asset, ours *and* chdman) | 190 | 1140 | ✅ 1140/1140 |
| Header info parity | 13 | 13 | ✅ 13/13 |
| CLI command battle suites | 30 | 759 | ✅ 759/759 |
| **TOTAL** | **331** | **2907** | ✅ **2907/2907** |

---

## 1. Raw-image encoding — byte-identical with `chdman createraw`

The encoder is run on 10 deterministic input profiles (see corpus table below) crossed with 9 hunk/unit/codec configurations. For every cell, **both files are compared byte-for-byte** (`cmp`-equivalent on the whole `.chd`), then both are verified (`chdman verify`), deeply checked (`Chd.CheckFile`), extracted, decoded, and header-compared.

**Cell legend:** `11/11` = all 11 checks passed for the pair · `5/5` = ours-only case (chdman rejects the config: input size not a multiple of the unit size) — encode + verify + deep check + extract + decode, no reference pair · `—` = not part of the corpus.

`hunk/unit` (codecs) → | `zlib`<br>4096/512 | `zstd`<br>4096/512 | `lzma`<br>4096/512 | `huff`<br>4096/512 | `flac`<br>4096/512 | `zlib,zstd,lzma`<br>4096/512 | `none`<br>4096/512 | `zlib`<br>65536/512 | `zlib`<br>4096/4096
---|---|---|---|---|---|---|---|---|---
**zeros** (512 KiB) | ✅ 11/11 | ✅ 11/11 | ✅ 11/11 | ✅ 11/11 | ✅ 11/11 | ✅ 11/11 | ✅ 11/11 | ✅ 11/11 | ✅ 11/11
**random** (1 MiB) | ✅ 11/11 | ✅ 11/11 | ✅ 11/11 | ✅ 11/11 | ✅ 11/11 | ✅ 11/11 | ✅ 11/11 | ✅ 11/11 | ✅ 11/11
**pattern** (1 MiB) | ✅ 11/11 | ✅ 11/11 | ✅ 11/11 | — | — | ✅ 11/11 | ✅ 11/11 | ✅ 11/11 | —
**mixed** (2 MiB) | ✅ 11/11 | ✅ 11/11 | ✅ 11/11 | ✅ 11/11 | ✅ 11/11 | ✅ 11/11 | ✅ 11/11 | ✅ 11/11 | ✅ 11/11
**repeated** (32 × 8 KiB) | ✅ 11/11 | ✅ 11/11 | ✅ 11/11 | ✅ 11/11 | ✅ 11/11 | ✅ 11/11 | ✅ 11/11 | ✅ 11/11 | ✅ 11/11
**text** (512 KiB) | ✅ 11/11 | ✅ 11/11 | ✅ 11/11 | ✅ 11/11 | ✅ 11/11 | ✅ 11/11 | ✅ 11/11 | ✅ 11/11 | ✅ 11/11
**pcm16** (512 KiB) | ✅ 11/11 | ✅ 11/11 | ✅ 11/11 | ✅ 11/11 | ✅ 11/11 | ✅ 11/11 | ✅ 11/11 | ✅ 11/11 | ✅ 11/11
**unaligned** (1 000 448 B) | ✅ 11/11 | — | — | — | — | — | ✅ 11/11 | ✅ 11/11 | —
**tiny1** (1 B) | ✅ 5/5 | — | — | — | — | — | ✅ 5/5 | — | ✅ 5/5
**tiny100** (100 B) | ✅ 5/5 | — | — | — | — | — | ✅ 5/5 | — | ✅ 5/5

**The 11 checks per raw pair** (in order):

| # | Check | Compares |
|---|-------|----------|
| 1 | `encode (ours)` | `ChdEncoder.EncodeRaw` succeeds and produces a file |
| 2 | `chdman createraw` | Reference file produced by chdman with identical args (skipped if chdman rejects the config) |
| 3 | `encode byte-identical` | **Every byte of the two `.chd` files is equal** |
| 4 | `chdman verify (ours)` | chdman verifies our file |
| 5 | `chdman verify (ref)` | chdman verifies its own file |
| 6 | `deep CheckFile (ours)` | `Chd.CheckFile(fs, path, deep: true)` succeeds on our file |
| 7 | `extract (ours)` | Our file extracts to the original input bytes |
| 8 | `extract (ref)` | chdman's file extracts to the original input bytes |
| 9 | `decode (ours)` | `ReadAllBytes` on our file equals the original input |
| 10 | `decode (ref)` | `ReadAllBytes` on chdman's file equals the original input |
| 11 | `info parity` | `Chd.ReadHeader` on our file matches `chdman info` (version, sizes, hunks, units, codecs, SHA1, data SHA1) |

The `5/5` tiny cases cover the first check plus 4, 6, 7, 9 (no reference pair exists).

**Corpus profiles** (deterministic — seed 1337):

| Input | Generated by | Purpose |
|-------|--------------|---------|
| `zeros` | `TestDataGenerator.Zeros` | All-zero image — tests uncompressed-hunk and dedup paths |
| `random` | `TestDataGenerator.Random` | Incompressible data — forces LZMA/huff/flac to emit raw-size hunks |
| `pattern` | `TestDataGenerator.Pattern` | Periodic byte patterns — tests repeated-substring compression |
| `mixed` | `TestDataGenerator.Mixed` | Interleaved zero/random/pattern regions — forces codec switching in multi-codec mode |
| `repeated` | `TestDataGenerator.RepeatedHunks(32, 8, 4096)` | 32 identical 8-KiB hunks — tests self-hunk dedup |
| `text` | `TestDataGenerator.Text` | Realistic text bytes — tests huff/flac on natural data |
| `pcm16` | `TestDataGenerator.Pcm16` | 16-bit PCM samples — the FLAC codec's intended input |
| `unaligned` | `TestDataGenerator.Random` | Size not a multiple of hunk or unit size |
| `tiny1` / `tiny100` | `TestDataGenerator.Random` | 1- and 100-byte images — minimal CHDs |

---

## 2. CD-image encoding — byte-identical with `chdman createcd`

The encoder is run on 3 CD profile inputs (mixed CD, audio-only CD, and a data ISO) crossed with 7 codec/hunk configurations. Same protocol as raw: **byte-identical `.chd` comparison**, dual verify, deep check, extract parity, dual decode, info parity.

`hunk` (codec) → | `cdzl`<br>19584 | `cdlz`<br>19584 | `cdzs`<br>19584 | `cdfl`<br>19584 | `zlib`<br>19584 | `none`<br>19584 | `cdzl`<br>39168
|---|---|---|---|---|---|---|---
**cd-mixed** (CUE, mixed-mode) | ✅ 10/10 | ✅ 10/10 | ✅ 10/10 | ✅ 10/10 | ✅ 10/10 | ✅ 10/10 | ✅ 10/10
**cd-audio** (CUE, audio-only) | ✅ 10/10 | ✅ 10/10 | ✅ 10/10 | ✅ 10/10 | ✅ 10/10 | ✅ 10/10 | ✅ 10/10
**disc-iso** (ISO) | ✅ 10/10 | ✅ 10/10 | ✅ 10/10 | ✅ 10/10 | ✅ 10/10 | ✅ 10/10 | ✅ 10/10

**The 10 checks per CD pair:**

| # | Check | Compares |
|---|-------|----------|
| 1 | `encode (ours)` | `ChdEncoder.EncodeCd(cue/gdi/iso/...)` succeeds |
| 2 | `chdman createcd` | Reference produced by chdman with identical args |
| 3 | `encode byte-identical` | **Every byte of the two `.chd` files is equal** (TOC metadata included) |
| 4 | `chdman verify (ours)` | chdman verifies our CD CHD |
| 5 | `chdman verify (ref)` | chdman verifies its own |
| 6 | `deep CheckFile (ours)` | `Chd.CheckFile(deep)` on our CD CHD |
| 7 | `extract parity (ours vs ref)` | Raw extraction of both files is identical |
| 8 | `decode (ours)` | `ReadAllBytes` on our file equals the reference extraction |
| 9 | `decode (ref)` | `ReadAllBytes` on chdman's file equals the reference extraction |
| 10 | `info parity` | Header parity vs `chdman info` |

The CD profiles are generated by `TestDataGenerator.CreateMixedCd`, `CreateAudioOnlyCd` (CUE sheets with generated audio — pregaps, mode markers, subcode region) and `CreateIso`; all deterministic.

---

## 3. Delta (parent/child differential) parity

Child CHDs are created **against a live parent** on both sides, in both directions of the compatibility matrix (chdman parent × chdman child, our parent × our child, and the two cross combinations). The child's hunk/unit sizes are taken from the actual parent file so both sides agree.

| # | Check | What it proves |
|---|-------|----------------|
| 1 | `child encode (ours, parent=chdman)` | We can build a child from a **chdman-written parent** |
| 2 | `chdman createraw -op` | chdman builds the same child from the same parent |
| 3 | `child encode byte-identical` | The two children are **byte-identical** |
| 4 | `chdman verify child (ours)` | chdman verifies our child with `-ip` |
| 5 | `deep CheckFileWithParent` | `Chd.CheckFileWithParent` deep-verifies our child |
| 6 | `chdman verify child (ref)` | chdman verifies its own child |
| 7 | `extract child (ours, -ip)` | Our child + parent extracts to the original image |
| 8 | `extract child (ref, -ip)` | chdman's child + parent extracts identically |
| 9 | `decode child (ours)` | `ReadAllBytes` with parent resolves the full image |
| 10 | `decode child (ref)` | Same for chdman's child |
| 11 | `wrong parent rejected (ours)` | A parent with different content is **rejected** by `CheckFileWithParent` |
| 12 | `chdman verify with wrong parent fails` | chdman also rejects a mismatched parent |
| 13 | `chdman createraw -op (parent=ours)` | chdman can build a child on **our parent file** (interop!) |
| 14 | `child encode (ours, parent=ours)` | We can build a child on our own parent |
| 15 | `child encode byte-identical (parent=ours)` | Both children byte-identical again |
| 16 | `chdman verify child of ours` | chdman verifies its child built against our parent |
| 17 | `chdman verify our child of ours` | chdman verifies *our* child built against our parent |
| 18 | `extract child of ours (ref)` | chdman-side extraction of the ours-parent chain |
| 19 | `decode child of ours (ours)` | Our decode of the ours/ours chain |

Result: **19/19 ✅** — including full **cross-interop** (chdman writing children against CHDSharp parents and vice versa).

---

## 4. Copy / re-compression parity

Zlib-encoded source CHDs are re-compressed to every other writable codec, on both sides.

| Suite | Checks | What is covered |
|-------|-------:|-----------------|
| `copy zlib -> zstd` | 7 | copy (ours) → `chdman verify` (ours) → extract (ours) → decode (ours) → `chdman copy` → verify (ref) → **extracted content identical** |
| `copy zlib -> lzma` | 7 | same protocol |
| `copy zlib -> huff` | 7 | same protocol |
| `copy zlib -> flac` | 7 | same protocol |
| `copy zlib -> none` | 7 | same protocol |
| `copy cd cdzl -> cdfl` | 5 | CD-to-CD re-compression: copy (ours), verify (ours), extract (ours), chdman copy, content identical |
| `copy child` | 3 | Copy of a **delta child** with `SourceParentPath`: copy, `chdman verify`, extract |
| **Total** | **43** | ✅ 43/43 |

`ChdEncoder.Copy` accepts the same `-c` codec list as chdman and — as the `decode copy zlib->x (chdman)/(ours)` decoder suites show — every produced copy is read back byte-perfect by **both** decoders.

---

## 5. Decoder parity (every asset, both implementations)

Every CHD produced anywhere in the run — by us **and** by chdman, raw and CD, plain and delta-child — is an **asset**. The decode suite re-opens all 190 assets (99 produced by CHDSharp, 91 by chdman — the eight extra are the CHDSharp-only tiny/1-byte cases for which chdman has no reference pair) and runs 6 checks on each:

| # | Check | Compares |
|---|-------|----------|
| 1 | `chdman verify` | chdman verifies the asset (with `-ip` when the asset is a child) |
| 2 | `deep CheckFile` | `Chd.CheckFile` / `CheckFileWithParent` (deep) on the asset |
| 3 | `ReadAllBytes == chdman extract` | Whole-image decode equals the original input bytes |
| 4 | `random access == chdman extract` | `Read()` at 9 probe offsets (0, 1, hunk−1, hunk, hunk+1, 2·hunk+137, mid, end−100, end−1) equals the source |
| 5 | `ReadHunk == chdman extract` | `ReadHunk` at hunk 0, middle, and last hunk equals the source |
| 6 | `Read past end -> error` | Out-of-range reads return an error, never garbage |

Asset families covered (all 190, **all 6/6 ✅**):

| Family | Assets | Codecs |
|--------|-------:|--------|
| Raw (`zero/random/pattern/mixed/repeated/text/pcm16/unaligned/tiny*`) | 132 | zlib, zstd, lzma, huff, flac, zlib+zstd+lzma, none |
| CD (`cd-mixed`, `cd-audio`, `disc-iso`) | 42 | cdzl, cdlz, cdzs, cdfl, zlib, none |
| Delta children (chdman-parent and ours-parent, both implementers) | 4 | zlib with parent |
| Copies (raw re-compress, CD re-compress, child copy) | 12 | zstd, lzma, huff, flac, none, cdfl, zlib |

---

## 6. Header info parity

`Chd.ReadHeader` output is compared field-by-field with `chdman info` for one representative CHD per distinct codec combination (deduplicated):

| Asset | Result |
|-------|:------:|
| zeros × zlib(4096/512), zstd, lzma, huff, flac, zlib,zstd,lzma, none | ✅ 7/7 |
| cd-mixed × cdzl, cdlz, cdzs, cdfl, zlib, none | ✅ 6/6 |
| **Total** | ✅ **13/13** — version, logical size, hunk size, hunk count, unit size, unit count, compression string, SHA1, data SHA1 all match |

---

## 7. CLI battle suites (CHDSharp CLI vs chdman)

The `CHDSharp` executable is exercised against `chdman.exe` on the same corpus. `cli-info` and `cli-verify` run on **every** asset; the remaining suites run dedicated create/extract/meta scenarios.

| Suite | Checks | Coverage |
|-------|-------:|----------|
| `cli-info` | 190 | `info` on every asset: exit code, then **field-by-field output parity** (version, logical size, hunk/unit size, counts, compression label, SHA1, data SHA1) |
| `cli-verify` | 190 | `verify` on every asset: exit-code parity with chdman |
| `cli-createraw` | 45 | 3 inputs (zeros, random, mixed) × 3 codecs (zlib, lzma, none): CLI create, chdman create, **byte-identical CHD**, extracted content parity, chdman verify of CLI output |
| `cli-createhd` | 12 | 3 sizes (4 KiB, 32 KiB, 1 MiB): CLI create, chdman create, byte-identical, verify |
| `cli-createcd` | 4 | createcd from CUE: CLI vs chdman, content parity (extract), verify CLI output |
| `cli-copy` | 4 | copy -c lzma: CLI vs chdman, content parity, verify |
| `cli-extractraw` | 4 | extractraw: CLI vs chdman output byte-identical + equals source |
| `cli-extractcd` | 3 | extractcd: CLI vs chdman — CUE sheets structurally identical (bin name normalized) |
| `cli-addmeta` | 11 | create (uncompressed) on both sides, addmeta/dumpmeta/delmeta on both sides, dumped metadata **byte-identical**, verify after meta ops |
| **Core suites total** | **463** | ✅ 463/463 |

### Full parity suites (per-command arg matrix)

`CHDSharpBattleTest` also exhaustively exercises every documented CLI argument on both tools — aliases, size-suffix forms, parent variants, slice windows, force/verbose, and every error path (duplicate option, invalid option, missing parameter, conflict pairs `-isb`/`-ish`, `-ib`/`-ih`, `-isf`/`-if`). Exit codes are required to match chdman **exactly** for creates, extracts, copies, verifies, and infos.

| Suite | Checks | Coverage |
|-------|-------:|----------|
| `cli-help` | 14 | help/no-args, `help <cmd>` for all 10 commands, unknown-command handling, chdman help parity |
| `cli-info-full` | 11 | `-i`/`--input`/positional, `-v`/`--verbose`, exit parity, duplicate `-i` → error, invalid option, missing param, non-existent file |
| `cli-verify-full` | 14 | plain/long/positional forms, `-ip` parent + `--inputparent`, child-without-parent → fail, `--fix`, duplicate `-i` parity, invalid/missing/non-existent |
| `cli-createraw-full` | 45 | 8 codec cases × (baseline, `--hunksize`, `--hunk-size`, `4K` suffix), `-d` DVD flag, `-np`, `-isb`/`-ib` + `-ish`/`-ih` slices, `-op` parent parity, `-c` alias, verbose, duplicate/missing/invalid/conflict errors |
| `cli-createhd-full` | 17 | `--size` vs `-s`, suffix quirk parity, `-chs` (short/long), `-ss`, `-tp` template (short/long), input file, `-hs`/`-np`, slices, `-c none`, `-op`, errors, verbose |
| `cli-createcd-full` | 52 | CUE (mixed/audio/ISO) × codecs (cdzl, cdlz, cdfl, none) × (default, `hs=39168`, `--hunksize`, `-np 2`), `-op`, duplicate/invalid/verbose |
| `cli-createdvd` | 7 | createdvd from ISO (CLI vs chdman), `--compression` + `-hs`, `-np` vs `--numprocessors`, slices, duplicate/invalid, `-op` |
| `cli-createld` | 8 | createld from AVI, **AVHU parity (byte-identical)**, verify via chdman, extract parity, `-hs`, `-isf`/`-if`, duplicate, missing input |
| `cli-extractraw-full` | 12 | full-file parity, `--input`/`--output` aliases, byte and hunk slices with suffix `K`, `-ip` parent parity, conflicts, errors, force |
| `cli-extracthd-dvd` | 4 | extracthd + extractdvd full parity and `-ip` |
| `cli-extractcd-full` | 9 | basic CUE, `--outputbin` (short/long), `--splitbin %t`, `--cooked` vs `--raw`, duplicate `-ob`, invalid, `.toc` output |
| `cli-extractld` | 3 | missing input, invalid option parity, `-isf`/`-if` |
| `cli-copy-full` | 22 | codecs lzma/zstd/huff/flac/none/zlib, `--compression`, `-hs`/`--hunksize`, `-np`, slices (`-isb`/`-ib`, `-ish`/`-ih`), `-ip`+`--outputparent`, `-op`, `--no-upgrade`, verbose, force, errors, conflicts |
| `cli-meta-full` | 15 | addmeta `-vt`/`--valuetext`/`-vf`/`-ix`/`-nocs`, dumpmeta `-t`/`--tag`/`--index`/`--output`, delmeta, verify after ops, missing-tag errors |
| `cli-hash` | 10 | sha1 default, `sha1,sha256,crc32,xxh3`, crc32 only, json/sfv results, per-track CD hashing, errors |
| `cli-batch` | 3 | batch extract, batch create (no-crash), missing dir error |
| `cli-listtemplates` | 2 | CLI output + chdman parity |
| `cli-misc` | 14 | classify, detect, toc, cue, parent, list metadata, random stress, missing-file handling |
| `cli-force` | 2 | `-f`/`--force` variants for createraw, copy force parity |
| `cli-alias-suffix` | 21 | `K`/`M`/`k` suffixes for `-hs`, `M` for `-ib`, exceeds-max error, all alias spellings (`-hs`/`--hunksize`/`--hunk-size`, `-us`/`--unitsize`/`--unit-size`, `-c`/`--compression`, `-np`/`--numprocessors`, `-t`/`--tasks`, `-f`/`--force`), positional args |
| `cli-error` | 11 | invalid-option parity per command (createraw, createcd, createdvd, copy, extractraw, info, verify), duplicate `-c`, missing input, hunk-not-multiple-of-unit error parity, unknown-command message |
| **Full-parity total** | **296** | ✅ 296/296 |
| **CLI battle total (core + full parity)** | **759** | ✅ **759/759** |

---

## 8. Real-world CHD collections (`--real`)

The synthetic corpus is not the end of it. Point the harness at a real folder of `*.chd` files and it runs the same decoder/verify/extract gauntlet on every file (recursive scan, per-command timeout configured with `--real-timeout`):

```bash
dotnet run --project CHDSharpBattleTest -- --real "D:\CHD Collection" --real-timeout 900
```

Every real CHD gets: `chdman verify`, deep `Chd.CheckFile`, `ReadAllBytes == chdman extractraw`, random-access probes, `ReadHunk`, past-end error behavior, `info` parity, and CLI verify/info parity. The last real-world sweep (56 CHDs, incl. zstd/cdzs/lzma/flac/delta chains) passed **3003/3003 checks**.

---

## 9. Known chdman quirks worth knowing

These are chdman behaviors (not bugs in CHDSharp) the battle suite has observed and accounts for:

| Quirk | Detail |
|-------|--------|
| `chdman info` access violation (rare) | On one run, `chdman info` crashed (exit `-1073741819` = 0xC0000005) on its **own** `zlib,zstd,lzma` text-data CHD — a file our CLI reads fine. Non-reproducible on the next run. |
| `-np` never changes bytes | `--numprocessors`/`--tasks` affects only speed; output CHDs are byte-identical regardless of worker count. |
| `createhd --size` suffix quirk | `-s 512K` is parsed by chdman as 512 bytes (sscanf semantics); CHDSharp reproduces the quirk for parity and only `createraw`-style sizes accept `K`/`M`/`G`. |
| `chdman addmeta/delmeta` needs uncompressed CHDs | chdman's V5 writer only opens uncompressed files for rewrite; the meta suite therefore uses `-c none` so both sides can rewrite. |
| Conflict pairs | `-isb`+`-ish`, `-ib`+`-ih`, `-isf`+`-if` are mutual exclusions enforced identically by both tools. |
| Duplicate options | "Multiple parameters of the same type specified" — hard error on both sides, exit-code parity verified. |

---

## Reproducing

```bash
dotnet run --project CHDSharpBattleTest                       # full (this page's numbers)
dotnet run --project CHDSharpBattleTest -- --quick             # smoke (~45 s)
dotnet run --project CHDSharpBattleTest -- --out H:\battle     # artifacts + report.txt
dotnet run --project CHDSharpBattleTest -- --real "D:\CHD"      # real-world sweep
```

Exit code: `0` = all checks passed, `1` = any failed, `2` = usage error. A full run writes a line-per-check `report.txt` plus the paired `.ours.chd` / `.ref.chd` artifacts into `<out>/battle/battle-<timestamp>/`.