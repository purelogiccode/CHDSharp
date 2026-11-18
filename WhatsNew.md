# CHDSharp v1.7.1 - What's New

**Released:** 2026-11-18 - [Full changelog](docs/ReleaseNotes.md) - [NuGet](https://www.nuget.org/packages/CHDSharp/1.7.1)

CHDSharp v1.7.1 is a bug-fix release. It stops empty CLI paths from crashing and restores
FLAC encoding for CD audio whose frames need more than 64 bytes of header metadata. No public
API or `chdman`-visible encoder output changed.

---

## Download (CLI)

| Platform | Bundle |
|----------|--------|
| Windows x64 | [`CHDSharp_win-x64_v1.7.1.zip`](https://github.com/purelogiccode/CHDSharp/releases/download/v1.7.1/CHDSharp_win-x64_v1.7.1.zip) |
| Windows ARM64 | [`CHDSharp_win-arm64_v1.7.1.zip`](https://github.com/purelogiccode/CHDSharp/releases/download/v1.7.1/CHDSharp_win-arm64_v1.7.1.zip) |
| Linux x64 | [`CHDSharp_linux-x64_v1.7.1.tar.gz`](https://github.com/purelogiccode/CHDSharp/releases/download/v1.7.1/CHDSharp_linux-x64_v1.7.1.tar.gz) |
| Linux ARM64 | [`CHDSharp_linux-arm64_v1.7.1.tar.gz`](https://github.com/purelogiccode/CHDSharp/releases/download/v1.7.1/CHDSharp_linux-arm64_v1.7.1.tar.gz) |
| macOS Intel | [`CHDSharp_osx-x64_v1.7.1.dmg`](https://github.com/purelogiccode/CHDSharp/releases/download/v1.7.1/CHDSharp_osx-x64_v1.7.1.dmg) |
| macOS Apple Silicon | [`CHDSharp_osx-arm64_v1.7.1.dmg`](https://github.com/purelogiccode/CHDSharp/releases/download/v1.7.1/CHDSharp_osx-arm64_v1.7.1.dmg) |

Every bundle contains a **framework-dependent single-file** `CHDSharp` / `CHDSharp.exe`
build (the .NET runtime is **not** embedded) plus `LICENSE.txt`, `README.md`, `README.txt`
and this document. The [.NET 11 runtime](https://dotnet.microsoft.com/download/dotnet/11.0)
is required.

---

## Empty CLI paths report the usage error

`extractcd -o ""` passed the old null check, reached
`Path.GetDirectoryName("")` / `Directory.CreateDirectory("")` and crashed with an unhandled
`ArgumentException`. An explicitly empty `--input` or `--output` is now treated as a missing
path for every command that uses the shared create/extract argument parser, so the CLI prints
the normal `... requires --input <file> --output <file>` usage text and exits with code 1.

Regression test: `Extractcd_reports_usage_error_for_an_empty_output_path`.

## FLAC frames have enough header room

`CdflCodec` reserved only 64 bytes per FLAC frame for the frame header, subframe headers,
Rice partition parameters and footer. Real CD audio using Fixed subframes can need 67 bytes,
which made a valid one-frame-per-hunk source fail with
`FLAC frame too large: 2419 bytes (buffer 2416)`.

- `CdflCodec` now reserves 512 bytes of metadata per frame.
- `AvHuffEncoder` does the same for each laserdisc audio channel's mono FLAC frame.
- The allowance is only headroom: the encoded bytes are unchanged for every hunk that already
  compressed, and the existing `total >= hunk size` check still decides whether a hunk is
  stored compressed.

Regression test: `CdflCodec_OneFrameHunk_EncodesWithoutOverflowingTheFrameBuffer`.

## Tooling and dependencies

- `Meziantou.Analyzer` 3.0.339 -> 3.0.341.
- `coverlet.collector` 10.1.0 -> 10.1.1.
- `Serilog.Extensions.Logging` 10.0.0 -> 11.0.0.

## Verification

- `dotnet build -c Release -warnaserror`: 0 warnings, 0 errors.
- `jb inspectcode CSharp_CHDSharp.sln --severity=WARNING`: **0 findings**.
- Library tests: **4,221 passed** and encoder tests: **456 passed** on each of `net8.0`,
  `net9.0`, `net10.0` and `net11.0` (**18,708 tests total**).
- CI quick `chdman` parity smoke: passed, byte-identical output.
- No public API changes and no encoder output changes.

## Upgrade notes

- Use the bundle matching your platform: `.zip` on Windows, `.tar.gz` on Linux and `.dmg` on
  macOS. On Linux run `chmod +x CHDSharp` after extracting if needed; on macOS drag `CHDSharp`
  out of the mounted disk image.
- The CLI update check expects the same asset names as v1.7.0, with the version bumped to
  v1.7.1. Older releases remain downloadable from the
  [releases page](https://github.com/purelogiccode/CHDSharp/releases).
