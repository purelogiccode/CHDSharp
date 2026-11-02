#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Builds the DocFX API reference into `_site/api` and applies the CHDSharp theme.

.DESCRIPTION
    Restores the pinned DocFX tool from `.config/dotnet-tools.json`, generates the managed
    reference metadata for `CHDSharpLib`, renders the site and appends `docfx/theme.css` to the
    template stylesheet so the reference matches the main documentation site.

    The `pages` workflow calls this script; contributors can run the same command locally:

        pwsh scripts/build-api-docs.ps1

    The Jekyll site is built separately (`jekyll build --source docs --destination _site`), so the
    final site is `_site` with the API reference under `_site/api`.

.NOTES
    Requires the .NET SDK from `global.json`. Generated metadata (`docfx/api`) is git-ignored.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw "dotnet tool restore failed with exit code $LASTEXITCODE" }

    dotnet docfx metadata docfx.json
    if ($LASTEXITCODE -ne 0) { throw "docfx metadata failed with exit code $LASTEXITCODE" }

    dotnet docfx build docfx.json
    if ($LASTEXITCODE -ne 0) { throw "docfx build failed with exit code $LASTEXITCODE" }

    $mainCss = Join-Path $repoRoot '_site/api/public/main.css'
    if (-not (Test-Path -LiteralPath $mainCss)) {
        throw "DocFX did not produce $mainCss"
    }

    Add-Content -LiteralPath $mainCss -Value ''
    Add-Content -LiteralPath $mainCss -Value '/* CHDSharp theme - see docfx/theme.css */'
    Get-Content -LiteralPath (Join-Path $repoRoot 'docfx/theme.css') -Raw |
        Add-Content -LiteralPath $mainCss

    Write-Host 'API reference written to _site/api'
}
finally {
    Pop-Location
}
