# CHDSharp v1.7.2 - What's New

**Released:** 2026-11-24 - [Full changelog](docs/ReleaseNotes.md) - [NuGet](https://www.nuget.org/packages/CHDSharp/1.7.2)

CHDSharp v1.7.2 is a bug-fix release. It stops expected input diagnostics from being filed as
application defects while keeping them visible in the console and file logs. No public API or
`chdman`-visible encoder output changed.

---

## Download (CLI)

| Platform | Bundle |
|----------|--------|
| Windows x64 | [`CHDSharp_win-x64_v1.7.2.zip`](https://github.com/purelogiccode/CHDSharp/releases/download/v1.7.2/CHDSharp_win-x64_v1.7.2.zip) |
| Windows ARM64 | [`CHDSharp_win-arm64_v1.7.2.zip`](https://github.com/purelogiccode/CHDSharp/releases/download/v1.7.2/CHDSharp_win-arm64_v1.7.2.zip) |
| Linux x64 | [`CHDSharp_linux-x64_v1.7.2.tar.gz`](https://github.com/purelogiccode/CHDSharp/releases/download/v1.7.2/CHDSharp_linux-x64_v1.7.2.tar.gz) |
| Linux ARM64 | [`CHDSharp_linux-arm64_v1.7.2.tar.gz`](https://github.com/purelogiccode/CHDSharp/releases/download/v1.7.2/CHDSharp_linux-arm64_v1.7.2.tar.gz) |
| macOS Intel | [`CHDSharp_osx-x64_v1.7.2.dmg`](https://github.com/purelogiccode/CHDSharp/releases/download/v1.7.2/CHDSharp_osx-x64_v1.7.2.dmg) |
| macOS Apple Silicon | [`CHDSharp_osx-arm64_v1.7.2.dmg`](https://github.com/purelogiccode/CHDSharp/releases/download/v1.7.2/CHDSharp_osx-arm64_v1.7.2.dmg) |

Every bundle contains a **framework-dependent single-file** `CHDSharp` / `CHDSharp.exe`
build (the .NET runtime is **not** embedded) plus `LICENSE.txt`, `README.md`, `README.txt`
and this document. The [.NET 11 runtime](https://dotnet.microsoft.com/download/dotnet/11.0)
is required.

Windows users can also install the CLI with winget. The v1.7.2 manifest is submitted to
`microsoft/winget-pkgs`; the command becomes available once Microsoft has reviewed it:

```powershell
winget install PureLogicCode.CHDSharp
```

---

## Expected input diagnostics stay out of the Bug Report API

Verifying a folder that contains differential (child) CHDs and damaged images produced warnings
that were forwarded to the Bug Report API even though every file was handled exactly as expected:

- `Child CHD found, cannot be processed` - a child CHD opened without its parent
  (`Chderrrequiresparent`).
- `Header/map read failed: Chderrdecompressionerror` - a corrupt or truncated header/map.
- `Unknown version` - a file whose header advertises an unsupported CHD version.

The library now writes those diagnostics inside a `UserError` logging scope
(`ChdLogger.BeginUserErrorScope`). Serilog's Microsoft.Extensions.Logging bridge turns the scope
into the same `UserError` event property the CLI and Tester bug-report sinks already check, so the
warnings are still visible in the console and rolling file log but are no longer POSTed as
application defects. Decompression and full-image hash failures stay reportable, so real codec
defects are not hidden.

Regression tests: `CheckFile_ChildChd_TagsWarningAsUserError` and
`CheckFile_UnreadableMap_TagsWarningAsUserError`.

## Tooling and dependencies

- `Meziantou.Analyzer` 3.0.341 -> 3.0.342.
- GitHub Actions: `ruby/setup-ruby` 1.342.0, `actions/dependency-review-action` 5.0.1 and
  `ossf/scorecard-action` 2.6.0.

## Verification

- `dotnet build -c Release -warnaserror`: 0 warnings, 0 errors.
- `jb inspectcode CSharp_CHDSharp.sln --severity=WARNING`: **0 findings**.
- Library tests: **4,223 passed** and encoder tests: **456 passed** on each of `net8.0`,
  `net9.0`, `net10.0` and `net11.0` (**18,716 tests total**).
- CI quick `chdman` parity smoke: passed, byte-identical output.
- No public API changes and no encoder output changes.

## Upgrade notes

- Use the bundle matching your platform: `.zip` on Windows, `.tar.gz` on Linux and `.dmg` on
  macOS. On Linux run `chmod +x CHDSharp` after extracting if needed; on macOS drag `CHDSharp`
  out of the mounted disk image.
- The CLI update check expects the same asset names as v1.7.1, with the version bumped to
  v1.7.2. Older releases remain downloadable from the
  [releases page](https://github.com/purelogiccode/CHDSharp/releases).
