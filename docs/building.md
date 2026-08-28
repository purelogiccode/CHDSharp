---
layout: default
---

# Building

How to build, test, pack, and publish every component of the repository.

---

## Prerequisites

- [.NET SDK 8.0](https://dotnet.microsoft.com/download/dotnet/8.0) or later (the solution targets `net8.0`, `net9.0`, and `net10.0`; the SDK's `rollForward` is set to `latestMajor` with prereleases allowed).
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
├── CHDSharpTestGen/      Deterministic corpus generator
├── CHDSharpTester/       WPF interactive tester
├── CHDSharpEncoderTest/  xUnit encoder tests (434 tests)
├── CHDSharpBattleTest/   Battle test harness (chdman vs CHDSharp)
├── VendoredZLib/         Pure C# zlib port
├── VendoredLZMA/         LZMA SDK C# port
├── VendoredFlac/         Pure C# FLAC encoder/decoder
├── VendoredZSTD/         Pure C# zstd 1.5.5 encoder/decoder (MAME parity)
└── Directory.Build.props Centralized versioning (1.4.1) and analyzer setup
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

`Directory.Build.props` defines a single `<Version>` (currently `1.4.1`) that all projects inherit, so `AssemblyVersion`, `FileVersion`, and the NuGet package version stay in sync automatically.

### Code style & analyzers

All projects enable:

- `Nullable` + `ImplicitUsings`.
- `LangVersion 14`.
- [Meziantou.Analyzer](https://github.com/meziantou/Meziantou.Analyzer) (build-time analyzer, `PrivateAssets=all`).
- `CHDSharpLib` additionally treats missing XML documentation (`CS1591`) as an error, so **all public API members must carry XML doc comments**.

---

## Test

```bash
# Run the full suite (all target frameworks)
dotnet test

# A single framework
dotnet test -f net10.0

# A specific test class
dotnet test --filter "FullyQualifiedName~CorpusTests"

# Verbose console output
dotnet test -v detailed
```

The suite contains **602 tests** (unit + corpus) that run against 30 deterministic CHD fixtures covering V1–V5 and every codec. See [Testing](testing.md).

The companion **encoder suite** (`CHDSharpEncoderTest`, 434 tests) validates CHD creation against `chdman.exe` — including 100 MB+ raw/CD round-trips:

```bash
dotnet test CHDSharpEncoderTest/ --filter "FullyQualifiedName~LargeFileValidationTests"
```

> **Regenerating the corpus** requires the vintage `chdman`/`hdcomp` binaries in `CHDSharpTest/chdman/`:
>
> ```bash
> dotnet run --project CHDSharpTestGen
> ```
>
> The generator is deterministic: regenerating produces byte-identical fixtures.

The **battle harness** (`CHDSharpBattleTest`) cross-checks the decoder and encoder against
`chdman.exe` (2611/2611 checks on the deterministic corpus; 3003/3003 on a real-world
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

- The library DLL for `net8.0`, `net9.0`, and `net10.0`.
- The README (this wiki's sibling pages are referenced from it), the combined license file (`LICENSE.txt`), and the package icon.
- Embedded PDBs (`DebugType=embedded`) and embedded SourceLink for debugging.
- Deterministic, reproducible builds (`Deterministic=true`, `ContinuousIntegrationBuild` on GitHub Actions).

### Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| [Microsoft.Extensions.Logging.Abstractions](https://www.nuget.org/packages/Microsoft.Extensions.Logging.Abstractions/) | 10.0.11 (all TFMs) | Pluggable logging (optional) |
| [System.IO.Hashing](https://www.nuget.org/packages/System.IO.Hashing/) | 10.0.11 (all TFMs) | SHA1/MD5 hashing helpers |
| Microsoft.SourceLink.GitHub | 10.0.400 | SourceLink (build-time, `PrivateAssets=all`) |

All codec dependencies are vendored in-repo as project references and bundled inside the package (the only runtime NuGet dependency is the optional `Microsoft.Extensions.Logging.Abstractions` logging abstraction):

| Project | Purpose |
|---------|---------|
| `VendoredZLib` | Pure C# zlib (deflate/inflate) |
| `VendoredLZMA` | Pure C# LZMA SDK port |
| `VendoredFlac` | Pure C# FLAC encoder/decoder (based on CUETools.Flake) |
| `VendoredZSTD` | Pure C# zstd 1.5.5 encoder/decoder (C-to-C# port of MAME's bundled tree) |

`CHDSharpLib` also declares `InternalsVisibleTo` for `CHDSharpTest` and `CHDSharpTestGen` so the test projects can exercise internal members directly.

---

## Publish the CLI

```bash
# Framework-dependent
dotnet publish CHDSharpCli/CHDSharpCli.csproj -c Release -r win-x64 --self-contained false

# Self-contained single-file
dotnet publish CHDSharpCli/CHDSharpCli.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

The repository ships ready-made zip bundles under `CHDSharpLib/bin/Release/` (e.g. `CHDSharp_win-x64_v1.4.1.zip`). The binary is named `CHDSharp` (e.g. `CHDSharp.exe` on Windows).

---

## CI

The repository is set up for GitHub Actions (`ContinuousIntegrationBuild=true`), producing deterministic builds and SourceLink-enabled symbols. The test suite is the gatekeeper: all 602 tests must pass on all three target frameworks before a release.
