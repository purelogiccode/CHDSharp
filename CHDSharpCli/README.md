# CHDSharp CLI

**Command-line CHD manager — compatible with MAME's `chdman` command syntax.**

The binary is named `CHDSharp` (e.g. `CHDSharp.exe` on Windows). It accepts the same subcommands and options as MAME's `chdman`, plus additional convenience commands.

> **v1.4.2** — complete `chdman` parity (GD-ROM Redump, `createhd -i` GDDD, `extractcd` cooked/raw, `copy` per-type defaults, strict CLI validation, createhd size/CHS quirks, laserdisc AVI byte-parity). Targets `net8.0` / `net9.0` / `net10.0`.

---

## Usage

```
CHDSharp <command> [options]

For help with any command, run:
   CHDSharp help <command>
```

### Commands

| Command | Description |
|---------|-------------|
| `info` | Displays information about a CHD |
| `verify` | Verifies a CHD's integrity |
| `createraw` | Create a raw CHD from the input file |
| `createhd` | Create a hard disk CHD from the input file |
| `createcd` | Create a CD CHD from the input file |
| `createdvd` | Create a DVD CHD from the input file |
| `createld` | Create a laserdisc CHD from the input file |
| `extractraw` | Extract raw file from a CHD input file |
| `extracthd` | Extract raw hard disk file from a CHD input file |
| `extractcd` | Extract CD file from a CHD input file |
| `extractdvd` | Extract DVD file from a CHD input file |
| `extractld` | Extract laserdisc AVI from a CHD input file |
| `copy` | Copy data from one CHD to another of the same type |
| `addmeta` | Add metadata to the CHD |
| `delmeta` | Remove metadata from the CHD |
| `dumpmeta` | Dump metadata from the CHD to stdout or to a file |
| `listtemplates` | List hard disk templates |

### Legacy Commands (also supported)

| Command | Description |
|---------|-------------|
| `<directory> [<directory> ...]` | Verify all .chd files in directories |
| `--random <file.chd>` | Random-access read test on a single CHD |
| `--list <listfile.txt>` | Verify every .chd path listed in a text file |
| `--parent <child.chd> <parent.chd>` | Verify a child CHD against its parent |
| `--toc <file.chd>` | Print table-of-contents for CD/GD-ROM CHD |
| `--cue <file.chd> [<binfile>]` | Generate CUE sheet for CD CHD |
| `--classify <file.chd>` | Classify CHD type (cd/dvd/hdd/gd-rom) |
| `--detect <file>` | Detect game platform |
| `--hash <file.chd>` | Compute content hashes |
| `--batch <in-dir> <out-dir>` | Batch extract/create |

---

## chdman Parity (battle-verified)

`CHDSharp` is battle-tested against `chdman.exe` (MAME 0.289) by `CHDSharpBattleTest` — every command, option, alias, size-suffix form, and error path, with **exit-code and output parity** verified on a deterministic corpus. Current dense-corpus result: **2907/2907 checks** (synthetic) + 3003/3003 (real-world CHDs).

| Suite | Checks | Coverage |
|-------|-------:|----------|
| `cli-info` / `cli-verify` | 190 + 190 | every corpus asset: field-by-field `info` output parity and `verify` exit-code parity |
| `cli-createraw` / `cli-createhd` / `cli-createcd` | 45 + 12 + 4 | CLI output **byte-identical** to chdman, content + verify parity |
| `cli-copy` / `cli-extractraw` / `cli-extractcd` / `cli-addmeta` | 4 + 4 + 3 + 11 | copy/extract/metadata output parity (extract byte-identical, CUE structural parity, metadata byte-identical) |
| Full arg-parity suites (`cli-*-full`, `cli-help`, `cli-hash`, `cli-batch`, `cli-misc`, `cli-force`, `cli-alias-suffix`, `cli-error`) | 296 | every documented argument on both tools — aliases, `K`/`M`/`G` suffixes, parent/slice variants, duplicate/invalid/missing-param errors with matching exit codes |
| **CLI total** | **759** | ✅ 759/759 |

Full per-check tables: [docs/chdman-parity.md](../docs/chdman-parity.md).

```bash
dotnet run --project CHDSharpBattleTest        # rerun the whole battle (requires chdman.exe at repo root)
```

---

## chdman-Compatible Examples

