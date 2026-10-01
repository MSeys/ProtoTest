<#
.SYNOPSIS
Rolls CHANGELOG.md: [Unreleased] becomes the release section and a fresh [Unreleased] goes on top.
#>
[CmdletBinding()]
param(
    # The changelog to cut; defaults to the repository's. -Path is what the tests use on a copy.
    [string]$Path,
    # The version for the new section; defaults to <Version> in Directory.Build.props.
    [string]$Version,
    [ValidatePattern('^\d{4}-\d{2}-\d{2}$')]
    [string]$Date = (Get-Date -Format "yyyy-MM-dd")
)

# Rolls the changelog for a release: `## [Unreleased]` becomes `## [<version>] - <date>` and a fresh
# empty `## [Unreleased]` is inserted above it. The release must have real entries: an empty
# [Unreleased] is a failed release preparation, not a heading rename. Run this before tagging;
# .github/workflows/release.yml reads the section for the GitHub Release notes and fails without it.

$ErrorActionPreference = "Stop"
Import-Module (Join-Path $PSScriptRoot "../lib/Proto.Eng.psm1") -Force
$repository = Get-ProtoRepository
if (-not $Path) { $Path = Join-Path $repository "CHANGELOG.md" }
if (-not (Test-Path -LiteralPath $Path)) { throw "The changelog '$Path' does not exist." }

if (-not $Version) { $Version = Get-ProtoVersion }
if ($Version -notmatch '^\d+\.\d+\.\d+([-.][0-9A-Za-z.-]+)?$') {
    throw "'$Version' is not a version (expected the <Version> form, for example 1.1.0)."
}

$lines = @(Get-Content -LiteralPath $Path)
$unreleasedIndex = -1
for ($index = 0; $index -lt $lines.Count; $index++) {
    if ($lines[$index] -match '^##\s+\[Unreleased\]\s*$') { $unreleasedIndex = $index; break }
}
if ($unreleasedIndex -lt 0) { throw "'$(Split-Path -Leaf $Path)' has no '## [Unreleased]' heading." }

$sectionEnd = $lines.Count
for ($index = $unreleasedIndex + 1; $index -lt $lines.Count; $index++) {
    if ($lines[$index] -match '^##\s') { $sectionEnd = $index; break }
}
$entries = @($lines[($unreleasedIndex + 1)..($sectionEnd - 1)] | Where-Object { $_.Trim() -ne '' })
if ($entries.Count -eq 0) {
    throw "'$(Split-Path -Leaf $Path)' has no entries under '## [Unreleased]'; there is nothing to cut for $Version. Add the release's entries first."
}

$existing = '^##\s+\[' + [regex]::Escape($Version) + '\]'
foreach ($line in $lines) {
    if ($line -match $existing) { throw "'$(Split-Path -Leaf $Path)' already has a section for $Version." }
}

$newLines = @("## [Unreleased]", "", "## [$Version] - $Date") + @($lines[($unreleasedIndex + 1)..($lines.Count - 1)])
if ($unreleasedIndex -gt 0) { $newLines = @($lines[0..($unreleasedIndex - 1)]) + $newLines }

$newline = if ((Get-Content -Raw -LiteralPath $Path) -match "`r`n") { "`r`n" } else { "`n" }
[IO.File]::WriteAllText($Path, (($newLines -join $newline) + $newline), [Text.UTF8Encoding]::new($false))

Write-Host "Cut $Version ($Date) in '$(Split-Path -Leaf $Path)': $($entries.Count) entry line(s) moved under '## [$Version] - $Date'; fresh '## [Unreleased]' inserted."
