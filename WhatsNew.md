# CHDSharp v1.7.0 - What's New

**Released:** 2026-11-13 - [Full changelog](docs/ReleaseNotes.md) - [NuGet](https://www.nuget.org/packages/CHDSharp/1.7.0)

CHDSharp v1.7.0 moves the complete solution to **.NET 11** and **C# 15**, keeps the
`net8.0`/`net9.0` targets for current consumers, and switches the CLI bundles to each
platform's native archive format. No library API or encoder output changed.

---

## Download (CLI)

| Platform | Bundle |
|----------|--------|
| Windows x64 | [`CHDSharp_win-x64_v1.7.0.zip`](https://github.com/purelogiccode/CHDSharp/releases/download/v1.7.0/CHDSharp_win-x64_v1.7.0.zip) |
| Windows ARM64 | [`CHDSharp_win-arm64_v1.7.0.zip`](https://github.com/purelogiccode/CHDSharp/releases/download/v1.7.0/CHDSharp_win-arm64_v1.7.0.zip) |
| Linux x64 | [`CHDSharp_linux-x64_v1.7.0.tar.gz`](https://github.com/purelogiccode/CHDSharp/releases/download/v1.7.0/CHDSharp_linux-x64_v1.7.0.tar.gz) |
| Linux ARM64 | [`CHDSharp_linux-arm64_v1.7.0.tar.gz`](https://github.com/purelogiccode/CHDSharp/releases/download/v1.7.0/CHDSharp_linux-arm64_v1.7.0.tar.gz) |
| macOS Intel | [`CHDSharp_osx-x64_v1.7.0.dmg`](https://github.com/purelogiccode/CHDSharp/releases/download/v1.7.0/CHDSharp_osx-x64_v1.7.0.dmg) |
| macOS Apple Silicon | [`CHDSharp_osx-arm64_v1.7.0.dmg`](https://github.com/purelogiccode/CHDSharp/releases/download/v1.7.0/CHDSharp_osx-arm64_v1.7.0.dmg) |

Every bundle contains a **framework-dependent single-file** `CHDSharp` / `CHDSharp.exe`
build (the .NET runtime is **not** embedded) plus `LICENSE.txt`, `README.md`, `README.txt`
and this document. The [.NET 11 runtime](https://dotnet.microsoft.com/download/dotnet/11.0)
is required.

---

## .NET 11 and C# 15

- All 12 projects now multi-target `net8.0;net9.0;net10.0;net11.0`
  (`net11.0-windows` for the WPF tester) and compile with `LangVersion 15`.
- The NuGet package ships `lib/net11.0` assemblies for `CHDSharpLib` and all four vendored
  codec assemblies.
- CI, CodeQL, DocFX, the release pipeline and the documentation site all run on the .NET 11 SDK.
- The runtime packages (`Microsoft.Extensions.Logging.Abstractions`, `System.IO.Hashing`) and
  SourceLink are at 11.0.0/11.0.100 for every target. The packages ship `netstandard2.0` assets,
  so the `net8.0`/`net9.0` targets remain functional; their advisory TFM-support warnings are
  suppressed centrally.

## Native CLI bundles

- **Windows** stays `.zip`.
- **Linux** is now `.tar.gz`.
- **macOS** is now `.dmg` (built on a macOS runner; the disk image contains the binary and
  the documentation files).
- `VersionCheckService` looks up the correct asset extension for the running platform, and
  the release workflow builds each platform's bundles on its native runner before
  publishing them together.

## Tooling and dependencies

- Dependabot updates: GitHub Actions (`setup-dotnet 6.1.0`, `github/codeql-action 4.39.3`,
  `ruby/setup-ruby 1.338.0`), `Meziantou.Analyzer 3.0.339` and `xunit.v3 4.0.3`.
- The Pages workflow now uses the .NET 11 SDK, so the DocFX API reference keeps building
  against the `net11.0` target.
- Documentation, `WhatsNew.md`, the per-project READMEs and `AGENTS.md` describe the
  four target frameworks and the new bundle formats.

## Verification

- `dotnet build -c Release -warnaserror`: 0 warnings, 0 errors.
- `jb inspectcode --severity=WARNING --exclude="Vendored*/**"`: **0 findings**.
- Library tests: **4,220 passed** and encoder tests: **455 passed** on each of `net8.0`,
  `net9.0`, `net10.0` and `net11.0` (**18,700 tests total**).
- Quick `chdman` parity battle on `net11.0`: **905/905 checks passed**, byte-identical output.
- No public API changes and no encoder output changes.

## Upgrade notes

- Use the bundle matching your platform: `.zip` on Windows, `.tar.gz` on Linux and `.dmg` on
  macOS. On Linux run `chmod +x CHDSharp` after extracting if needed; on macOS drag `CHDSharp`
  out of the mounted disk image.
- The CLI's update check now expects the new asset names. Older releases remain downloadable
  from the [releases page](https://github.com/purelogiccode/CHDSharp/releases).
