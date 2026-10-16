#Requires -Version 7.0
<#
.SYNOPSIS
    Converts the docs/ Jekyll pages into GitHub wiki pages.

.DESCRIPTION
    The docs/ folder is deployed twice: as the GitHub Pages site (Jekyll) and as the
    repository wiki. Wiki pages are plain Markdown with extension-less page links, so this
    script copies every docs/*.md page (README.md becomes Home.md), strips the Jekyll
    front matter, rewrites relative links (page.md -> page, ../README.md and ../LICENSE.txt
    -> GitHub blob links) and regenerates the _Sidebar.md navigation used by the wiki.

.EXAMPLE
    pwsh scripts/prepare-wiki.ps1
    pwsh scripts/prepare-wiki.ps1 -OutputPath wiki-content
#>
[CmdletBinding()]
param(
    [string]$DocsPath,
    [string]$OutputPath,
    [string]$RepoUrl = "https://github.com/purelogiccode/CHDSharp"
)

$ErrorActionPreference = "Stop"
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
if (-not $DocsPath) { $DocsPath = Join-Path $RepoRoot "docs" }
if (-not $OutputPath) { $OutputPath = Join-Path $RepoRoot "artifacts/wiki" }

if (-not (Test-Path $DocsPath)) { throw "Docs folder not found: $DocsPath" }
if (Test-Path $OutputPath) { Remove-Item -Recurse -Force $OutputPath }
New-Item -ItemType Directory -Force -Path $OutputPath | Out-Null

$pageNameByFile = @{}
foreach ($file in Get-ChildItem -Path $DocsPath -Filter "*.md" -File) {
    $pageNameByFile[$file.Name] = if ($file.Name -ieq "README.md") { "Home" } else { [IO.Path]::GetFileNameWithoutExtension($file.Name) }
}

# Rewrites a single markdown link target for wiki consumption.
function Convert-LinkTarget {
    param([string]$Target)

    if ($Target -match "^https?://|^mailto:|^#") {
        return $Target
    }

    $readmeMatch = [regex]::Match($Target, "^(?:\.\./)+README\.md(#.*)?$")
    if ($readmeMatch.Success) {
        return "$RepoUrl/blob/main/README.md$($readmeMatch.Groups[1].Value)"
    }

    if ([regex]::IsMatch($Target, "^(?:\.\./)+LICENSE\.txt$")) {
        return "$RepoUrl/blob/main/LICENSE.txt"
    }

    $pageMatch = [regex]::Match($Target, "^([A-Za-z0-9._\-]+)\.md(#.*)?$")
    if ($pageMatch.Success) {
        $fileName = "$($pageMatch.Groups[1].Value).md"
        if ($pageNameByFile.ContainsKey($fileName)) {
            return $pageNameByFile[$fileName] + $pageMatch.Groups[2].Value
        }
    }

    return $Target
}

$linkRegex = [regex]"\]\(([^)]+)\)"
$evaluator = [System.Text.RegularExpressions.MatchEvaluator] {
    param($match)
    return "](" + (Convert-LinkTarget $match.Groups[1].Value) + ")"
}

$written = [System.Collections.Generic.List[string]]::new()
foreach ($file in Get-ChildItem -Path $DocsPath -Filter "*.md" -File | Sort-Object Name) {
    $content = Get-Content -Path $file.FullName -Raw

    # strip Jekyll front matter
    if ($content.StartsWith("---")) {
        $frontMatter = [regex]::Match($content, "(?s)\A---\r?\n.*?\r?\n---\r?\n\r?\n?")
        if ($frontMatter.Success) {
            $content = $content.Substring($frontMatter.Length)
        }
    }

    $content = $linkRegex.Replace($content, $evaluator)
    $targetName = if ($file.Name -ieq "README.md") { "Home.md" } else { $file.Name }
    Set-Content -Path (Join-Path $OutputPath $targetName) -Value $content -Encoding utf8NoBOM -NoNewline
    $written.Add($targetName)
}

$sidebar = @"
**CHDSharp**

- [Home](Home)
- [Getting Started](getting-started)
- [Building](building)

**Format & internals**

- [CHD Format Reference](chd-format)
- [CHD Deep Reference](chd-deep-reference)
- [Codecs](codecs)
- [Architecture](architecture)

**API & usage**

- [API Reference](api-reference)
- [Metadata](metadata)
- [Verification](verification)
- [Extraction](extraction)
- [Parent/Child CHDs](parent-child-chds)

**CLI & writing**

- [CLI Command Reference](cli-commands)
- [Encoder (CHD creation)](encoder)

**Parity & validation**

- [CHDman Parity (battle test)](chdman-parity)
- [Comparison with libchdr](libchdr-comparison)
- [Testing](testing)

**Operations**

- [Performance](performance)
- [Logging](logging)
- [Error Codes](error-codes)

**Reference**

- [Troubleshooting & FAQ](troubleshooting)
- [Release Notes](ReleaseNotes)
"@
Set-Content -Path (Join-Path $OutputPath "_Sidebar.md") -Value $sidebar -Encoding utf8NoBOM -NoNewline
$written.Add("_Sidebar.md")

Write-Host "Prepared $($written.Count) wiki page(s) in $OutputPath :" -ForegroundColor Cyan
foreach ($name in $written) { Write-Host "  $name" }
