# CHDSharp v1.5.0 — What's New

**Released:** 2026-10-16 · [Full changelog](docs/ReleaseNotes.md) · [NuGet](https://www.nuget.org/packages/CHDSharp/1.5.0)

CHDSharp v1.5.0 is the first release produced end-to-end by the GitHub Actions pipeline:
one tag push builds the six platform bundles, creates the GitHub release, publishes the
NuGet package, and updates the [wiki](https://github.com/purelogiccode/CHDSharp/wiki) and
the [documentation site](https://purelogiccode.github.io/CHDSharp/).

---

## Download (CLI)

| Platform | Bundle |
|----------|--------|
| Windows x64 | [`CHDSharp_win-x64_v1.5.0.zip`](https://github.com/purelogiccode/CHDSharp/releases/download/v1.5.0/CHDSharp_win-x64_v1.5.0.zip) |
| Windows ARM64 | [`CHDSharp_win-arm64_v1.5.0.zip`](https://github.com/purelogiccode/CHDSharp/releases/download/v1.5.0/CHDSharp_win-arm64_v1.5.0.zip) |
| Linux x64 | [`CHDSharp_linux-x64_v1.5.0.zip`](https://github.com/purelogiccode/CHDSharp/releases/download/v1.5.0/CHDSharp_linux-x64_v1.5.0.zip) |
| Linux ARM64 | [`CHDSharp_linux-arm64_v1.5.0.zip`](https://github.com/purelogiccode/CHDSharp/releases/download/v1.5.0/CHDSharp_linux-arm64_v1.5.0.zip) |
| macOS Intel | [`CHDSharp_osx-x64_v1.5.0.zip`](https://github.com/purelogiccode/CHDSharp/releases/download/v1.5.0/CHDSharp_osx-x64_v1.5.0.zip) |
| macOS Apple Silicon | [`CHDSharp_osx-arm64_v1.5.0.zip`](https://github.com/purelogiccode/CHDSharp/releases/download/v1.5.0/CHDSharp_osx-arm64_v1.5.0.zip) |

Every bundle contains a **framework-dependent single-file** `CHDSharp` / `CHDSharp.exe`
build (the .NET runtime is **not** embedded) plus `LICENSE.txt`, `README.md`, `README.txt`
and this document. The [.NET 10 runtime](https://dotnet.microsoft.com/download/dotnet/10.0)
is required. On Linux/macOS run `chmod +x CHDSharp` if the binary is not executable after
unzipping.

---

## Fixed: built-in update check

The CLI's "a new version is available" notice now links **directly to the download for
your platform**. The bundle the release ships (`CHDSharp_<os>-<arch>_v<version>.zip`) is
exactly the name the update check looks up; previously it searched for the wrong asset
name built from the *currently running* version, so the direct link never resolved and
users were sent to the releases page instead.

## Release engineering

- **One tag, one release** — `Release` workflow: bundles (win/linux/osx × x64/arm64),
  GitHub release, NuGet push, wiki sync, Pages rebuild. The workflow refuses to run if
  the tag does not match `Directory.Build.props`.
- **NuGet publishing is idempotent** — re-running the workflow skips a version that is
  already on nuget.org.
- **GitHub releases now ship only the CLI** — the WPF Tester remains source-only.

## Also in this release

- **Wiki + documentation site in sync** — the `docs/` folder is deployed twice from CI:
  as the [documentation site](https://purelogiccode.github.io/CHDSharp/) and as the
  [GitHub wiki](https://github.com/purelogiccode/CHDSharp/wiki), both with the same side menu.
- **Dark theme by default** on the documentation site (with a persisted light/dark toggle).
- **Test harness hardening** — test-spawned CLI runs set `CHDSHARP_NO_BUG_REPORTS=1` /
  `CHDSHARP_NO_TELEMETRY=1`, so local full-suite runs no longer file bug reports.
- **Badges on every README** (CI, release, NuGet, platform, parity, license).

---

## Upgrading from v1.4.3 (or earlier)

v1.5.0 rolls up everything from v1.4.4 and v1.4.5:

- **Multisession cue parity** — ports MAME's `REM SESSION` / `REM LEAD-IN` / `REM LEAD-OUT`
  / session-pregap layout rules (IsoBuster/ImgBurn single-bin trim with zero-filled
  inter-session gaps, Redump/DiscImageCreator synthesized lead-in/out, `.img` heuristic).
  Multisession cues now encode **byte-identically** to `chdman createcd`.
- **Redump multi-cue Dreamcast GD-ROM parity** — cues with `REM SINGLE-DENSITY AREA` /
  `REM HIGH-DENSITY AREA` markers get the full `is_gdicue` layout (split pregap regions,
  LBA-45000 padding, `CHGD` metadata); verified byte-identical on a real 1.19 GB Redump
  multi-cue with a byte-perfect `chdman extractcd` round-trip.
- **cdrdao TOC `START` parity** — a bare `START` line (cdrdao's documented form) is
  accepted as "no pregap", matching MAME 0.289's `parse_toc` exactly.

---

## NuGet

```bash
dotnet add package CHDSharp --version 1.5.0
```

Targets `net8.0`, `net9.0` and `net10.0` — pure C#, zero native dependencies. Every
codec (zlib, lzma, huffman, flac, zstd, AVHuff and the CD variants) is implemented
in-repo and ships inside the package.
