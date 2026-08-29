---
layout: default
---

# CLI Command Reference

Complete list of every command and every accepted argument in the **`CHDSharp`** CLI, side by side with **`chdman`** (MAME 0.289). The CLI is drop-in compatible with `chdman`: every `chdman` subcommand and option is accepted with identical semantics, validation errors, and exit codes, plus a few CHDSharp-only conveniences (marked below).

Sources of truth: `chdman.cpp` (`s_options[]` / `s_commands[]`, MAME 0.289) and `CHDSharpCli/Program.cs` (per-command parsers).

---

## Command index

| Command | In chdman | Description |
|-----------|:---------:|-------------|
| `info` | ✓ | Displays information about a CHD |
| `verify` | ✓ | Verifies a CHD's integrity |
| `createraw` | ✓ | Create a raw CHD from the input file |
| `createhd` | ✓ | Create a hard disk CHD from the input file |
| `createcd` | ✓ | Create a CD CHD from the input file |
| `createdvd` | ✓ | Create a DVD CHD from the input file |
| `createld` | ✓ | Create a laserdisc CHD from the input file |
| `extractraw` | ✓ | Extract raw file from a CHD input file |
| `extracthd` | ✓ | Extract raw hard disk file from a CHD input file |
| `extractcd` | ✓ | Extract CD file from a CHD input file |
| `extractdvd` | ✓ | Extract DVD file from a CHD input file |
| `extractld` | ✓ | Extract laserdisc AVI from a CHD input file |
| `copy` | ✓ | Copy data from one CHD to another of the same type |
| `addmeta` | ✓ | Add metadata to the CHD |
| `delmeta` | ✓ | Remove metadata from the CHD |
| `dumpmeta` | ✓ | Dump metadata from the CHD to stdout or to a file |
| `listtemplates` | ✓ | List hard disk templates |
| `help <command>` | ✓ | Help for all commands or a specific command |
| `create` | — | CHDSharp alias for `createraw` |
| `random` | — | Random-access read stress test on a CHD |
| `list` | — | Verify every `.chd` path listed in a text file |
| `parent` | — | Verify a child CHD against its parent |
| `toc` | — | Print table-of-contents for a CD/GD-ROM CHD |
| `cue` | — | Generate a CUE sheet for a CD CHD |
| `classify` | — | Classify CHD media type (cd/dvd/hdd/gd-rom) |
| `detect` | — | Detect game platform/region from a CD CHD |
| `hash` | — | Compute content hashes (SHA-1/SHA-256/CRC-32/XXH3) |
| `batch` | — | Batch extract/create over a directory |
| `<dir> [<dir> ...]` | — | Recursively verify all `.chd` files in directories |

---

## Option syntax conventions

These apply to both `chdman` and `CHDSharp`:

- Options are passed as separate tokens: `-i file.chd` or `--input file.chd` (not `option=value`).
- Every option has a short chdman-style form (`-i`) and a long form (`--input`).
- Unknown option, duplicate option ("Multiple parameters of the same type specified"), and missing parameter are hard errors.
- Start/length pairs are mutually exclusive per group: `-isb`/`-ish`/`-isf` vs `-ib`/`-ih`/`-if`.
- Numeric option values accept a `K`/`M`/`G` suffix (`k`=1024, `M`=1024², `G`=1024³), matching chdman's `parse_number()`. The one exception is `createhd --size`, which follows chdman's stricter `sscanf("%I64u")` behaviour (plain digits only — `"512K"` is read as `512` bytes).
- `-f` is shared: **force** (overwrite output) on create/extract/copy/dumpmeta commands, **fix** (repair header SHA-1) on `verify`.
- `-t` means **tag** (metadata) in chdman. CHDSharp keeps `-t` = `--tag` on `addmeta`/`delmeta`/`dumpmeta` and additionally accepts `-t` as a legacy alias for `--numprocessors` on the create/copy commands (see tables).

CHDSharp-only parsing conveniences:

- Positional fallback: `CHDSharp createraw in.bin out.chd` (input then output, before any flags) works for create/extract/copy/batch commands. chdman requires named `-i`/`-o`.
- Extra long-form aliases: `--hunk-size`, `--unit-size`, `--codecs`, `--tasks` (see tables).