```bash
# Display CHD information
CHDSharp info --input game.chd
CHDSharp info -i game.chd --verbose

# Verify a CHD
CHDSharp verify --input game.chd
CHDSharp verify -i game.chd --inputparent parent.chd --fix

# Create a raw CHD from a binary file
CHDSharp createraw --output game.chd --input game.bin
CHDSharp createraw -o game.chd -i game.bin -c zlib,zstd,lzma -hs 65536 -np 8

# Create a CD CHD from CUE/GDI/ISO/TOC/NRG
CHDSharp createcd --output game.chd --input game.cue
CHDSharp createcd -o game.chd -i game.gdi -c cdlz,cdzl,cdfl

# Create a DVD CHD from ISO
CHDSharp createdvd --output game.chd --input game.iso
CHDSharp createdvd -o game.chd -i game.iso -c lzma,zlib,huff,flac

# Create a hard disk CHD (blank, zero-filled)
CHDSharp createhd --output disk.chd --size 104857600
CHDSharp createhd -o disk.chd --chs 1024,16,63 --sectorsize 512
CHDSharp createhd -o disk.chd --template 0

# Create a hard disk CHD from a raw image
CHDSharp createhd --output disk.chd --input disk.img

# Create a laserdisc CHD from AVI
CHDSharp createld --output ld.chd --input movie.avi -c avhu

# Extract a CHD to raw binary
CHDSharp extractraw --output game.bin --input game.chd
CHDSharp extracthd --output disk.img --input disk.chd

# Extract a CD CHD to BIN/CUE (cooked by default — matches chdman; add --raw for 2448-byte frames)
CHDSharp extractcd --output game.cue --input game.chd
CHDSharp extractcd -o game.cue -i game.chd --outputbin game.bin --raw

# Extract a DVD CHD to ISO
CHDSharp extractdvd --output game.iso --input game.chd

# Extract a laserdisc CHD to AVI
CHDSharp extractld --output movie.avi --input ld.chd

# Re-compress a CHD
CHDSharp copy --output new.chd --input old.chd -c zstd
CHDSharp copy -o new.chd -i old.chd --inputparent parent.chd --outputparent parent.chd

# Add/remove/dump metadata
CHDSharp addmeta --input game.chd --tag GAME --valuetext "gauntlet"
CHDSharp delmeta --input game.chd --tag GAME
CHDSharp dumpmeta --input game.chd --tag GAME

# List hard disk templates
CHDSharp listtemplates
```

### Common Options

| Option | Short | Description |
|--------|-------|-------------|
| `--input` | `-i` | Input file |
| `--output` | `-o` | Output file |
| `--inputparent` | `-ip` | Parent CHD for input |
| `--outputparent` | `-op` | Parent CHD for output |
| `--compression` | `-c` | Codecs (comma-separated: `zlib,zstd,lzma,huff,flac,cdzl,cdlz,cdzs,cdfl,avhu,none`) |
| `--hunksize` | `-hs` | Hunk size in bytes |
| `--unitsize` | `-us` | Unit size in bytes |
| `--numprocessors` | `-np` | Parallel worker count (default 8, range 1-64; speed only — never changes output bytes) |
| `--verbose` | `-v` | Per-hunk compression logging |
| `--force` | `-f` | Overwrite existing output |
| `--tag` | `-t` | 4-character metadata tag |
| `--index` | `-ix` | Metadata tag index |
| `--size` | `-s` | Size (supports K/M/G suffixes) |
| `--chs` | `-chs` | CHS geometry: `cylinders,heads,sectors` |
| `--sectorsize` | `-ss` | Sector size in bytes |
| `--template` | `-tp` | Hard disk template ID |
| `--ident` | `-id` | ATA IDENTIFY DEVICE file |

### CHDSharp-Only Option Aliases

Accepted by CHDSharp in addition to the chdman spellings:

| Alias | Equivalent to | Accepted on |
|---------|----------------|-------------|
| `--hunk-size` | `--hunksize` | `createraw`, `createcd`, `createdvd` |
| `--unit-size` | `--unitsize` | `createraw` |
| `--codecs` | `--compression` | `createld`, `copy`, `batch` |
| `--tasks`, `-t` | `--numprocessors` | `createraw`, `createcd`, `createdvd`, `createld`, `copy` |
| `--dvd`, `-d` | Force DVD metadata | `createraw` |
| `--cooked` | Cooked (2048-byte) sector output | `extractcd` |
| `--raw`, `--raw-frames` | Raw (2448-byte) frame output | `extractcd` |
| `--no-upgrade` | Preserve legacy metadata tags | `copy` |

Notes:
- `-f` means **force** on create/extract/copy/dumpmeta commands and **fix** on `verify`.
- `-t` means **tag** on `addmeta`/`delmeta`/`dumpmeta` and is an alias for `--numprocessors` on the create/copy commands.
- Numeric values accept `K`/`M`/`G` suffixes (`10M` = 10485760). Exception: `createhd --size` follows chdman's strict plain-digit parsing.
- Create/extract/copy/batch commands also accept positional `<input> <output>` before any flags.

