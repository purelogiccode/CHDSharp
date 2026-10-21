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
jb inspectcode CSharp_CHDSharp.sln --severity=WARNING   # ReSharper inspections (must be clean)
```

- Keep builds warning-free: Meziantou.Analyzer and Roslynator.Analyzers run in every project.
- `CHDSharpLib` treats missing XML docs (CS1591) as errors — document every public API addition.
- Keep ReSharper inspections clean too: `jb inspectcode CSharp_CHDSharp.sln --severity=WARNING`
  must report zero findings. The policy lives in the root `.editorconfig`: the vendored codec
  folders are exempt from style inspections and must stay verbatim for `chdman` parity — never
  "fix" a warning there, suppress the inspection instead. InspectCode 2026.2 logs a few internal
  `AssertionException`s while analyzing `VendoredZSTD` (an analyzer bug, not a finding); the
  report is still empty, and `--exclude="Vendored*/**/*.cs"` silences the console.
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
- Every `README.md` starts with its badge row. The docs site is dark by default via
  `docs/_layouts/default.html` + `docs/assets/css/style.css` (no Jekyll `theme:` — the custom
  layout must keep winning).
- Licensing is mixed — do not strip or alter license headers/notices: library MIT,
  `VendoredFlac` LGPL-2.1, `VendoredZLib` zlib, `VendoredLZMA` public domain, `VendoredZSTD`
  MIT, Tester GPL-3.0-only.

## Releasing a new version

The release is tag-driven: `release.yml` runs on `v*` tags and refuses to proceed unless the tag
matches `Directory.Build.props`.

1. **Bump the version** in `Directory.Build.props` (`Version`, `AssemblyVersion`, `FileVersion`).
2. **Update the release notes**:
   - `WhatsNew.md` — heading, release date, NuGet link and the six `v<version>` bundle links.
   - `docs/ReleaseNotes.md` — new `## CHDSharp v<version>` section at the top.
   - `README.md` and `CHDSharpLib/README.md` — new `## What's New in v<version>` block.
   - Per-project banners (`CHDSharpCli`, `CHDSharpTest`, `CHDSharpTestDataGeneration`).
   - `CHDSharpLib/CHDSharpLib.csproj` — `<PackageReleaseNotes>`.
   - `docs/building.md` — the "currently `<version>`" line and the example bundle name.
3. **Verify**:

   ```bash
   dotnet build -c Release
   dotnet test
   dotnet run --project CHDSharpBattleTest -c Release --framework net10.0
   jb inspectcode CSharp_CHDSharp.sln --severity=WARNING   # must report 0 findings
   ```

4. **Commit and push `main`**, then wait for `ci.yml` (three-OS build/test, then the `docs` job
   syncs the wiki and requests a Pages rebuild).
5. **Build the bundles locally** (optional before tagging, required when the user asks for them):

   ```bash
   pwsh scripts/package-release.ps1
   ```

   Writes `CHDSharp_<rid>_v<version>.zip` for win/linux/osx × x64/arm64 into
   `CHDSharpLib/bin/Release` (the default output path) without deleting other files there. Each
   bundle gets the single framework-dependent binary (the .NET runtime is **not** embedded) plus
   `LICENSE.txt`, `README.md`, `README.txt` and `WhatsNew.md`. Asset names must match
   `VersionCheckService.GetExpectedAssetName`:
   `CHDSharp_{win|linux|osx}-{x64|arm64}_v<version>.zip`.

6. **Tag and push**:

   ```bash
   git tag v1.5.2
   git push origin main v1.5.2
   ```

   `release.yml` verifies the tag against `Directory.Build.props`, rebuilds the six bundles,
   creates the GitHub release with `WhatsNew.md` as the body, uploads the assets, publishes the
   NuGet package (skipped when the version already exists), syncs the wiki and requests a Pages
   rebuild.

7. **Verify the release**:

   ```bash
   gh release view v1.5.2
   gh run list --workflow=release.yml --limit 1
   (Invoke-RestMethod https://api.nuget.org/v3-flatcontainer/chdsharp/index.json).versions
   ```

   NuGet can take a few minutes to index a freshly pushed package; the workflow logs
   `Your package was pushed.` when the push succeeded even if the version is not yet listed.

Required repository secrets: `NUGET_API_KEY` (nuget.org push) and `WIKI_TOKEN` (wiki push and
Pages rebuild request; needs `repo` scope).

## CI

`.github/workflows/ci.yml` builds and tests on every push and pull request across Linux, macOS,
and Windows (matrix). Each OS job installs the .NET 8/9/10 SDKs and runs both xUnit suites
(`CHDSharpTest`, `CHDSharpEncoderTest`) for all three target frameworks. The CLI is built first
because `CHDSharpTest` spawns `CHDSharpCli/bin/<config>/<tfm>/CHDSharp.dll`. The Windows job also
builds the full solution (WPF tester, battle harness, benchmark). Tests that need `chdman` no-op
when it is absent, so chdman validation only actually runs on Windows CI (the exe is copied from
`CHDSharpTester/`); the battle harness itself is not part of CI. CI sets
`CHDSHARP_NO_BUG_REPORTS=1`/`CHDSHARP_NO_TELEMETRY=1` so test-spawned CLI runs never reach the
production bug-report API or usage stats. On a push to `main`, a `docs` job (after the
build/test matrix) regenerates the GitHub wiki from `docs/` and requests a Pages rebuild; the
same steps run in `release.yml` for tags, so code, wiki and the documentation site stay in sync.
GitHub Pages is still served by the repository-managed `pages-build-deployment` workflow from
`main`/`docs` (build type `legacy`).
