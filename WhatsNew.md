# CHDSharp v1.7.3 - What's New

**Released:** 2026-11-26 - [Full changelog](docs/ReleaseNotes.md) -
[NuGet](https://www.nuget.org/packages/CHDSharp/1.7.3) -
[NuGet CLI](https://www.nuget.org/packages/CHDSharp.Cli/1.7.3)

CHDSharp v1.7.3 is a tooling release. The CLI is also published as the `CHDSharp.Cli` .NET tool,
Windows bundles have winget manifests, and CI verifies and smoke-tests the tool package. No public
API or `chdman`-visible encoder output changed.

---

## Download (CLI)

| Platform | Bundle |
|----------|--------|
| Windows x64 | [`CHDSharp_win-x64_v1.7.3.zip`](https://github.com/purelogiccode/CHDSharp/releases/download/v1.7.3/CHDSharp_win-x64_v1.7.3.zip) |
| Windows ARM64 | [`CHDSharp_win-arm64_v1.7.3.zip`](https://github.com/purelogiccode/CHDSharp/releases/download/v1.7.3/CHDSharp_win-arm64_v1.7.3.zip) |
| Linux x64 | [`CHDSharp_linux-x64_v1.7.3.tar.gz`](https://github.com/purelogiccode/CHDSharp/releases/download/v1.7.3/CHDSharp_linux-x64_v1.7.3.tar.gz) |
| Linux ARM64 | [`CHDSharp_linux-arm64_v1.7.3.tar.gz`](https://github.com/purelogiccode/CHDSharp/releases/download/v1.7.3/CHDSharp_linux-arm64_v1.7.3.tar.gz) |
| macOS Intel | [`CHDSharp_osx-x64_v1.7.3.dmg`](https://github.com/purelogiccode/CHDSharp/releases/download/v1.7.3/CHDSharp_osx-x64_v1.7.3.dmg) |
| macOS Apple Silicon | [`CHDSharp_osx-arm64_v1.7.3.dmg`](https://github.com/purelogiccode/CHDSharp/releases/download/v1.7.3/CHDSharp_osx-arm64_v1.7.3.dmg) |

Every bundle contains a **framework-dependent single-file** `CHDSharp` / `CHDSharp.exe`
build (the .NET runtime is **not** embedded) plus `LICENSE.txt`, `README.md`, `README.txt`
and this document. The [.NET 11 runtime](https://dotnet.microsoft.com/download/dotnet/11.0)
is required.

The CLI is also available as a .NET tool on Windows, Linux and macOS:

```bash
dotnet tool install --global CHDSharp.Cli
chdsharp help
```

Windows users can also install the CLI with winget. The `PureLogicCode.CHDSharp` manifest is
submitted to `microsoft/winget-pkgs`; the command becomes available once Microsoft has reviewed it:

```powershell
winget install PureLogicCode.CHDSharp
```

---

## CLI as a .NET tool

The `CHDSharp.Cli` package targets `net8.0`, `net9.0`, `net10.0` and `net11.0`, bundles every
managed dependency under `tools/<tfm>/any`, and exposes the `chdsharp` command. The release
workflow packs, pushes and attests it independently of the library package.

```bash
dotnet tool install --global CHDSharp.Cli   # install
dotnet tool update  --global CHDSharp.Cli   # update
dotnet tool uninstall --global CHDSharp.Cli # remove
```

## Windows winget manifest

The Windows CLI bundle now has a winget manifest (`PureLogicCode.CHDSharp`) generated from the
published `.zip` assets and submitted to `microsoft/winget-pkgs`.
`winget install PureLogicCode.CHDSharp` becomes available after Microsoft's review.

## CI and test stability

- The NuGet package job now verifies the tool layout for every target framework and smoke-installs
  the freshly packed tool (`chdsharp help`) before uploading the packages. This also fixes the
  `unzip` glob that matched both `CHDSharp.*.nupkg` and `CHDSharp.Cli.*.nupkg`.
- The logger-capture tests run in a non-parallel xUnit collection behind a process-wide gate, so
  replacing `Chd.LoggerFactory` cannot race another test (the Ubuntu `net10.0` flake).
- `InlineMethod.Fody` 0.8.7 -> 0.8.8.

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
- Existing `CHDSharp.Cli` .NET tool installs update with
  `dotnet tool update --global CHDSharp.Cli`.
- The CLI update check expects the same asset names as v1.7.2, with the version bumped to
  v1.7.3. Older releases remain downloadable from the
  [releases page](https://github.com/purelogiccode/CHDSharp/releases).