### Accepted Arguments by Command

Every option accepted by each command (✱ = required). For the side-by-side comparison with `chdman` 0.289, see the [CLI Command Reference](../docs/cli-commands.md).

#### info

| Option | Short | Description |
|--------|-------|-------------|
| `--input` ✱ | `-i` | Input CHD file |
| `--verbose` | `-v` | Additional information |

#### verify

| Option | Short | Description |
|--------|-------|-------------|
| `--input` ✱ | `-i` | Input CHD file |
| `--inputparent` | `-ip` | Parent CHD file |
| `--fix` | `-f` | Fix mismatched SHA-1 header fields |

#### createraw (alias: `create`)

| Option | Short | Description |
|--------|-------|-------------|
| `--output` ✱ | `-o` | Output CHD file |
| `--input` ✱ | `-i` | Input file |
| `--outputparent` | `-op` | Output parent CHD |
| `--force` | `-f` | Overwrite existing output |
| `--inputstartbyte` | `-isb` | Starting byte offset within input |
| `--inputstarthunk` | `-ish` | Starting hunk offset within input |
| `--inputbytes` | `-ib` | Effective length of input in bytes |
| `--inputhunks` | `-ih` | Effective length of input in hunks |
| `--hunksize` | `-hs` | Hunk size in bytes |
| `--unitsize` | `-us` | Unit size in bytes (required if no output parent) |
| `--compression` | `-c` | Codecs (default: `lzma,zlib,huff,flac`) |
| `--numprocessors` | `-np` | Parallel workers |
| `--dvd` | `-d` | Force DVD metadata |
| `--verbose` | `-v` | Per-hunk compression logging |

#### createhd

Input is optional — omit for a blank zero-filled image.

| Option | Short | Description |
|--------|-------|-------------|
| `--output` ✱ | `-o` | Output CHD file |
| `--input` | `-i` | Input file (optional; omit for blank) |
| `--outputparent` | `-op` | Output parent CHD |
| `--force` | `-f` | Overwrite existing output |
| `--inputstartbyte` | `-isb` | Starting byte offset within input |
| `--inputstarthunk` | `-ish` | Starting hunk offset within input |
| `--inputbytes` | `-ib` | Effective length of input in bytes |
| `--inputhunks` | `-ih` | Effective length of input in hunks |
| `--hunksize` | `-hs` | Hunk size in bytes |
| `--compression` | `-c` | Codecs (default: `none` for blank images) |
| `--template` | `-tp` | Hard disk template ID (see `listtemplates`) |
| `--ident` | `-id` | 512-byte ATA IDENTIFY DEVICE file |
| `--chs` | `-chs` | CHS geometry: `cylinders,heads,sectors` |
| `--size` | `-s` | Size of blank image |
| `--sectorsize` | `-ss` | Sector size in bytes (default: 512) |
| `--numprocessors` | `-np` | Parallel workers |
| `--verbose` | `-v` | Per-hunk compression logging |

#### createcd

| Option | Short | Description |
|--------|-------|-------------|
| `--output` ✱ | `-o` | Output CHD file |
| `--input` ✱ | `-i` | Input file: `.cue`, `.gdi`, `.iso`, `.toc`, `.nrg`, `.cdr`, `.toast` |
| `--outputparent` | `-op` | Output parent CHD |
| `--force` | `-f` | Overwrite existing output |
| `--hunksize` | `-hs` | Hunk size in bytes (default: 19584 = 8 × 2448) |
| `--compression` | `-c` | Codecs (default: `cdlz,cdzl,cdfl`) |
| `--numprocessors` | `-np` | Parallel workers |
| `--verbose` | `-v` | Per-hunk compression logging |

#### createdvd

| Option | Short | Description |
|--------|-------|-------------|
| `--output` ✱ | `-o` | Output CHD file |
| `--input` ✱ | `-i` | Input file (raw binary, size divisible by 2048) |
| `--outputparent` | `-op` | Output parent CHD |
| `--force` | `-f` | Overwrite existing output |
| `--inputstartbyte` | `-isb` | Starting byte offset within input |
| `--inputstarthunk` | `-ish` | Starting hunk offset within input |
| `--inputbytes` | `-ib` | Effective length of input in bytes |
| `--inputhunks` | `-ih` | Effective length of input in hunks |
| `--hunksize` | `-hs` | Hunk size in bytes |
| `--compression` | `-c` | Codecs (default: `lzma,zlib,huff,flac`) |
| `--numprocessors` | `-np` | Parallel workers |
| `--verbose` | `-v` | Per-hunk compression logging |