---

## Shared option pool

All options defined by chdman's global option table, and whether each command accepts them. `**req**` = required for that command.

| Long form | Short | Parameter | Description |
|-------------|-------|:---------:|-------------|
| `--input` | `-i` | file | Input file (CHD for extract/verify/info; source file for create) |
| `--inputparent` | `-ip` | file | Parent CHD file for the input CHD |
| `--output` | `-o` | file | Output file (CHD, BIN, CUE, ISO or AVI depending on command) |
| `--outputparent` | `-op` | file | Parent CHD file for the output CHD |
| `--outputbin` | `-ob` | file | Output BIN file name for binary data (`extractcd`) |
| `--splitbin` | `-sb` | — | Output one binary file per track (`extractcd`) |
| `--force` | `-f` | — | Force overwriting an existing output file |
| `--fix` | `-f` | — | Fix the header SHA-1 if it is incorrect (`verify` only) |
| `--inputstartbyte` | `-isb` | offset | Starting byte offset within the input |
| `--inputstarthunk` | `-ish` | offset | Starting hunk offset within the input |
| `--inputstartframe` | `-isf` | offset | Starting frame within the input |
| `--inputbytes` | `-ib` | length | Effective length of input in bytes |
| `--inputhunks` | `-ih` | length | Effective length of input in hunks |
| `--inputframes` | `-if` | length | Effective length of input in frames |
| `--hunksize` | `-hs` | bytes | Size of each hunk, in bytes |
| `--unitsize` | `-us` | bytes | Size of each unit, in bytes |
| `--compression` | `-c` | codecs | Which compression codecs to use (up to 4, comma-separated) |
| `--ident` | `-id` | file | 512-byte ATA IDENTIFY DEVICE file providing CHS information (`createhd`) |
| `--chs` | `-chs` | c,h,s | CHS geometry specified directly (`createhd`) |
| `--sectorsize` | `-ss` | bytes | Size of each hard disk sector (`createhd`) |
| `--size` | `-s` | bytes | Size of the blank output file (`createhd`) |
| `--template` | `-tp` | id | Use hard disk template (see `listtemplates`) (`createhd`) |
| `--tag` | `-t` | tag | 4-character metadata tag (`addmeta`/`delmeta`/`dumpmeta`) |
| `--index` | `-ix` | index | Indexed instance of the metadata tag |
| `--valuetext` | `-vt` | text | Text for the metadata (`addmeta`) |
| `--valuefile` | `-vf` | file | File containing metadata data (`addmeta`) |
| `--nochecksum` | `-nocs` | — | Exclude this metadata from the overall SHA-1 (`addmeta`) |
| `--numprocessors` | `-np` | count | Limit processors used during compression |
| `--verbose` | `-v` | — | Output additional information |

### CHDSharp-only option aliases (not accepted by chdman)

| Alias | Equivalent to | Accepted on |
|---------|----------------|-------------|
| `--hunk-size` | `--hunksize` | `createraw`, `createcd`, `createdvd` |
| `--unit-size` | `--unitsize` | `createraw` |
| `--codecs` | `--compression` | `createld`, `copy`, `batch` |
| `--tasks`, `-t` | `--numprocessors` | `createraw`, `createcd`, `createdvd`, `createld`, `copy` |
| `--dvd` / `-d` | Force DVD metadata | `createraw` |
| `--cooked` | Cooked (2048-byte) sector output | `extractcd` |
| `--raw`, `--raw-frames` | Raw (2448-byte) frame output | `extractcd` |
| `--no-upgrade` | Preserve legacy metadata tags | `copy` |
| `--verbose` (`-v`) | Per-hunk compression logging | additionally on `createraw`, `createhd`, `createcd`, `createdvd`, `createld`, `copy` |

---

## Per-command option tables

Legend: **✓** accepted, **—** not accepted, **✓ req** required.

### info

Displays information about a CHD.

| Option | Short | chdman | CHDSharp | Description |
|----------|-------|:------:|:--------:|-------------|
| `--input` | `-i` | ✓ req | ✓ req | Input CHD file |
| `--verbose` | `-v` | ✓ | ✓ | Additional information |

