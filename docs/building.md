---
layout: default
---

# Building

How to build, test, pack, and publish every component of the repository.

---

## Prerequisites

- [.NET SDK 11.0](https://dotnet.microsoft.com/download/dotnet/11.0) or later (the solution targets `net8.0`, `net9.0`, `net10.0`, and `net11.0`; the SDK's `rollForward` is set to `latestMajor` with prereleases allowed).
- Works on Windows, Linux, and macOS.
- No native toolchains are required — the library is 100% managed.

---

## Solution layout

```
CSharp_CHDSharp.sln
├── CHDSharpLib/          The library (NuGet package: CHDSharp)
├── CHDSharpCli/          Command-line CHD manager (binary: CHDSharp)
├── CHDSharpLib/Encoder/  Encoder subsystem (V5 CHD creation; see [Encoder](encoder.md))
├── CHDSharpTest/         xUnit unit + corpus tests
├── CHDSharpTestDataGeneration/  Deterministic corpus generator
├── CHDSharpTester/       WPF interactive tester
├── CHDSharpEncoderTest/  xUnit encoder tests (455 tests)
├── CHDSharpBattleTest/   Battle test harness (chdman vs CHDSharp)
├── VendoredZLib/         Pure C# zlib port
├── VendoredLZMA/         LZMA SDK C# port
├── VendoredFlac/         Pure C# FLAC encoder/decoder
├── VendoredZSTD/         Pure C# zstd 1.5.5 encoder/decoder (MAME parity)
└── Directory.Build.props Centralized versioning (1.7.0) and analyzer setup
```

---

## Build

```bash
# Everything (Debug)
dotnet build

# Release
dotnet build -c Release

# A single project
dotnet build CHDSharpLib/CHDSharpLib.csproj -c Release
```

### Centralized versioning

`Directory.Build.props` defines a single `<Version>` (currently `1.7.0`) that all projects inherit, so `AssemblyVersion`, `FileVersion`, and the NuGet package version stay in sync automatically.

### Code style & analyzers

All projects enable:

- `Nullable` + `ImplicitUsings`.
- `LangVersion 15`.
- [Meziantou.Analyzer](https://github.com/meziantou/Meziantou.Analyzer) (build-time analyzer, `PrivateAssets=all`).
- `CHDSharpLib` additionally treats missing XML documentation (`CS1591`) as an error, so **all public API members must carry XML doc comments**.

### Code inspections

The solution is kept **free of ReSharper warnings and errors**. The gate runs InspectCode from
the [JetBrains command-line tools](https://www.jetbrains.com/help/resharper/InspectCode.html)
(`jb`), from the repository root:

```bash
# The four vendored codec projects are excluded outright: they are checked in verbatim
# and editing them for style would risk the byte-for-byte chdman parity guarantee.
jb inspectcode CSharp_CHDSharp.sln --severity=WARNING --exclude="Vendored*/**" --output=inspectcode.xml
```

The inspection policy lives in the root `.editorconfig`:

- **The vendored codecs are excluded from InspectCode.** `VendoredZLib`, `VendoredLZMA`,
  `VendoredFlac` and `VendoredZSTD` (projects and sources) are third-party ports checked in
  verbatim; byte-for-byte `chdman` parity depends on them staying untouched. They are skipped by
  `--exclude="Vendored*/**"` in CI and by `.editorconfig`, and must never be edited to satisfy an
  inspection.
- **Consumer-facing models are exempt from "never used" inspections.** Public library records
  (`CHDSharpLib/Models`), the JSON corpus manifest
  (`CHDSharpTestDataGeneration/Models`) and the WPF data models (`CHDSharpTester/Models`) expose
  members that are only read by callers, serializers or XAML bindings.

> InspectCode 2026.2 logs internal `AssertionException`s while its nullable-flow analyzer walks
> the vendored ZSTD sources. This is a bug in the analyzer, not a finding: the report is still
> empty, and the `--exclude="Vendored*/**"` switch also silences the console.

---

## Test

```bash
# Run the full suite (all target frameworks)
dotnet test

# A single framework
dotnet test -f net11.0

# A specific test class
dotnet test --filter "FullyQualifiedName~CorpusTests"

# Verbose console output
dotnet test -v detailed
```

The suite contains **4,220 tests per target framework** (unit + corpus) that run against 30 deterministic CHD fixtures covering V1–V5 and every codec. See [Testing](testing.md).

The companion **encoder suite** (`CHDSharpEncoderTest`, 455 tests) validates CHD creation against `chdman.exe` — including 100 MB+ raw/CD round-trips:

```bash
dotnet test CHDSharpEncoderTest/ --filter "FullyQualifiedName~LargeFileValidationTests"
```

> **Regenerating the corpus** requires the vintage `chdman`/`hdcomp` binaries in `CHDSharpTest/chdman/`:
>
> ```bash
> dotnet run --project CHDSharpTestDataGeneration
> ```
>
> The generator is deterministic: regenerating produces byte-identical fixtures.

The **battle harness** (`CHDSharpBattleTest`) cross-checks the decoder and encoder against
`chdman.exe` (3,174/3,174 checks on the deterministic corpus; 3003/3003 on a real-world
scan of 56 CHDs), and can also scan any real-world CHD
folders via `--real <dir>`:

```bash
dotnet run --project CHDSharpBattleTest          # repo-root chdman.exe auto-resolved
dotnet run --project CHDSharpBattleTest -- --real "D:\CHD Collection"
```

---

## Pack (NuGet)

```bash
dotnet pack CHDSharpLib/CHDSharpLib.csproj -c Release
```

The package (`CHDSharp.<version>.nupkg`) is written to `CHDSharpLib/bin/Release/`. It includes:

- The library DLL for `net8.0`, `net9.0`, `net10.0`, and `net11.0`.
- The README (this wiki's sibling pages are referenced from it), the combined license file (`LICENSE.txt`), and the package icon.
- Embedded PDBs (`DebugType=embedded`) and embedded SourceLink for debugging.
- Deterministic, reproducible builds (`Deterministic=true`, `ContinuousIntegrationBuild` on GitHub Actions).

### Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| [Microsoft.Extensions.Logging.Abstractions](https://www.nuget.org/packages/Microsoft.Extensions.Logging.Abstractions/) | 11.0.0 (all TFMs) | Pluggable logging (optional) |
| [System.IO.Hashing](https://www.nuget.org/packages/System.IO.Hashing/) | 11.0.0 (all TFMs) | SHA1/MD5 hashing helpers |
| Microsoft.SourceLink.GitHub | 11.0.100 | SourceLink (build-time, `PrivateAssets=all`) |

The 11.0.0 packages target net10+, but ship `netstandard2.0` assets, so the `net8.0`/`net9.0`
targets remain functional; their advisory TFM-support warnings are suppressed in
`Directory.Build.props`.

All codec dependencies are vendored in-repo as project references and bundled inside the package (the runtime NuGet dependencies are the optional `Microsoft.Extensions.Logging.Abstractions` logging abstraction and `System.IO.Hashing`):

| Project | Purpose |
|---------|---------|
| `VendoredZLib` | Pure C# zlib (deflate/inflate) |
| `VendoredLZMA` | Pure C# LZMA SDK port |
| `VendoredFlac` | Pure C# FLAC encoder/decoder (based on CUETools.Flake) |
| `VendoredZSTD` | Pure C# zstd 1.5.5 encoder/decoder (C-to-C# port of MAME's bundled tree) |

`CHDSharpLib` also declares `InternalsVisibleTo` for `CHDSharpTest`, `CHDSharpTestDataGeneration` and `CHDSharpEncoderTest` so the test projects can exercise internal members directly.

---

## Publish the CLI (release bundles)

```bash
# Build every release bundle (win/linux/osx x x64/arm64) into CHDSharpLib/bin/Release/
pwsh scripts/package-release.ps1

# One platform, custom output
pwsh scripts/package-release.ps1 -Rids win-x64 -OutputPath dist

# Native formats: .zip on Windows, .tar.gz on Linux, .dmg on macOS
pwsh scripts/package-release.ps1 -Rids linux-x64,linux-arm64 -OutputPath dist
```

For every runtime identifier the script publishes the CLI as a **framework-dependent
single-file** binary (`--self-contained false -p:PublishSingleFile=true` — the .NET
runtime is *not* embedded), appends `LICENSE.txt`, `README.md`, `README.txt` and
`WhatsNew.md`, and writes the platform-native bundle: `CHDSharp_<rid>_v<version>.zip` on
Windows, `.tar.gz` on Linux and `.dmg` on macOS (the exact asset names the CLI's built-in
update check looks up on GitHub releases, e.g. `CHDSharp_linux-x64_v1.7.0.tar.gz`).
Existing files in the output path are never deleted. macOS `.dmg` bundles must be built on
macOS. The binary is named `CHDSharp` (`CHDSharp.exe` on Windows); bundles require the
.NET 11 runtime.

The `Release` GitHub Actions workflow (`.github/workflows/release.yml`) runs this script
on a version tag with one native runner per platform, then attests and publishes all six
assets in a final release job, pushes the NuGet package, and syncs the wiki — see
[CI](#ci).

---

## Documentation site (Jekyll + DocFX)

The published site at <https://purelogiccode.github.io/CHDSharp/> is the combination of two builds:

- the Jekyll guides in `docs/` (dark theme, sidebar, and the same pages mirrored to the wiki), and
- the DocFX API reference generated from `CHDSharpLib`'s XML documentation, published under
  [`/api/`](https://purelogiccode.github.io/CHDSharp/api/).

```bash
# Guides only
gem install jekyll -v 4.3.4 --no-document
jekyll build --source docs --destination _site --strict_front_matter

# Complete site: guides + API reference, with the CHDSharp theme applied
pwsh scripts/build-api-docs.ps1
```

DocFX is pinned in `.config/dotnet-tools.json`, so `dotnet tool restore` installs exactly the
version CI uses. `scripts/build-api-docs.ps1` runs `docfx metadata`, `docfx build` and appends
`docfx/theme.css` to the template stylesheet so the reference matches the guides. Generated
metadata (`docfx/*.yml`) and the rendered site (`_site/`) are git-ignored.

The `Pages` workflow (`.github/workflows/pages.yml`) builds both halves into one artifact and deploys
it with `actions/deploy-pages`. GitHub Pages uses `build_type: workflow`, so the wiki job no longer
requests a legacy Pages rebuild.

---

## CI

The repository is set up for GitHub Actions (`ContinuousIntegrationBuild=true`), producing deterministic builds and SourceLink-enabled symbols.

- **`ci.yml`** - the build + test gatekeeper, on every push to `main`, every pull request, and manual dispatch. The `CI gate` job aggregates:
  - `build & test` across Ubuntu, macOS and Windows: builds the full solution on Windows and the cross-platform subset elsewhere with `-warnaserror` (NuGet audit covers direct and transitive packages), then runs both xUnit suites (`CHDSharpTest` - 4,220 tests per target framework - and `CHDSharpEncoderTest` - 455 tests) for all four TFMs.
  - `quality`: `jb inspectcode --severity=WARNING`, failing on any finding and uploading SARIF to code scanning.
  - `package`: `dotnet pack` plus a `.nupkg` content check, uploaded as an artifact.
  - `coverage`: merged Cobertura coverage for both suites on `net11.0` (the wall-clock parallel-encode test is excluded because instrumentation invalidates its timing).
  - `docs build (Jekyll + DocFX)`: builds `docs/` with `--strict_front_matter`, then generates the DocFX API reference, so broken XML docs or a broken `docfx.json` fail the gate.
  - `chdman parity (smoke)`: the quick battle harness against the checked-in `CHDSharpTester/chdman.exe`.
  - `Wiki sync` (on `main` only, after the gate): mirrors `docs/` to the wiki. The site itself is deployed by `pages.yml`.

  Test-spawned CLI runs are isolated from the production bug-report API via `CHDSHARP_NO_BUG_REPORTS=1` / `CHDSHARP_NO_TELEMETRY=1`.
- **`codeql.yml`** - CodeQL C# analysis (`security-extended`) plus GitHub Actions analysis on `main`, pull requests, and a weekly schedule.
- **`dependency-review.yml`** - fails pull requests that add moderate-or-higher vulnerable dependencies.
- **`scorecard.yml`** - OpenSSF Scorecard, published to the public API and code scanning.
- **`release.yml`** - the tag-triggered release pipeline: builds the six platform CLI bundles on native runners (Windows `.zip`, Linux `.tar.gz`, macOS `.dmg`), attests their provenance, creates the GitHub release, publishes the NuGet package, and syncs this documentation to the wiki. See [Publish the CLI](#publish-the-cli-release-bundles).

Repository security settings (Dependabot alerts/security updates, secret scanning with push protection, private vulnerability reporting, read-only workflow permissions, and mandatory full-SHA action pinning) are described in [`SECURITY.md`](https://github.com/purelogiccode/CHDSharp/blob/main/SECURITY.md).
