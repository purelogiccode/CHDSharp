# CHDSharp v1.6.0 - What's New

**Released:** 2026-10-29 - [Full changelog](docs/ReleaseNotes.md) - [NuGet](https://www.nuget.org/packages/CHDSharp/1.6.0)

CHDSharp v1.6.0 is a reliability and supply-chain release: read-ahead teardown no longer blocks on
in-flight I/O, truncated CD sources report exactly where the layout ran out, and the repository
gained a full CI gate with CodeQL, Dependency Review, OpenSSF Scorecard, Dependabot, NuGet audit,
SHA-pinned actions and provenance-attested release assets.

---

## Download (CLI)

| Platform | Bundle |
|----------|--------|
| Windows x64 | [`CHDSharp_win-x64_v1.6.0.zip`](https://github.com/purelogiccode/CHDSharp/releases/download/v1.6.0/CHDSharp_win-x64_v1.6.0.zip) |
| Windows ARM64 | [`CHDSharp_win-arm64_v1.6.0.zip`](https://github.com/purelogiccode/CHDSharp/releases/download/v1.6.0/CHDSharp_win-arm64_v1.6.0.zip) |
| Linux x64 | [`CHDSharp_linux-x64_v1.6.0.zip`](https://github.com/purelogiccode/CHDSharp/releases/download/v1.6.0/CHDSharp_linux-x64_v1.6.0.zip) |
| Linux ARM64 | [`CHDSharp_linux-arm64_v1.6.0.zip`](https://github.com/purelogiccode/CHDSharp/releases/download/v1.6.0/CHDSharp_linux-arm64_v1.6.0.zip) |
| macOS Intel | [`CHDSharp_osx-x64_v1.6.0.zip`](https://github.com/purelogiccode/CHDSharp/releases/download/v1.6.0/CHDSharp_osx-x64_v1.6.0.zip) |
| macOS Apple Silicon | [`CHDSharp_osx-arm64_v1.6.0.zip`](https://github.com/purelogiccode/CHDSharp/releases/download/v1.6.0/CHDSharp_osx-arm64_v1.6.0.zip) |

Every bundle contains a **framework-dependent single-file** `CHDSharp` / `CHDSharp.exe`
build (the .NET runtime is **not** embedded) plus `LICENSE.txt`, `README.md`, `README.txt`
and this document. The [.NET 10 runtime](https://dotnet.microsoft.com/download/dotnet/10.0)
is required.

---

## Fixed: read-ahead disposal no longer blocks or leaks permits

`ChdFile.Dispose()` and `ConfigureReadAhead(0)` used to wait for every in-flight read-ahead read.
A stream that ignored cancellation could stall teardown indefinitely. The manager now cancels,
returns immediately, and lets the last background task dispose its semaphore and token source.
In-flight hunks are deduplicated, so two tasks can no longer race to cache the same hunk and leak a
permit. Shutdown cancellation is no longer logged as a decompression failure (important because
Warning+ events are forwarded to the CLI bug-report sink).

## Fixed: truncated CD sources are diagnosable

A short read while encoding a CD now reports the track, frame within the track, logical frame,
source offset (hex and decimal), sector size, bytes needed/read and the file length. Split-pregap
reads name the next track's file only when that file was actually used.

## Full CI and security (new in v1.6.0)

- **`CI gate`** - three-OS build/test across .NET 8/9/10, InspectCode, NuGet packaging, merged
  coverage, a Jekyll docs build and a quick `chdman` parity battle.
- **CodeQL** for C# and GitHub Actions, **Dependency Review** on pull requests, and
  **OpenSSF Scorecard** results published to code scanning.
- **Dependabot** alerts, security updates and grouped version updates; **NuGet audit** of direct
  and transitive packages, with `-warnaserror` in CI.
- **Secret scanning** with push protection, **private vulnerability reporting**, read-only default
  workflow permissions, mandatory full-SHA action pinning and artifact provenance attestations on
  releases.
- **`SECURITY.md`** documents the reporting process and how to verify a release.

## Verification

- `dotnet test` across `net8.0`/`net9.0`/`net10.0`: **14,013/14,013 passed**
  (4,216 library tests + 455 encoder tests per target framework).
- Full battle harness: **3,174/3,174 checks**, byte-identical `chdman` output.
- `jb inspectcode CSharp_CHDSharp.sln --severity=WARNING`: **0 findings**.
- No public API changes - behavior fixes and repository infrastructure only.