### verify

Verifies a CHD's integrity.

| Option | Short | chdman | CHDSharp | Description |
|----------|-------|:------:|:--------:|-------------|
| `--input` | `-i` | ✓ req | ✓ req | Input CHD file |
| `--inputparent` | `-ip` | ✓ | ✓ | Parent CHD file |
| `--fix` | `-f` | ✓ | ✓ | Fix mismatched SHA-1 header fields |

### createraw

Create a raw CHD from the input file. (CHDSharp also accepts the alias `create`.)

| Option | Short | chdman | CHDSharp | Description |
|----------|-------|:------:|:--------:|-------------|
| `--output` | `-o` | ✓ req | ✓ req | Output CHD file |
| `--input` | `-i` | ✓ req | ✓ req | Input file |
| `--outputparent` | `-op` | ✓ | ✓ | Output parent CHD |
| `--force` | `-f` | ✓ | ✓ | Overwrite existing output |
| `--inputstartbyte` | `-isb` | ✓ | ✓ | Starting byte offset within input |
| `--inputstarthunk` | `-ish` | ✓ | ✓ | Starting hunk offset within input |
| `--inputbytes` | `-ib` | ✓ | ✓ | Effective length of input in bytes |
| `--inputhunks` | `-ih` | ✓ | ✓ | Effective length of input in hunks |
| `--hunksize` | `-hs` | ✓ | ✓ ( + `--hunk-size`) | Hunk size in bytes |
| `--unitsize` | `-us` | ✓ | ✓ ( + `--unit-size`) | Unit size in bytes (required if no output parent) |
| `--compression` | `-c` | ✓ | ✓ | Codecs (default: `lzma,zlib,huff,flac`) |
| `--numprocessors` | `-np` | ✓ | ✓ ( + `--tasks`, `-t`) | Parallel workers |
| `--dvd` | `-d` | — | ✓ | CHDSharp only: force DVD metadata |
| `--verbose` | `-v` | — | ✓ | Per-hunk compression logging |

### createhd

Create a hard disk CHD. If `--input` is omitted, a blank zero-filled image is created.

| Option | Short | chdman | CHDSharp | Description |
|----------|-------|:------:|:--------:|-------------|
| `--output` | `-o` | ✓ req | ✓ req | Output CHD file |
| `--input` | `-i` | ✓ | ✓ | Input file (optional; omit for blank image) |
| `--outputparent` | `-op` | ✓ | ✓ | Output parent CHD |
| `--force` | `-f` | ✓ | ✓ | Overwrite existing output |
| `--inputstartbyte` | `-isb` | ✓ | ✓ | Starting byte offset within input |
| `--inputstarthunk` | `-ish` | ✓ | ✓ | Starting hunk offset within input |
| `--inputbytes` | `-ib` | ✓ | ✓ | Effective length of input in bytes |
| `--inputhunks` | `-ih` | ✓ | ✓ | Effective length of input in hunks |
| `--hunksize` | `-hs` | ✓ | ✓ | Hunk size in bytes |
| `--compression` | `-c` | ✓ | ✓ | Codecs (default: `none` for blank images) |
| `--template` | `-tp` | ✓ | ✓ | Hard disk template ID (see `listtemplates`) |
| `--ident` | `-id` | ✓ | ✓ | 512-byte ATA IDENTIFY DEVICE file |
| `--chs` | `-chs` | ✓ | ✓ | CHS geometry: `cylinders,heads,sectors` |
| `--size` | `-s` | ✓ | ✓ | Size of blank image (plain bytes; chdman sscanf semantics) |
| `--sectorsize` | `-ss` | ✓ | ✓ | Sector size in bytes (default: 512) |
| `--numprocessors` | `-np` | ✓ | ✓ | Parallel workers |
| `--verbose` | `-v` | — | ✓ | Per-hunk compression logging |

### createcd

Create a CD CHD from CUE/GDI/ISO/TOC/NRG input.

