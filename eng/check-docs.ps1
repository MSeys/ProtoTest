[CmdletBinding()]
param()

# Documentation enforcement for docs/docs and docs/src. It runs before the Docusaurus build so that
# removed symbols, configuration keys and sample links cannot drift back in. This mirrors the
# enforcement list in assets/internal/docs-rework-plan.md.

$ErrorActionPreference = "Stop"

$repository = Split-Path -Parent $PSScriptRoot
$docsRoot = Join-Path $repository "docs"
$docsContentRoot = Join-Path $docsRoot "docs"
$docsSourceRoot = Join-Path $docsRoot "src"
$factsRoot = Join-Path $repository "assets\internal\docs-facts"

$generatedFolders = '[\\/](build|\.docusaurus|node_modules|obj|bin)[\\/]'

$docsContentFiles = @(Get-ChildItem -LiteralPath $docsContentRoot -Recurse -File |
    Where-Object { $_.Extension -in '.md', '.mdx' -and $_.FullName -notmatch $generatedFolders })
$docsSourceFiles = @(Get-ChildItem -LiteralPath $docsSourceRoot -Recurse -File |
    Where-Object { $_.Extension -in '.ts', '.tsx' -and $_.FullName -notmatch $generatedFolders })

# The fact sheets are internal and gitignored; a fresh CI checkout has none. The key cross-check
# runs where they exist and reports itself as skipped where they do not.
$factFiles = @()
if (Test-Path -LiteralPath $factsRoot) {
    $factFiles = @(Get-ChildItem -LiteralPath $factsRoot -File -Filter *.md)
}

$forbiddenFailures = New-Object System.Collections.Generic.List[string]
$keyFailures = New-Object System.Collections.Generic.List[string]
$linkFailures = New-Object System.Collections.Generic.List[string]