#### createld

| Option | Short | Description |
|--------|-------|-------------|
| `--output` ✱ | `-o` | Output CHD file |
| `--input` ✱ | `-i` | Input AVI file |
| `--outputparent` | `-op` | Output parent CHD |
| `--force` | `-f` | Overwrite existing output |
| `--inputstartframe` | `-isf` | Starting frame within input |
| `--inputframes` | `-if` | Effective length of input in frames |
| `--hunksize` | `-hs` | Hunk size in bytes |
| `--compression` | `-c` | Codecs (default: `avhu`) |
| `--numprocessors` | `-np` | Parallel workers |
| `--verbose` | `-v` | Per-hunk compression logging |

#### extractraw / extracthd / extractdvd

All three share the same option set.

| Option | Short | Description |
|--------|-------|-------------|
| `--output` ✱ | `-o` | Output file |
| `--input` ✱ | `-i` | Input CHD file |
| `--inputparent` | `-ip` | Parent CHD file |
| `--force` | `-f` | Overwrite existing output |
| `--inputstartbyte` | `-isb` | Starting byte offset within input |
| `--inputstarthunk` | `-ish` | Starting hunk offset within input |
| `--inputbytes` | `-ib` | Effective length of input in bytes |
| `--inputhunks` | `-ih` | Effective length of input in hunks |

#### extractcd

| Option | Short | Description |
|--------|-------|-------------|
| `--output` ✱ | `-o` | Output CUE file |
| `--input` ✱ | `-i` | Input CHD file |
| `--outputbin` | `-ob` | Output BIN file name (supports `%t` track-number variables, e.g. `trk%02t.bin`) |
| `--splitbin` | `-sb` | Output one binary file per track |
| `--inputparent` | `-ip` | Parent CHD file |
| `--force` | `-f` | Overwrite existing output |
| `--cooked` | — | Force cooked (2048-byte) sectors (default) |
| `--raw`, `--raw-frames` | — | Write full 2448-byte raw frames |

#### extractld

| Option | Short | Description |
|--------|-------|-------------|
| `--output` ✱ | `-o` | Output AVI file |
| `--input` ✱ | `-i` | Input CHD file |
| `--inputparent` | `-ip` | Parent CHD file |
| `--force` | `-f` | Overwrite existing output |
| `--inputstartframe` | `-isf` | Starting frame within input |
| `--inputframes` | `-if` | Effective length of input in frames |

#### copy

| Option | Short | Description |
|--------|-------|-------------|
| `--output` ✱ | `-o` | Output CHD file |
| `--input` ✱ | `-i` | Input CHD file |
| `--inputparent` | `-ip` | Parent CHD file for input |
| `--outputparent` | `-op` | Parent CHD file for output |
| `--force` | `-f` | Overwrite existing output |
| `--inputstartbyte` | `-isb` | Starting byte offset within input |
| `--inputstarthunk` | `-ish` | Starting hunk offset within input |
| `--inputbytes` | `-ib` | Effective length of input in bytes |
| `--inputhunks` | `-ih` | Effective length of input in hunks |
| `--hunksize` | `-hs` | Hunk size in bytes |
| `--compression` | `-c` | Codecs (default: per input CHD type) |
| `--numprocessors` | `-np` | Parallel workers |
| `--no-upgrade` | — | Preserve legacy metadata tags |
| `--verbose` | `-v` | Per-hunk compression logging |

#### addmeta

| Option | Short | Description |
|--------|-------|-------------|
| `--input` ✱ | `-i` | Input CHD file |
| `--tag` ✱ | `-t` | 4-character metadata tag |
| `--index` | `-ix` | Indexed instance of this tag (default: 0) |
| `--valuetext` | `-vt` | Text for the metadata |
| `--valuefile` | `-vf` | File containing data to add |
| `--nochecksum` | `-nocs` | Exclude this metadata from the overall SHA-1 |

#### delmeta

| Option | Short | Description |
|--------|-------|-------------|
| `--input` ✱ | `-i` | Input CHD file |
| `--tag` ✱ | `-t` | 4-character metadata tag |
| `--index` | `-ix` | Indexed instance of this tag (default: 0) |

#### dumpmeta

| Option | Short | Description |
|--------|-------|-------------|
| `--input` ✱ | `-i` | Input CHD file |
| `--tag` ✱ | `-t` | 4-character metadata tag |
| `--output` | `-o` | Output file for binary data (default: stdout) |
| `--force` | `-f` | Overwrite existing output |
| `--index` | `-ix` | Indexed instance of this tag (default: 0) |