| Option | Short | chdman | CHDSharp | Description |
|----------|-------|:------:|:--------:|-------------|
| `--output` | `-o` | ✓ req | ✓ req | Output CHD file |
| `--input` | `-i` | ✓ req | ✓ req | Input file: `.cue`, `.gdi`, `.iso`, `.toc`, `.nrg`, `.cdr`, `.toast` |
| `--outputparent` | `-op` | ✓ | ✓ | Output parent CHD |
| `--force` | `-f` | ✓ | ✓ | Overwrite existing output |
| `--hunksize` | `-hs` | ✓ | ✓ ( + `--hunk-size`) | Hunk size in bytes (default: 19584 = 8 × 2448) |
| `--compression` | `-c` | ✓ | ✓ | Codecs (default: `cdlz,cdzl,cdfl`) |
| `--numprocessors` | `-np` | ✓ | ✓ ( + `--tasks`, `-t`) | Parallel workers |
| `--verbose` | `-v` | — | ✓ | Per-hunk compression logging |

### createdvd

Create a DVD CHD from the input file (typically an `.iso`).

| Option | Short | chdman | CHDSharp | Description |
|----------|-------|:------:|:--------:|-------------|
| `--output` | `-o` | ✓ req | ✓ req | Output CHD file |
| `--input` | `-i` | ✓ req | ✓ req | Input file (raw binary, size divisible by 2048) |
| `--outputparent` | `-op` | ✓ | ✓ | Output parent CHD |
| `--force` | `-f` | ✓ | ✓ | Overwrite existing output |
| `--inputstartbyte` | `-isb` | ✓ | ✓ | Starting byte offset within input |
| `--inputstarthunk` | `-ish` | ✓ | ✓ | Starting hunk offset within input |
| `--inputbytes` | `-ib` | ✓ | ✓ | Effective length of input in bytes |
| `--inputhunks` | `-ih` | ✓ | ✓ | Effective length of input in hunks |
| `--hunksize` | `-hs` | ✓ | ✓ ( + `--hunk-size`) | Hunk size in bytes |
| `--compression` | `-c` | ✓ | ✓ | Codecs (default: `lzma,zlib,huff,flac`) |
| `--numprocessors` | `-np` | ✓ | ✓ ( + `--tasks`, `-t`) | Parallel workers |
| `--verbose` | `-v` | — | ✓ | Per-hunk compression logging |

### createld

Create a laserdisc CHD from an AVI file.

| Option | Short | chdman | CHDSharp | Description |
|----------|-------|:------:|:--------:|-------------|
| `--output` | `-o` | ✓ req | ✓ req | Output CHD file |
| `--input` | `-i` | ✓ req | ✓ req | Input AVI file |
| `--outputparent` | `-op` | ✓ | ✓ | Output parent CHD |
| `--force` | `-f` | ✓ | ✓ | Overwrite existing output |
| `--inputstartframe` | `-isf` | ✓ | ✓ | Starting frame within input |
| `--inputframes` | `-if` | ✓ | ✓ | Effective length of input in frames |
| `--hunksize` | `-hs` | ✓ | ✓ | Hunk size in bytes |
| `--compression` | `-c` | ✓ | ✓ ( + `--codecs`) | Codecs (default: `avhu`) |
| `--numprocessors` | `-np` | ✓ | ✓ ( + `--tasks`, `-t`) | Parallel workers |
| `--verbose` | `-v` | — | ✓ | Per-hunk compression logging |

### extractraw / extracthd / extractdvd

Extract raw file / raw hard disk file / DVD file from a CHD input file. All three share the same option set.

| Option | Short | chdman | CHDSharp | Description |
|----------|-------|:------:|:--------:|-------------|
| `--output` | `-o` | ✓ req | ✓ req | Output file |
| `--input` | `-i` | ✓ req | ✓ req | Input CHD file |
| `--inputparent` | `-ip` | ✓ | ✓ | Parent CHD file |
| `--force` | `-f` | ✓ | ✓ | Overwrite existing output |
| `--inputstartbyte` | `-isb` | ✓ | ✓ | Starting byte offset within input |
| `--inputstarthunk` | `-ish` | ✓ | ✓ | Starting hunk offset within input |
| `--inputbytes` | `-ib` | ✓ | ✓ | Effective length of input in bytes |
| `--inputhunks` | `-ih` | ✓ | ✓ | Effective length of input in hunks |

### extractcd

