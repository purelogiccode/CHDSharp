#Requires -Version 7.0
<#
.SYNOPSIS
    Builds the CHDSharp CLI release bundles: one framework-dependent single-file
    binary per platform plus LICENSE.txt, README.md, README.txt and WhatsNew.md.

.DESCRIPTION
    For every requested runtime identifier the CLI is published as a framework-dependent
    single-file executable - the .NET runtime itself is NOT embedded, so the bundle
    requires the .NET 10 runtime to be installed. Each bundle is written as
    CHDSharp_<rid>_v<version>.zip, the exact asset name VersionCheckService looks up on
    GitHub releases for the built-in update check (e.g. CHDSharp_win-x64_v1.5.0.zip).
    Existing files in the output path are never deleted.

.EXAMPLE
    pwsh scripts/package-release.ps1
    pwsh scripts/package-release.ps1 -Rids win-x64 -OutputPath dist
#>
[CmdletBinding()]
param(
    [string]$Version,
    [string]$OutputPath,
    [string]$Configuration = "Release",
    [string]$TargetFramework = "net10.0",
    [string[]]$Rids = @("win-x64", "win-arm64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64")
)

$ErrorActionPreference = "Stop"
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path

if (-not $Version) {
    $props = Get-Content (Join-Path $RepoRoot "Directory.Build.props") -Raw
    $versionMatch = [regex]::Match($props, "<Version>([^<]+)</Version>")
    if (-not $versionMatch.Success) { throw "Could not read <Version> from Directory.Build.props" }
    $Version = $versionMatch.Groups[1].Value
}
if (-not $OutputPath) { $OutputPath = Join-Path $RepoRoot "CHDSharpLib/bin/Release" }
$OutputPath = [IO.Path]::GetFullPath($OutputPath)

$cliProject = Join-Path $RepoRoot "CHDSharpCli/CHDSharpCli.csproj"
$license = Join-Path $RepoRoot "LICENSE.txt"
$readme = Join-Path $RepoRoot "README.md"
$whatsNew = Join-Path $RepoRoot "WhatsNew.md"
foreach ($file in @($cliProject, $license, $readme, $whatsNew)) {
    if (-not (Test-Path $file)) { throw "Required file not found: $file" }
}

$artifactsRoot = Join-Path $RepoRoot "artifacts/release"
New-Item -ItemType Directory -Force -Path $OutputPath | Out-Null
if (Test-Path $artifactsRoot) { Remove-Item -Recurse -Force $artifactsRoot }
New-Item -ItemType Directory -Force -Path $artifactsRoot | Out-Null

$produced = [System.Collections.Generic.List[string]]::new()

foreach ($rid in $Rids) {
    $isWindowsRid = $rid.StartsWith("win-", [StringComparison]::Ordinal)
    $exeName = if ($isWindowsRid) { "CHDSharp.exe" } else { "CHDSharp" }

    $publishDir = Join-Path $artifactsRoot "publish/$rid"
    $stageDir = Join-Path $artifactsRoot "bundle/$rid"
    New-Item -ItemType Directory -Force -Path $publishDir, $stageDir | Out-Null

    Write-Host "==> Publishing CHDSharp $Version for $rid ($TargetFramework)..." -ForegroundColor Cyan
    & dotnet publish $cliProject -c $Configuration -f $TargetFramework -r $rid --self-contained false -p:PublishSingleFile=true -p:AllowedReferenceRelatedFileExtensions=none -o $publishDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $rid (exit code $LASTEXITCODE)" }

    $published = @(Get-ChildItem -Path $publishDir -File)
    $binary = $published | Where-Object Name -eq $exeName
    if (-not $binary) {
        throw "Expected single binary '$exeName' not found in $publishDir (found: $($published.Name -join ', '))"
    }
    $extra = $published | Where-Object { $_.Name -ne $exeName -and $_.Extension -ne ".pdb" }
    if ($extra.Count -gt 0) {
        Write-Warning "$rid publish did not collapse to a single file - bundling extras: $($extra.Name -join ', ')"
    }

    foreach ($item in $published | Where-Object { $_.Name -eq $exeName -or ($_.Name -ne $exeName -and $_.Extension -ne ".pdb") }) {
        Copy-Item $item.FullName $stageDir
    }
    Copy-Item $license $stageDir
    Copy-Item $readme $stageDir
    Copy-Item $whatsNew $stageDir

    $runExample = if ($isWindowsRid) { "CHDSharp.exe info -i game.chd" } else { "CHDSharp info -i game.chd" }
    $readmeTxtLines = @(
        "CHDSharp CLI v$Version",
        "Pure C# CHD (Compressed Hunks of Data) reader/writer -- MAME chdman-compatible.",
        "Framework-dependent single-file build targeting .NET 10.",
        "REQUIRES the .NET 10 runtime to be installed: https://dotnet.microsoft.com/download/dotnet/10.0",
        "===============================================================================",
        "",
        "Run from a command prompt:",
        "    $runExample"
    )
    if (-not $isWindowsRid) {
        $readmeTxtLines += @(
            "",
            "NOTE: if the binary is not executable after unzipping, run: chmod +x $exeName"
        )
    }
    $readmeTxtLines += @(
        "",
        "Common commands:",
        "    $runExample",
        "    CHDSharp$($isWindowsRid ? '.exe' : '') verify -i game.chd",
        "    CHDSharp$($isWindowsRid ? '.exe' : '') createcd -o game.chd -i game.cue",
        "    CHDSharp$($isWindowsRid ? '.exe' : '') createhd -o game.chd -i game.bin",
        "    CHDSharp$($isWindowsRid ? '.exe' : '') extractraw -o game.bin -i game.chd",
        "    CHDSharp$($isWindowsRid ? '.exe' : '') copy -o game.chd -i old.chd -c zstd",
        "",
        "Examples:",
        "    CHDSharp$($isWindowsRid ? '.exe' : '') help            (full command list)",
        "    CHDSharp$($isWindowsRid ? '.exe' : '') help <command>  (per-command help)",
        "",
        "License: see LICENSE.txt (MIT project code; VendoredFlac is LGPL-2.1; full third-party notices inside).",
        "What's new: see WhatsNew.md. Full changelog: see README.md and the GitHub releases page."
    )
    Set-Content -Path (Join-Path $stageDir "README.txt") -Value $readmeTxtLines -Encoding utf8NoBOM

    if ($IsWindows -and $rid -eq "win-x64") {
        $smoke = & $binary.FullName help
        if ($LASTEXITCODE -ne 0) { throw "Smoke test failed: $exeName help exited with $LASTEXITCODE" }
        Write-Host "    smoke test: $($smoke | Select-Object -First 1)"
    }

    $zipName = "CHDSharp_${rid}_v$Version.zip"
    $zipPath = Join-Path $OutputPath $zipName
    if (Test-Path $zipPath) { Remove-Item $zipPath }
    [IO.Compression.ZipFile]::CreateFromDirectory($stageDir, $zipPath, [IO.Compression.CompressionLevel]::Optimal, $false)
    $sizeMb = [math]::Round((Get-Item $zipPath).Length / 1MB, 2)
    Write-Host "    -> $zipPath ($sizeMb MB)" -ForegroundColor Green
    $produced.Add($zipPath)
}

Write-Host ""
Write-Host "Built $($produced.Count) bundle(s) for v$Version into $OutputPath :" -ForegroundColor Cyan
foreach ($path in $produced) { Write-Host "  $([IO.Path]::GetFileName($path))" }