#### listtemplates

Takes no options. Prints the built-in hard disk geometry templates.

#### hash

| Option | Short | Description |
|--------|-------|-------------|
| `--input` ✱ | `-i` | Input CHD file |
| `--hashes` | — | Comma-separated hash types: `sha1`, `sha256`, `crc32`, `xxh3` (default: `sha1`) |
| `--result` | — | Output format: `text`, `json`, `sfv` (default: `text`) |
| `--tracks` | — | Compute per-track hashes (CD only) |

#### batch

| Option | Short | Description |
|--------|-------|-------------|
| `--input` ✱ | `-i` | Input directory |
| `--output` ✱ | `-o` | Output directory |
| `--action` | — | `extract` (default) or `create` |
| `--compression` | `-c` | Codecs for create mode |

#### random / list / parent / toc / cue / classify / detect

All take the file (or files) as positional arguments or via `--input`/`-i`:

| Command | Arguments |
|---------|-----------|
| `random <file.chd>` | Random-access read test |
| `list <listfile.txt>` | Verify every `.chd` path listed in a text file |
| `parent <child.chd> <parent.chd>` | Verify a child CHD against its parent |
| `toc <file.chd>` | Print table-of-contents for a CD/GD-ROM CHD |
| `cue <file.chd> [binfile]` | Generate a CUE sheet (optional BIN name) |
| `classify <file.chd>` | Classify CHD media type |
| `detect <file>` | Detect game platform |

### Accepted Input File Types

| Command | Accepted Formats |
|---------|-----------------|
| `createcd` | `.cue`, `.gdi`, `.iso`, `.nrg`, `.toc`, `.cdr`, `.toast` |
| `createdvd` | `.iso` (any raw binary; must be divisible by 2048) |
| `createraw` | Any raw binary file |
| `createhd` | Any raw binary file, or omit `--input` for blank |
| `createld` | `.avi` |

---

## Additional Features (beyond chdman)

These commands are CHDSharp extensions not found in `chdman`:

```bash
# Verify all .chd files in directories (recursive)
CHDSharp D:\CHD
CHDSharp D:\CHD E:\MoreCHDs

# Random-access read test
CHDSharp --random game.chd

# Verify paths from a text file
CHDSharp --list chd_paths.txt

# Verify a child CHD against its parent
CHDSharp --parent child.chd parent.chd

# Print CD/GD-ROM table of contents
CHDSharp --toc game.chd

# Generate CUE sheet
CHDSharp --cue game.chd [optional.bin]

# Classify CHD media type
CHDSharp --classify game.chd

# Detect game platform
CHDSharp --detect game.chd

# Compute content hashes (SHA-1, SHA-256, CRC-32, XXH3)
CHDSharp --hash game.chd --hashes sha1,sha256,crc32 --result json --tracks

# Batch extract/create
CHDSharp --batch input-dir output-dir --action extract
CHDSharp --batch input-dir output-dir --action create -c zstd
```

---

## Double-Click Behavior

When launched by double-clicking the executable (no arguments), the application stays open and displays the help text. Press any key to exit.

---

## Building

```bash
# Build (requires .NET 8.0+ SDK)
dotnet build CHDSharpCli/CHDSharpCli.csproj -c Release

# Run
dotnet run --project CHDSharpCli -- info -i game.chd

# Run the built executable (the binary is named CHDSharp)
CHDSharpCli/bin/Release/net8.0/CHDSharp.exe info -i game.chd
```

### Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| [Serilog](https://www.nuget.org/packages/Serilog/) | 4.4.0 | Structured logging |
| [Serilog.Extensions.Logging](https://www.nuget.org/packages/Serilog.Extensions.Logging/) | 10.0.0 | Bridges Serilog to `ILoggerFactory` |
| [Serilog.Sinks.Console](https://www.nuget.org/packages/Serilog.Sinks.Console/) | 6.1.1 | Console log output |
| `CHDSharpLib` | (project reference) | Core CHD library — see its LICENSE.txt |

---

## License

This CLI builds on `CHDSharpLib`. It is a combined work: the project code is **MIT**;
`VendoredFlac` (in `CHDSharpLib`) is **LGPL-2.1**; `VendoredZLib` is **zlib-licensed**;
`VendoredLZMA` is **public domain**; `VendoredZSTD` is **MIT** (based on Facebook zstd,
BSD-3-Clause). See [LICENSE.txt](LICENSE.txt) for the full third-party notice and obligations.