Extract CD file from a CHD input file (BIN/CUE).

| Option | Short | chdman | CHDSharp | Description |
|----------|-------|:------:|:--------:|-------------|
| `--output` | `-o` | ✓ req | ✓ req | Output CUE file |
| `--input` | `-i` | ✓ req | ✓ req | Input CHD file |
| `--outputbin` | `-ob` | ✓ | ✓ | Output BIN file name for binary data |
| `--splitbin` | `-sb` | ✓ | ✓ | Output one binary file per track (BIN name must contain a `%t` track-number variable) |
| `--inputparent` | `-ip` | ✓ | ✓ | Parent CHD file |
| `--force` | `-f` | ✓ | ✓ | Overwrite existing output |
| `--cooked` | — | — | ✓ | CHDSharp only: force cooked (2048-byte) sectors (default) |
| `--raw`, `--raw-frames` | — | — | ✓ | CHDSharp only: write full 2448-byte raw frames |

The output BIN name supports chdman variables: `%t` (track number), optional printf width (`%02t`), and `%%` escaping.

### extractld

Extract laserdisc AVI from a CHD input file.

| Option | Short | chdman | CHDSharp | Description |
|----------|-------|:------:|:--------:|-------------|
| `--output` | `-o` | ✓ req | ✓ req | Output AVI file |
| `--input` | `-i` | ✓ req | ✓ req | Input CHD file |
| `--inputparent` | `-ip` | ✓ | ✓ | Parent CHD file |
| `--force` | `-f` | ✓ | ✓ | Overwrite existing output |
| `--inputstartframe` | `-isf` | ✓ | ✓ | Starting frame within input |
| `--inputframes` | `-if` | ✓ | ✓ | Effective length of input in frames |

### copy

Copy data from one CHD to another of the same type (re-compression).

| Option | Short | chdman | CHDSharp | Description |
|----------|-------|:------:|:--------:|-------------|
| `--output` | `-o` | ✓ req | ✓ req | Output CHD file |
| `--input` | `-i` | ✓ req | ✓ req | Input CHD file |
| `--inputparent` | `-ip` | ✓ | ✓ | Parent CHD file for input |
| `--outputparent` | `-op` | ✓ | ✓ | Parent CHD file for output |
| `--force` | `-f` | ✓ | ✓ | Overwrite existing output |
| `--inputstartbyte` | `-isb` | ✓ | ✓ | Starting byte offset within input |
| `--inputstarthunk` | `-ish` | ✓ | ✓ | Starting hunk offset within input |
| `--inputbytes` | `-ib` | ✓ | ✓ | Effective length of input in bytes |
| `--inputhunks` | `-ih` | ✓ | ✓ | Effective length of input in hunks |
| `--hunksize` | `-hs` | ✓ | ✓ | Hunk size in bytes |
| `--compression` | `-c` | ✓ | ✓ ( + `--codecs`) | Codecs (default: per input CHD type) |
| `--numprocessors` | `-np` | ✓ | ✓ ( + `--tasks`, `-t`) | Parallel workers |
| `--no-upgrade` | — | — | ✓ | CHDSharp only: preserve legacy metadata tags |
| `--verbose` | `-v` | — | ✓ | Per-hunk compression logging |

### addmeta

Add metadata to the CHD.

| Option | Short | chdman | CHDSharp | Description |
|----------|-------|:------:|:--------:|-------------|
| `--input` | `-i` | ✓ req | ✓ req | Input CHD file |
| `--tag` | `-t` | ✓ req | ✓ req | 4-character metadata tag |
| `--index` | `-ix` | ✓ | ✓ | Indexed instance of this tag (default: 0) |
| `--valuetext` | `-vt` | ✓ | ✓ | Text for the metadata |
| `--valuefile` | `-vf` | ✓ | ✓ | File containing data to add |
| `--nochecksum` | `-nocs` | ✓ | ✓ | Exclude this metadata from the overall SHA-1 |

### delmeta

Remove metadata from the CHD.

| Option | Short | chdman | CHDSharp | Description |
|----------|-------|:------:|:--------:|-------------|
| `--input` | `-i` | ✓ req | ✓ req | Input CHD file |
| `--tag` | `-t` | ✓ req | ✓ req | 4-character metadata tag |
| `--index` | `-ix` | ✓ | ✓ | Indexed instance of this tag (default: 0) |