function Get-RelativePath {
    param([string]$FullPath)
    return $FullPath.Substring($repository.Length + 1).Replace('\', '/')
}

# 1. Forbidden symbols ---------------------------------------------------------

$forbiddenSymbols = @(
    'ShouldHaveHttpStatus',
    'ShouldMatchData',
    'Messages()',
    'RestResponseOptions',
    'GraphQLResponseOptions',
    'RestAttachmentOptions',
    'GraphQLAttachmentOptions',
    'ProtoGrpcClientOptions',
    'ProtoMessagingOptions',
    'ProtoRabbitMqOptions',
    'ProtoSqlOptions',
    'ProtoSheetsOptions',
    'IProtoMessageBrokerSetup',
    'run.json',
    'sheets.assert',
    'auth.apply',
    'auth.skip',
    'matthiasseys',
    '[Fact, ProtoTest]'
)

foreach ($file in @($docsContentFiles + $docsSourceFiles)) {
    $relative = Get-RelativePath $file.FullName
    $lines = @(Get-Content -LiteralPath $file.FullName)
    for ($lineNumber = 0; $lineNumber -lt $lines.Count; $lineNumber++) {
        foreach ($symbol in $forbiddenSymbols) {
            if ($lines[$lineNumber].IndexOf($symbol, [StringComparison]::Ordinal) -ge 0) {
                $forbiddenFailures.Add(("{0}:{1}: {2}" -f $relative, ($lineNumber + 1), $symbol))
            }
        }
    }
}

# 2. Configuration keys --------------------------------------------------------

$keyPattern = [regex]'ProtoTest:[A-Za-z:]+[A-Za-z]'

# The demo's environment switches are sample conventions, not package options: they select the
# environment the demo's Setup builds and have no section in a package fact sheet.
$sampleSideKeys = @('ProtoTest:TargetUrl', 'ProtoTest:Database')

function Get-ConfigurationKeys {
    param([string[]]$Files)

    $keys = New-Object System.Collections.Generic.HashSet[string]
    foreach ($file in $Files) {
        $text = Get-Content -Raw -LiteralPath $file
        foreach ($match in $keyPattern.Matches($text)) {
            [void]$keys.Add($match.Value.TrimEnd('.', ',', ';', ':', '`', ')', ']'))
        }
    }
    return $keys
}

function Test-TruncatedKey {
    param([string]$Key, [System.Collections.Generic.HashSet[string]]$Keys)

    # A literal that is the leading path of a longer literal in the same corpus is a truncated form
    # (for example `ProtoTest:Applications` out of `ProtoTest:Applications:{app}:BaseUrl`).
    foreach ($other in $Keys) {
        if ($other -ne $Key -and $other.StartsWith($Key + ':', [StringComparison]::Ordinal)) {
            return $true
        }
    }
    return $false
}

function Test-KeyCovered {
    param([string]$Key, [System.Collections.Generic.HashSet[string]]$Candidates)

    # A key is covered when it appears verbatim, when the candidate names the section it lives in
    # (`ProtoTest:Rest:Responses` covers `ProtoTest:Rest:Responses:MaxBodyBytes`), or when the key
    # names a section the candidate lives in (the page documents the section, not every leaf).
    foreach ($candidate in $Candidates) {
        if ($candidate -eq $Key -or
            $candidate.StartsWith($Key + ':', [StringComparison]::Ordinal) -or
            $Key.StartsWith($candidate + ':', [StringComparison]::Ordinal)) {
            return $true
        }
    }
    return $false
}

$docsKeys = Get-ConfigurationKeys -Files @($docsContentFiles + $docsSourceFiles | ForEach-Object { $_.FullName })
$factKeys = Get-ConfigurationKeys -Files @($factFiles | ForEach-Object { $_.FullName })
$keyCrossCheckSkipped = $factFiles.Count -eq 0

if (-not $keyCrossCheckSkipped) {
    foreach ($key in $docsKeys) {
        if ($sampleSideKeys -contains $key) { continue }
        if (Test-TruncatedKey -Key $key -Keys $docsKeys) { continue }
        if (-not (Test-KeyCovered -Key $key -Candidates $factKeys)) {
            $keyFailures.Add("docs mention '$key' but no fact sheet does")
        }
    }

    foreach ($key in $factKeys) {
        if (Test-TruncatedKey -Key $key -Keys $factKeys) { continue }
        if (-not (Test-KeyCovered -Key $key -Candidates $docsKeys)) {
            $keyFailures.Add("fact sheet key '$key' is mentioned in no docs page")
        }
    }
}

# 3. Sample links ---------------------------------------------------------------

$linkPattern = [regex]'\]\(([^()\s]+)\)'
foreach ($file in $docsContentFiles) {
    $relative = Get-RelativePath $file.FullName
    $lines = @(Get-Content -LiteralPath $file.FullName)
    for ($lineNumber = 0; $lineNumber -lt $lines.Count; $lineNumber++) {
        foreach ($match in $linkPattern.Matches($lines[$lineNumber])) {
            $target = $match.Groups[1].Value
            if ($target -match '^([a-zA-Z][a-zA-Z0-9+.-]*:|//|#)' -or $target -eq '') { continue }

            $path = ($target -split '#')[0]
            $path = ($path -split '\?')[0]
            if ($path -eq '') { continue }

            if ($path -match '^(samples|tests)/') {
                # A target already rooted at the repository.
                $resolved = [System.IO.Path]::GetFullPath((Join-Path $repository $path))
                $repoRelative = $path
            }
            else {
                $resolved = [System.IO.Path]::GetFullPath((Join-Path $file.DirectoryName $path))
                if (-not $resolved.StartsWith($repository + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
                    continue
                }
                $repoRelative = Get-RelativePath $resolved
            }

            if ($repoRelative -notmatch '^(samples|tests)/') { continue }
            if (-not (Test-Path -LiteralPath $resolved)) {
                $linkFailures.Add(("{0}:{1}: '{2}' does not resolve to '{3}'" -f $relative, ($lineNumber + 1), $target, $repoRelative))
            }
        }
    }
}

# Summary -----------------------------------------------------------------------

$checkedFiles = $docsContentFiles.Count + $docsSourceFiles.Count + $factFiles.Count
$totalFailures = $forbiddenFailures.Count + $keyFailures.Count + $linkFailures.Count

if ($totalFailures -gt 0) {
    Write-Host "Documentation checks failed:"
    if ($forbiddenFailures.Count -gt 0) {
        Write-Host ("  Forbidden symbols ({0}):" -f $forbiddenFailures.Count)
        foreach ($failure in $forbiddenFailures) { Write-Host "    $failure" }
    }
    if ($keyFailures.Count -gt 0) {
        Write-Host ("  Configuration keys ({0}):" -f $keyFailures.Count)
        foreach ($failure in $keyFailures) { Write-Host "    $failure" }
    }
    if ($linkFailures.Count -gt 0) {
        Write-Host ("  Sample links ({0}):" -f $linkFailures.Count)
        foreach ($failure in $linkFailures) { Write-Host "    $failure" }
    }
}

$summary = "check-docs: checked {0} files ({1} docs pages, {2} source files, {3} fact sheets); {4} failure(s)." -f
    $checkedFiles, $docsContentFiles.Count, $docsSourceFiles.Count, $factFiles.Count, $totalFailures
if ($keyCrossCheckSkipped) {
    $summary += " Configuration-key cross-check skipped: assets/internal/docs-facts is not present (internal, gitignored)."
}
Write-Host $summary

if ($totalFailures -gt 0) { exit 1 }
exit 0
