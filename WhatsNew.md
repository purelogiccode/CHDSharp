# CHDSharp v1.6.2 - What's New

**Released:** 2026-11-02 - [Full changelog](docs/ReleaseNotes.md) - [NuGet](https://www.nuget.org/packages/CHDSharp/1.6.2)

CHDSharp v1.6.2 is a diagnostics and extraction-robustness release. Extracted-file failures keep
their real cause instead of being flattened to `Chderrwriteerror`, read-ahead teardown cannot race
a disposed semaphore, and damaged GD-ROM metadata returns an error instead of crashing. Encoder
output is unchanged and remains byte-identical to MAME `chdman` 0.289.

---

## Download (CLI)

| Platform | Bundle |
|----------|--------|
| Windows x64 | [`CHDSharp_win-x64_v1.6.2.zip`](https://github.com/purelogiccode/CHDSharp/releases/download/v1.6.2/CHDSharp_win-x64_v1.6.2.zip) |
| Windows ARM64 | [`CHDSharp_win-arm64_v1.6.2.zip`](https://github.com/purelogiccode/CHDSharp/releases/download/v1.6.2/CHDSharp_win-arm64_v1.6.2.zip) |
| Linux x64 | [`CHDSharp_linux-x64_v1.6.2.zip`](https://github.com/purelogiccode/CHDSharp/releases/download/v1.6.2/CHDSharp_linux-x64_v1.6.2.zip) |
| Linux ARM64 | [`CHDSharp_linux-arm64_v1.6.2.zip`](https://github.com/purelogiccode/CHDSharp/releases/download/v1.6.2/CHDSharp_linux-arm64_v1.6.2.zip) |
| macOS Intel | [`CHDSharp_osx-x64_v1.6.2.zip`](https://github.com/purelogiccode/CHDSharp/releases/download/v1.6.2/CHDSharp_osx-x64_v1.6.2.zip) |
| macOS Apple Silicon | [`CHDSharp_osx-arm64_v1.6.2.zip`](https://github.com/purelogiccode/CHDSharp/releases/download/v1.6.2/CHDSharp_osx-arm64_v1.6.2.zip) |

Every bundle contains a **framework-dependent single-file** `CHDSharp` / `CHDSharp.exe`
build (the .NET runtime is **not** embedded) plus `LICENSE.txt`, `README.md`, `README.txt`
and this document. The [.NET 10 runtime](https://dotnet.microsoft.com/download/dotnet/10.0)
is required.

---

## Fixed: extraction failures keep their real cause

Every exception raised while writing extracted data was folded into
`ChdError.Chderrwriteerror`, so a codec, path, permission or disk failure all looked the same and
callers could not tell a damaged CHD from an unwritable output volume. This is what produced the
opaque `track 3: Chderrwriteerror` reports in consuming applications.

- The extraction writers now log the underlying exception at warning level.
- `InvalidDataException` (data/codec failure) maps to `Chderrdecompressionerror`; output I/O maps to
  `Chderrwriteerror`.
- A regression test forces an unwritable target and asserts the warning and the returned code.

## Fixed: read-ahead teardown race

`ReadAheadManager.TryGet` and `Clear` can run while `Dispose` releases the semaphore. A permit
release after disposal is now ignored, so teardown cannot surface an unobserved
`ObjectDisposedException`.

## Fixed: damaged GD-ROM metadata

`ExtractToDirectoryWithReporting` returns `Chderrinvaliddata` when a GD-ROM CHD has no readable
tracks instead of dereferencing a null track list.

## Verification

- `dotnet build -c Release`: 0 warnings, 0 errors; `jb inspectcode --severity=WARNING`: 0 findings.
- Library tests: **4,219 passed**. Encoder tests: **455 passed**.
- Quick `chdman` parity battle: **3,174/3,174 checks passed**, byte-identical output.
- CD (V3/V4/V5, all CD codecs) and synthetic GD-ROM GDI extraction compared byte-for-byte with
  `chdman extractcd`.
- No public API changes - behavior and diagnostics only.