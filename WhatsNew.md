# CHDSharp v1.5.2 - What's New

**Released:** 2026-10-21 - [Full changelog](docs/ReleaseNotes.md) - [NuGet](https://www.nuget.org/packages/CHDSharp/1.5.2)

CHDSharp v1.5.2 rolls up the v1.5.1 cue-layout parity fixes (this is the first release that ships
the CLI bundles) and adds a repo-wide code-quality pass plus fully automated wiki and
documentation-site deployment.

---

## Download (CLI)

| Platform | Bundle |
|----------|--------|
| Windows x64 | [`CHDSharp_win-x64_v1.5.2.zip`](https://github.com/purelogiccode/CHDSharp/releases/download/v1.5.2/CHDSharp_win-x64_v1.5.2.zip) |
| Windows ARM64 | [`CHDSharp_win-arm64_v1.5.2.zip`](https://github.com/purelogiccode/CHDSharp/releases/download/v1.5.2/CHDSharp_win-arm64_v1.5.2.zip) |
| Linux x64 | [`CHDSharp_linux-x64_v1.5.2.zip`](https://github.com/purelogiccode/CHDSharp/releases/download/v1.5.2/CHDSharp_linux-x64_v1.5.2.zip) |
| Linux ARM64 | [`CHDSharp_linux-arm64_v1.5.2.zip`](https://github.com/purelogiccode/CHDSharp/releases/download/v1.5.2/CHDSharp_linux-arm64_v1.5.2.zip) |
| macOS Intel | [`CHDSharp_osx-x64_v1.5.2.zip`](https://github.com/purelogiccode/CHDSharp/releases/download/v1.5.2/CHDSharp_osx-x64_v1.5.2.zip) |
| macOS Apple Silicon | [`CHDSharp_osx-arm64_v1.5.2.zip`](https://github.com/purelogiccode/CHDSharp/releases/download/v1.5.2/CHDSharp_osx-arm64_v1.5.2.zip) |

Every bundle contains a **framework-dependent single-file** `CHDSharp` / `CHDSharp.exe`
build (the .NET runtime is **not** embedded) plus `LICENSE.txt`, `README.md`, `README.txt`
and this document. The [.NET 10 runtime](https://dotnet.microsoft.com/download/dotnet/10.0)
is required.

---

## Fixed: `REM LEAD-OUT` at or before the session's last track

The parser compared the track's frame count with `leadOut - index00` using two `int` values.
MAME compares a `uint32_t` frame count against a signed `int32_t` end frame
(`cdrom.cpp:2748`), so a negative end frame converts to a huge unsigned value and the trim is
skipped. Cues that declare `REM LEAD-OUT 00:00:00` (or any address at or before the last track
of the session) therefore failed with:

```
Track 2: the session lead-out values leave a negative length (-11400 frames); the cue's
REM SESSION/LEAD-OUT values do not match the image.
```

while `chdman createcd` encoded the same cue, because the no-op lead-out leaves the cue's INDEX
positions in charge of the layout. The parser now performs the same unsigned comparison, so the
track layout and the CHD bytes match `chdman`.

## Fixed: a pad larger than its track zeroed the whole track

The CD reader treated the last `PadFrames` frames of a track as zero-filled padding. In chdman
the pad start is an unsigned offset from the track start (`chdman.cpp:471-472`); when the pad is
larger than the track the offset wraps below the track start, the pad region is unreachable, and
every frame is read from the source file. The encoder now mirrors that rule, and the GD-ROM
data/pad/split ordering follows the same frame layout (`splitStart = frames - splitFrames`,
`padStart = splitStart - padFrames`).

## Code quality (new in v1.5.2)

The whole solution is now clean under the ReSharper command-line inspector:
`jb inspectcode CSharp_CHDSharp.sln --severity=WARNING` reports **zero warnings or errors**
(308 before the cleanup). The policy lives in the root `.editorconfig`:

- **Vendored codecs are exempt and untouched.** `VendoredZLib`, `VendoredLZMA`, `VendoredFlac`
  and `VendoredZSTD` are third-party ports; byte-for-byte `chdman` parity depends on them staying
  verbatim, so their style/quality inspections are disabled instead of "fixed".
- **Consumer-facing models are exempt from "never used" inspections** - public library records,
  the JSON corpus manifest, and the WPF data models are read by callers, serializers and XAML
  bindings rather than by the declaring assembly.
- **First-party findings were fixed**: redundant null checks in the CLI encode paths, a write-only
  AVI stream field, and dead tester state (`LogEntry`/`LogEntries` and the unread
  `PerFileResult.FilePath`).

## CI, wiki & docs (new in v1.5.2)

- **Docs deploy from CI.** After the three-OS build/test matrix passes on a push to `main`, the
  CI workflow regenerates the GitHub wiki from `docs/` and requests a Pages rebuild. The release
  workflow does the same for tags, so the wiki and the documentation site stay in sync with the
  code.
- **Dark theme by default.** The documentation site uses a dark palette with a persisted
  light/dark toggle and `color-scheme` metadata.
- **Badges everywhere.** Every README (root, library, CLI, tests, generator, tester, benchmarks
  and `docs/`) starts with its CI/NuGet/platform badge row.
- **Release routine documented.** `AGENTS.md` now records the version bump, verification, tag,
  bundle and wiki/Pages steps used for this release.

## Verification

- New `chdman createcd` byte-parity fixtures: zero lead-out, lead-out before the track, and the
  pad-larger-than-track layout.
- Parser regression tests for both lead-out shapes.
- `CHDSharpEncoderTest` 454/454, `CHDSharpTest` 4214/4214, battle harness `--quick` 905/905 checks.
- Full `dotnet test` across `net8.0`/`net9.0`/`net10.0`: 14,004 passed, 0 failed; full battle
  harness: 3,174/3,174 checks, byte-identical `chdman` output.

## Upgrading

No public API changes. Replace the package; cues that encoded before produce the same bytes, and
the two layouts above now encode instead of throwing.
