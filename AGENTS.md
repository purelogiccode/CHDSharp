# AGENTS.md

Guidance for AI coding agents (and humans) working in **CHDSharp** — a pure C# reader/writer for
the CHD (Compressed Hunks of Data) disk-image format used by MAME.

## What this repo is

- `CHDSharpLib` (NuGet package `CHDSharp`, MIT) — V1–V5 reading, all 10 codecs, parent/child
  chains, parallel verification, and a full encoder.
- **The single most important invariant: byte-for-byte parity with MAME `chdman`** (MAME 0.289;
  `chdman.exe` is checked in at the repo root). Every encoder path (all codecs, CD/GD-ROM/DVD/
  laserdisc layouts, copy/partial-tail quirks) is verified to produce byte-identical output to
  `chdman`, and the CLI mirrors chdman's console output and exit codes. Never change encoder
  output bytes or chdman-visible CLI text unless the battle harness is run and stays green.
- Zero native dependencies: every codec is vendored in pure C# under `VendoredZLib`,
  `VendoredLZMA`, `VendoredFlac` (LGPL-2.1), and `VendoredZSTD`.

## Layout

| Path | What |
|---|---|
| `CHDSharpLib/` | Library (NuGet: CHDSharp) — `CHD.cs`, `CHDFile.cs`, `Encoder/`, `Models/`, `Utils/` |
| `CHDSharpCli/` | CLI binary named `CHDSharp` (chdman-compatible + extras); entry point `Program.cs` |
| `CHDSharpTest/`, `CHDSharpEncoderTest/` | xUnit v3 test suites |
| `CHDSharpBattleTest/` | chdman byte-parity battle harness (needs root `chdman.exe`) |
| `CHDSharpTester/` | Windows-only WPF tester (GPL-3.0-only) |
| `CHDSharpBenchmark/` | BenchmarkDotNet suite |
| `CHDSharpTestDataGeneration/` | Deterministic corpus generator |
| `docs/` | Jekyll documentation site (deployed to GitHub Pages) |
| `References/` | Local chdman reference material; excluded from builds |

## Build / test / run

Requires .NET SDK 8 or later (`global.json` rolls forward; targets `net8.0;net9.0;net10.0`,
LangVersion 14).

```bash
dotnet build -c Release                          # whole solution
dotnet test                                      # xUnit suites (Microsoft.Testing.Platform runner)
dotnet run --project CHDSharpBattleTest          # chdman byte-parity harness (long; needs chdman.exe)
dotnet run --project CHDSharpCli -- info -i game.chd
dotnet pack -c Release CHDSharpLib/              # NuGet package
```

- Keep builds warning-free: Meziantou.Analyzer and Roslynator.Analyzers run in every project.
- `CHDSharpLib` treats missing XML docs (CS1591) as errors — document every public API addition.
- Always run `dotnet test` before committing. Run the battle harness for changes touching
  encoding, decoding, CLI argument parsing, or anything chdman-visible.

## Logging & bug reports (behavior to preserve)

- The CLI and Tester configure Serilog at startup (`MinimumLevel.Debug`) and bridge the library
  with `Chd.LoggerFactory = new SerilogLoggerFactory(...)`; the logger is flushed on exit.
- CLI sinks: console + rolling-day file (`%LocalAppData%\CHDSharp\logs\chdsharp-<date>.log`) +
  `BugReportSink`. The sink POSTs **every Warning+ event** (except user cancellation) to
  `https://www.purelogiccode.com/bugreport/api/send-bug-report`.
- Every report must embed: `=== Environment Details ===` (Date, Application Name, Application
  Version, OS Version, Architecture, Bitness, Windows, Linux or MacOsX Version, Processor Count,
  Base Directory, Temp Path), `=== Error Details ===` (message), and `=== Exception Details ===`
  (Type, Message, Source, StackTrace). The format lives in `CHDSharpCli/BugReportSink.cs` /
  `EnvironmentSnapshot.cs` — keep it intact when editing.
- Delivery is best-effort: never throw into the logging pipeline; `Log.CloseAndFlush()` gives
  in-flight reports a bounded (5 s) grace period so short-lived CLI runs still deliver.
- Opt-outs (used by CI and by the test harness when it spawns the CLI):
  `CHDSHARP_NO_BUG_REPORTS=1` suppresses report delivery; `CHDSHARP_NO_TELEMETRY=1` skips the
  launch-stats hit and the GitHub version check.
- Route diagnostics through Serilog/`ILogger`, not `Console`/`Debug`. The only intentional
  `Console` writes are help/usage text, chdman-parity report output (`info`, errors on stderr),
  and the double-click "press any key" pause.

## Code conventions

- chdman parity is cited inline: comments reference `chdman.cpp:<line>` for ported behavior —
  keep those references when editing.
- Use `CultureInfo.InvariantCulture` for parsing/formatting (analyzer-enforced in many places).
- Nullable annotations are enabled; favor explicit argument validation.
- The version lives only in `Directory.Build.props`; bump it and add a `docs/ReleaseNotes.md`
  entry when preparing a release.
- Commit style: conventional prefixes — `feat:`, `fix:`, `docs:`, `bench:`, `release:`.
- Docs: `docs/*.md` (Jekyll, deployed via GitHub Pages), `docs/ReleaseNotes.md`, and the
  per-project READMEs — update them when behavior, CLI surface, or logging changes.
- Licensing is mixed — do not strip or alter license headers/notices: library MIT,
  `VendoredFlac` LGPL-2.1, `VendoredZLib` zlib, `VendoredLZMA` public domain, `VendoredZSTD`
  MIT, Tester GPL-3.0-only.

## CI

`.github/workflows/ci.yml` builds and tests on every push and pull request across Linux, macOS,
and Windows (matrix). Each OS job installs the .NET 8/9/10 SDKs and runs both xUnit suites
(`CHDSharpTest`, `CHDSharpEncoderTest`) for all three target frameworks. The CLI is built first
because `CHDSharpTest` spawns `CHDSharpCli/bin/<config>/<tfm>/CHDSharp.dll`. The Windows job also
builds the full solution (WPF tester, battle harness, benchmark). Tests that need `chdman` no-op
when it is absent, so chdman validation only actually runs on Windows CI (the exe is copied from
`CHDSharpTester/`); the battle harness itself is not part of CI. CI sets
`CHDSHARP_NO_BUG_REPORTS=1`/`CHDSHARP_NO_TELEMETRY=1` so test-spawned CLI runs never reach the
production bug-report API or usage stats. GitHub Pages deploys `docs/` separately via the
repository-managed `pages-build-deployment` workflow.
