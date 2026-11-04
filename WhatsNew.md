# CHDSharp v1.6.3 - What's New

**Released:** 2026-11-03 - [Full changelog](docs/ReleaseNotes.md) - [NuGet](https://www.nuget.org/packages/CHDSharp/1.6.3)

CHDSharp v1.6.3 is a small diagnostics release. `createcd` now keeps expected malformed-input
failures out of the Bug Report API while preserving the exact `chdman`-compatible console text and
exit codes. The vendored codec libraries are explicitly documented as frozen, and no encoder output
changed.

---

## Download (CLI)

| Platform | Bundle |
|----------|--------|
| Windows x64 | [`CHDSharp_win-x64_v1.6.3.zip`](https://github.com/purelogiccode/CHDSharp/releases/download/v1.6.3/CHDSharp_win-x64_v1.6.3.zip) |
| Windows ARM64 | [`CHDSharp_win-arm64_v1.6.3.zip`](https://github.com/purelogiccode/CHDSharp/releases/download/v1.6.3/CHDSharp_win-arm64_v1.6.3.zip) |
| Linux x64 | [`CHDSharp_linux-x64_v1.6.3.zip`](https://github.com/purelogiccode/CHDSharp/releases/download/v1.6.3/CHDSharp_linux-x64_v1.6.3.zip) |
| Linux ARM64 | [`CHDSharp_linux-arm64_v1.6.3.zip`](https://github.com/purelogiccode/CHDSharp/releases/download/v1.6.3/CHDSharp_linux-arm64_v1.6.3.zip) |
| macOS Intel | [`CHDSharp_osx-x64_v1.6.3.zip`](https://github.com/purelogiccode/CHDSharp/releases/download/v1.6.3/CHDSharp_osx-x64_v1.6.3.zip) |
| macOS Apple Silicon | [`CHDSharp_osx-arm64_v1.6.3.zip`](https://github.com/purelogiccode/CHDSharp/releases/download/v1.6.3/CHDSharp_osx-arm64_v1.6.3.zip) |

Every bundle contains a **framework-dependent single-file** `CHDSharp` / `CHDSharp.exe`
build (the .NET runtime is **not** embedded) plus `LICENSE.txt`, `README.md`, `README.txt`
and this document. The [.NET 10 runtime](https://dotnet.microsoft.com/download/dotnet/10.0)
is required.

---

## Fixed: expected `createcd` input errors no longer become bug reports

A report showed `--createcd failed: "Unrecognized ISO sector size (...)"` being filed as an
application defect. That input is rejected by `chdman` too (`cdrom.cpp:2078`, `parse_iso`): a
raw ISO must be a multiple of 2048, 2336 or 2352 bytes.

- `createcd` now logs `InvalidDataException` (malformed ISO/CUE/GDI/TOC, truncated BIN/WAV,
  invalid source layout) and `FileNotFoundException` (missing referenced file) through
  `logger.UserError(...)`. The event stays at warning level in the console and rolling file logs,
  and the console text and exit code are unchanged, but it is no longer POSTed to the Bug Report
  API.
- Other `ArgumentException` / `IOException` / `UnauthorizedAccessException` failures still use
  `logger.Warning(...)` and are still reported.
- Regression test `Createcd_rejects_iso_size_that_is_not_sector_aligned` covers the reported
  non-sector-aligned ISO path.

## Documentation: vendored codecs are frozen

`AGENTS.md` now has a **Vendored codecs (frozen)** section: `VendoredFlac`, `VendoredLZMA`,
`VendoredZLib` and `VendoredZSTD` are kept byte-for-byte verbatim for `chdman` parity and may only
be edited when strictly necessary - never to satisfy an analyzer, formatter or InspectCode finding.
The existing InspectCode and `.editorconfig` exclusions remain in place.

## Verification

- `dotnet build -c Release`: 0 warnings, 0 errors.
- `jb inspectcode --severity=WARNING --exclude="Vendored*/**"`: **0 findings**; no vendored sources
  were modified.
- Library tests: **4,220 passed** and encoder tests: **455 passed** on each of `net8.0`, `net9.0`
  and `net10.0`.
- Quick `chdman` parity battle: **905/905 checks passed**, byte-identical output.
- No public API changes and no encoder output changes.