### dumpmeta

Dump metadata from the CHD to stdout or to a file.

| Option | Short | chdman | CHDSharp | Description |
|----------|-------|:------:|:--------:|-------------|
| `--input` | `-i` | ✓ req | ✓ req | Input CHD file |
| `--tag` | `-t` | ✓ req | ✓ req | 4-character metadata tag |
| `--output` | `-o` | ✓ | ✓ | Output file for binary data (default: stdout) |
| `--force` | `-f` | ✓ | ✓ | Overwrite existing output |
| `--index` | `-ix` | ✓ | ✓ | Indexed instance of this tag (default: 0) |

### listtemplates

List built-in hard disk geometry templates. Takes no options.

| Option | chdman | CHDSharp | Description |
|--------|:------:|:--------:|-------------|
| *(none)* | ✓ | ✓ | Prints the 17 built-in HDD templates |

### help

| Usage | chdman | CHDSharp | Description |
|---------|:------:|:--------:|-------------|
| `help` | ✓ | ✓ | Print command summary |
| `help <command>` | ✓ | ✓ | Detailed help for a command |

---

## CHDSharp-only commands

These commands exist only in CHDSharp (no chdman equivalent). All accept the file as a positional argument or via `--input`/`-i`.

| Command | Options | Description |
|-----------|-----------|-------------|
| `create` | same as `createraw` | Alias for `createraw` |
| `random <file.chd>` | — | Random-access read stress test |
| `list <listfile.txt>` | — | Verify every `.chd` path listed in a text file |
| `parent <child.chd> <parent.chd>` | — | Verify a child CHD against its parent |
| `toc <file.chd>` | — | Print table-of-contents for a CD/GD-ROM CHD |
| `cue <file.chd> [binfile]` | — | Generate a CUE sheet (optional BIN name) |
| `classify <file.chd>` | — | Classify CHD media type |
| `detect <file>` | — | Detect game platform/region from a CD CHD |
| `hash <file.chd>` | see below | Compute content hashes |
| `batch <in-dir> <out-dir>` | see below | Batch extract/create |
| `<dir> [<dir> ...]` | — | Recursively verify all `.chd` files in the given directories |

### hash

| Option | Short | Description |
|----------|-------|-------------|
| `--input` | `-i` | Input CHD file (required) |
| `--hashes` | — | Comma-separated hash types: `sha1`, `sha256`, `crc32`, `xxh3` (default: `sha1`) |
| `--result` | — | Output format: `text`, `json`, `sfv` (default: `text`) |
| `--tracks` | — | Compute per-track hashes (CD only) |

### batch

| Option | Short | Description |
|----------|-------|-------------|
| `--input` | `-i` | Input directory (required) |
| `--output` | `-o` | Output directory (required) |
| `--action` | — | `extract` (default) or `create` |
| `--compression` | `-c` ( + `--codecs`) | Codecs for create mode |

---

## Compression codec names

Valid tags for `-c`/`--compression` (up to 4, tried in order):

| Tag | Codec | Typical use |
|-------|-------|-------------|
| `none` | No compression | blank/testing |
| `zlib` | Deflate | raw-type images |
| `zstd` | Zstandard | raw-type images |
| `lzma` | LZMA | raw-type images |
| `huff` | Huffman | raw-type images |
| `flac` | FLAC | raw-type images |
| `cdlz` | CD LZMA (ECC/sync aware) | CD/GD-ROM images |
| `cdzl` | CD Deflate (ECC/sync aware) | CD/GD-ROM images |
| `cdzs` | CD Zstandard (ECC/sync aware) | CD/GD-ROM images |
| `cdfl` | CD FLAC (ECC/sync aware) | CD/GD-ROM images |
| `avhu` | A/V Huffman | Laserdisc images |

Per-command defaults: `createraw`/`createdvd`/`copy` (raw types): `lzma,zlib,huff,flac` · `createcd`: `cdlz,cdzl,cdfl` · `createld`: `avhu` · blank `createhd`: `none`.
