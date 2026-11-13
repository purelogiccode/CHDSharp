#Requires -Version 7.0
<#
.SYNOPSIS
    Builds the CHDSharp CLI release bundles: one framework-dependent single-file
    binary per platform plus LICENSE.txt, README.md, README.txt and WhatsNew.md.

.DESCRIPTION
    For every requested runtime identifier the CLI is published as a framework-dependent
    single-file executable - the .NET runtime itself is NOT embedded, so the bundle
    requires the .NET 11 runtime to be installed. Bundles use the native archive format
    for each platform - CHDSharp_<rid>_v<version>.zip on Windows,
    CHDSharp_<rid>_v<version>.tar.gz on Linux and CHDSharp_<rid>_v<version>.dmg on macOS -
    the exact asset names VersionCheckService looks up on GitHub releases for the built-in
    update check (e.g. CHDSharp_linux-x64_v1.7.0.tar.gz). Existing files in the output
    path are never deleted. macOS .dmg bundles require macOS; use -Rids on other hosts to
    select only the formats you can build.

.EXAMPLE
    pwsh scripts/package-release.ps1
    pwsh scripts/package-release.ps1 -Rids win-x64,win-arm64 -OutputPath dist
    pwsh scripts/package-release.ps1 -Rids osx-arm64 -OutputPath dist
#>
[CmdletBinding()]
param(
    [string]$Version,
    [string]$OutputPath,
    [string]$Configuration = "Release",
    [string]$TargetFramework = "net11.0",
    [string[]]$Rids = @("win-x64", "win-arm64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64")
)

$ErrorActionPreference = "Stop"
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path

# Accept both -Rids win-x64,linux-x64 (CI matrix expansion) and -Rids win-x64 -Rids linux-x64.
$Rids = @($Rids | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })

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
    $isLinuxRid = $rid.StartsWith("linux-", [StringComparison]::Ordinal)
    $isMacRid = $rid.StartsWith("osx-", [StringComparison]::Ordinal)
    if (-not ($isWindowsRid -or $isLinuxRid -or $isMacRid)) {
        throw "Unsupported runtime identifier '$rid' (expected win-*, linux-* or osx-*)."
    }
    if ($isMacRid -and -not $IsMacOS) {
        throw "macOS .dmg bundles must be built on macOS; requested '$rid' on $([Environment]::OSVersion.Platform)."
    }
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
    if (-not $isWindowsRid -and -not $IsWindows) {
        & chmod +x (Join-Path $stageDir $exeName)
        if ($LASTEXITCODE -ne 0) { throw "chmod +x failed for $exeName (exit code $LASTEXITCODE)" }
    }

    $runExample = if ($isWindowsRid) { "CHDSharp.exe info -i game.chd" } else { "CHDSharp info -i game.chd" }
    $readmeTxtLines = @(
        "CHDSharp CLI v$Version",
        "Pure C# CHD (Compressed Hunks of Data) reader/writer -- MAME chdman-compatible.",
        "Framework-dependent single-file build targeting .NET 11.",
        "REQUIRES the .NET 11 runtime to be installed: https://dotnet.microsoft.com/download/dotnet/11.0",
        "===============================================================================",
        "",
        "Run from a command prompt:",
        "    $runExample"
    )
    if (-not $isWindowsRid) {
        $readmeTxtLines += @(
            "",
            "NOTE: if the binary is not executable after extracting the archive, run: chmod +x $exeName"
        )
    }
    if ($isMacRid) {
        $readmeTxtLines += @(
            "",
            "macOS: open the .dmg, copy CHDSharp anywhere (for example ~/bin), then run it from a terminal."
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

    $extension = if ($isWindowsRid) { "zip" } elseif ($isLinuxRid) { "tar.gz" } else { "dmg" }
    $bundleName = "CHDSharp_${rid}_v$Version.$extension"
    $bundlePath = Join-Path $OutputPath $bundleName
    if (Test-Path $bundlePath) { Remove-Item $bundlePath }

    switch ($extension) {
        "zip" {
            [IO.Compression.ZipFile]::CreateFromDirectory($stageDir, $bundlePath, [IO.Compression.CompressionLevel]::Optimal, $false)
        }
        "tar.gz" {
            & tar -czf $bundlePath -C $stageDir .
            if ($LASTEXITCODE -ne 0) { throw "tar failed for $rid (exit code $LASTEXITCODE)" }
        }
        "dmg" {
            & hdiutil create -volname "CHDSharp v$Version" -srcfolder $stageDir -ov -format UDZO $bundlePath
            if ($LASTEXITCODE -ne 0) { throw "hdiutil failed for $rid (exit code $LASTEXITCODE)" }
        }
    }

    $sizeMb = [math]::Round((Get-Item $bundlePath).Length / 1MB, 2)
    Write-Host "    -> $bundlePath ($sizeMb MB)" -ForegroundColor Green
    $produced.Add($bundlePath)
}

Write-Host ""
Write-Host "Built $($produced.Count) bundle(s) for v$Version into $OutputPath :" -ForegroundColor Cyan
foreach ($path in $produced) { Write-Host "  $([IO.Path]::GetFileName($path))" }
