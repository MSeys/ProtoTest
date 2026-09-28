[CmdletBinding()]
param()

# Documentation enforcement for docs/docs and docs/src. It runs before the Docusaurus build so that
# removed symbols, dead API names, configuration keys and sample links cannot drift back in. This
# mirrors the enforcement list in assets/internal/docs-rework-plan.md.

$ErrorActionPreference = "Stop"

$repository = Split-Path -Parent $PSScriptRoot
$docsRoot = Join-Path $repository "docs"
$docsContentRoot = Join-Path $docsRoot "docs"
$docsSourceRoot = Join-Path $docsRoot "src"
$factsRoot = Join-Path $repository "assets\internal\records\docs-facts"

$generatedFolders = '[\\/](build|\.docusaurus|node_modules|obj|bin)[\\/]'

$docsContentFiles = @(Get-ChildItem -LiteralPath $docsContentRoot -Recurse -File |
    Where-Object { $_.Extension -in '.md', '.mdx' -and $_.FullName -notmatch $generatedFolders })
$docsSourceFiles = @(Get-ChildItem -LiteralPath $docsSourceRoot -Recurse -File |
    Where-Object { $_.Extension -in '.ts', '.tsx', '.md', '.mdx' -and $_.FullName -notmatch $generatedFolders })

# The fact sheets are internal and live in the private records checkout; a fresh CI checkout has none.
# The public key list (docs/configuration-keys.json) carries the half that runs everywhere; the fact
# sheets add the fact-to-docs direction where the checkout exists.
$factFiles = @()
if (Test-Path -LiteralPath $factsRoot) {
    $factFiles = @(Get-ChildItem -LiteralPath $factsRoot -File -Filter *.md)
}

$forbiddenFailures = New-Object System.Collections.Generic.List[string]
$apiFailures = New-Object System.Collections.Generic.List[string]
$keyFailures = New-Object System.Collections.Generic.List[string]
$linkFailures = New-Object System.Collections.Generic.List[string]
$releaseFailures = New-Object System.Collections.Generic.List[string]

function Get-RelativePath {
    param([string]$FullPath)
    return $FullPath.Substring($repository.Length + 1).Replace('\', '/')
}

# 1. Forbidden symbols ---------------------------------------------------------

# The non-API half of the deny list: artifact file names and old vocabulary strings that no API
# list can name.
$forbiddenVocabulary = @(
    'run.json',
    'sheets.assert',
    'auth.apply',
    'auth.skip',
    'matthiasseys',
    '[Fact, ProtoTest]',
    '--prerelease',
    '0.1.0-alpha'
)

# The removed-API half is the package-validation evidence itself. Every
# src/**/CompatibilitySuppressions.xml entry records a public symbol the published baseline had and
# this branch deliberately removed, so the gate reads those files instead of a hand list.
# CP0001 suppresses a removed public type: its short name is the deny entry. CP0002 suppresses a
# removed member: the entry is `DeclaringType.Member`, and only while no member of that name is
# left on the declaring type in source - CP0002 also records changed overloads, whose name is still
# live and whose page must stay correct. CP0006 is deliberately not read: it means "a member was
# added to an interface", so the member exists and denying it would fail a page that teaches the
# current interface. Only the Target name is read; the lib/... Left/Right paths name the same
# symbol per target framework and add nothing.
$sourceFiles = @(Get-ChildItem -Path (Join-Path $repository "src") -Recurse -File -Filter *.cs |
    Where-Object { $_.FullName -notmatch $generatedFolders } |
    ForEach-Object { [pscustomobject]@{ Text = Get-Content -Raw -LiteralPath $_.FullName } })

$removedApiSymbols = New-Object System.Collections.Generic.HashSet[string]
foreach ($suppressionFile in Get-ChildItem -Path (Join-Path $repository "src") -Recurse -Filter CompatibilitySuppressions.xml) {
    [xml]$suppressions = Get-Content -Raw -LiteralPath $suppressionFile.FullName
    foreach ($suppression in $suppressions.Suppressions.Suppression) {
        if ($suppression.DiagnosticId -notin @('CP0001', 'CP0002')) { continue }

        $signature = ((($suppression.Target -replace '^[A-Z]:', '') -split '\(')[0]) -replace '`', ''
        $segments = $signature -split '\.'
        $member = $segments[-1]
        if ($member -match '^(get|set)_(.+)$') { $member = $Matches[2] }
        if ($member -in @('#ctor', '#cctor')) { continue }

        if ($suppression.DiagnosticId -eq 'CP0001') {
            [void]$removedApiSymbols.Add($member)
            continue
        }

        $declaring = if ($segments.Count -ge 2) { $segments[-2] } else { '' }
        $typeFiles = @($sourceFiles | Where-Object { $_.Text -match "\b(class|struct|interface|enum|record)\s+$([regex]::Escape($declaring))\b" })
        $stillDeclared = $false
        foreach ($typeFile in $typeFiles) {
            if ($typeFile.Text -match "\b$([regex]::Escape($member))\b") { $stillDeclared = $true; break }
        }
        if (-not $stillDeclared) { [void]$removedApiSymbols.Add("$declaring.$member") }
    }
}

$forbiddenSymbols = @($forbiddenVocabulary) + @($removedApiSymbols | Sort-Object)

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

# 2. Documented API names ------------------------------------------------------
# A documented Add* symbol must exist as a method in src/** or samples/** (the sample helpers are part
# of what the docs teach). The check catches the class the audit found: docs taught AddWebSocketDevices
# after the builder had been renamed. Only camelCase API names match (Add followed by an uppercase
# letter), so prose like "Adding", "Address" or "Added" is not a candidate. Comments and string
# literals are stripped first: a name that only appears in prose, an example string or a usage message
# is not an API (A5R-03). The allowlist covers the framework helpers the docs reference, which are not
# this repository's API; add a name here with its owner when a page legitimately teaches it.

$apiAllowlist = @(
    'AddBus',                  # a reader-written extension example (advanced/extending.md)
    'AddConnectionString',     # Aspire.Hosting's AppHost connection-string resource (integrations/aspire.md)
    'AddConsumer',             # MassTransit's registration configurator (integrations/messaging/masstransit.md)
    'AddEnvironmentVariables', # Microsoft.Extensions.Configuration
    'AddJsonFile',             # Microsoft.Extensions.Configuration
    'AddMassTransitTestHarness', # MassTransit.Testing's app-side composition (integrations/messaging/masstransit.md)
    'AddMinutes',              # System.DateTimeOffset
    'AddOtlpExporter'          # OpenTelemetry exporter builder
    'AddSource'                # OpenTelemetry source subscription
)

$apiNamePattern = [regex]'\b(Add[A-Z][A-Za-z0-9_]*)\b'
$sourceApiNames = New-Object System.Collections.Generic.HashSet[string]([StringComparer]::Ordinal)
$sourceRoots = @((Join-Path $repository "src"), (Join-Path $repository "samples"))
foreach ($sourceRoot in $sourceRoots) {
    if (-not (Test-Path -LiteralPath $sourceRoot)) { continue }
    foreach ($file in Get-ChildItem -LiteralPath $sourceRoot -Recurse -File -Filter *.cs |
        Where-Object { $_.FullName -notmatch $generatedFolders }) {
        $text = Get-Content -Raw -LiteralPath $file.FullName
        $codeText = ($text -split "`r?`n" | ForEach-Object {
                ($_ -replace '"[^"]*"', '') -replace '/\*.*?\*/', '' -replace '//.*$', ''
            }) -join "`n"
        foreach ($match in [regex]::Matches($codeText, '\b(Add[A-Z][A-Za-z0-9_]*)\s*[<(]')) {
            [void]$sourceApiNames.Add($match.Groups[1].Value)
        }
    }
}

foreach ($file in @($docsContentFiles + $docsSourceFiles)) {
    $relative = Get-RelativePath $file.FullName
    $lines = @(Get-Content -LiteralPath $file.FullName)
    for ($lineNumber = 0; $lineNumber -lt $lines.Count; $lineNumber++) {
        foreach ($match in $apiNamePattern.Matches($lines[$lineNumber])) {
            $symbol = $match.Groups[1].Value
            if ($apiAllowlist -contains $symbol) { continue }
            if (-not $sourceApiNames.Contains($symbol)) {
                $apiFailures.Add(("{0}:{1}: '{2}' is documented but no Add* method with that name exists in src/ or samples/" -f $relative, ($lineNumber + 1), $symbol))
            }
        }
    }
}

# 3. Configuration keys --------------------------------------------------------

$keyPattern = [regex]'ProtoTest:[A-Za-z:]+[A-Za-z]'

# The public key list is tracked at docs/configuration-keys.json so the cross-check runs in CI too,
# where the private docs-facts checkout does not exist. `sections` is generated from the source
# section constants and verified against them below; `allowedKeys` carries the documented keys that
# are not section constants (sample-side switches and shared defaults), each with its reason. The
# private fact sheets stay the richer source and add the fact-to-docs direction where present.
$keyListPath = Join-Path $docsRoot "configuration-keys.json"
$publicSections = @()
$publicAllowedKeys = @()
if (-not (Test-Path -LiteralPath $keyListPath)) {
    $keyFailures.Add("docs/configuration-keys.json is missing; it is the public configuration-key list the docs check cross-checks against.")
}
else {
    try {
        $keyList = Get-Content -Raw -LiteralPath $keyListPath | ConvertFrom-Json
        $publicSections = @($keyList.sections | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        $publicAllowedKeys = @($keyList.allowedKeys | ForEach-Object { $_.key } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    }
    catch {
        $keyFailures.Add("docs/configuration-keys.json cannot be read: $($_.Exception.Message)")
    }
}

# The section constants in src/**/*.cs are the authority: a rename that leaves the tracked list
# behind fails here instead of silently detaching the list from the code.
$sourceSections = New-Object System.Collections.Generic.HashSet[string]([StringComparer]::Ordinal)
foreach ($file in Get-ChildItem -Path (Join-Path $repository "src") -Recurse -File -Filter *.cs |
    Where-Object { $_.FullName -notmatch $generatedFolders }) {
    $text = Get-Content -Raw -LiteralPath $file.FullName
    foreach ($match in [regex]::Matches($text, '\b\w*(?:SectionName|SectionPath)\s*=\s*"(ProtoTest:[^"]+)"')) {
        [void]$sourceSections.Add($match.Groups[1].Value)
    }
}

if ($publicSections.Count -gt 0 -or $sourceSections.Count -gt 0) {
    $missingFromList = @($sourceSections | Where-Object { $publicSections -notcontains $_ } | Sort-Object)
    $extraInList = @($publicSections | Where-Object { -not $sourceSections.Contains($_) } | Sort-Object)
    if ($missingFromList.Count -gt 0 -or $extraInList.Count -gt 0) {
        $keyFailures.Add("docs/configuration-keys.json 'sections' is stale against the src/**/*.cs section constants (missing from the list: $($missingFromList -join ', '); not in the source: $($extraInList -join ', ')).")
    }
}

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
$publicCandidates = @($publicSections) + @($publicAllowedKeys)

# Every key a docs page names must exist: a source section constant covers it, the tracked allowlist
# covers it, or a fact sheet covers it. The allowlist and the sections must stay taught, so the
# tracked list cannot rot into fiction. The fact sheets add the fact-to-docs direction when present.
foreach ($key in $docsKeys) {
    if (Test-TruncatedKey -Key $key -Keys $docsKeys) { continue }
    if ((Test-KeyCovered -Key $key -Candidates $publicCandidates) -or
        (Test-KeyCovered -Key $key -Candidates $factKeys)) { continue }
    $keyFailures.Add("docs mention '$key' but no source section constant or fact sheet covers it")
}

foreach ($key in $publicCandidates) {
    if (Test-KeyCovered -Key $key -Candidates $docsKeys) { continue }
    $keyFailures.Add("docs/configuration-keys.json lists '$key' but no docs page mentions it")
}

if (-not $keyCrossCheckSkipped) {
    foreach ($key in $factKeys) {
        if (Test-TruncatedKey -Key $key -Keys $factKeys) { continue }
        if (-not (Test-KeyCovered -Key $key -Candidates $docsKeys)) {
            $keyFailures.Add("fact sheet key '$key' is mentioned in no docs page")
        }
    }
}

# 4. Repository paths -----------------------------------------------------------

# Two views of one rule: a markdown link into the repository must resolve, and so must a repository
# path a page or component names in prose. The prose half catches a retired path the link check
# cannot see, because no link markup is there to follow.

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

            if ($path -match '^(samples|src|tests)/') {
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

            if ($repoRelative -notmatch '^(samples|src|tests)/') { continue }
            if (-not (Test-Path -LiteralPath $resolved)) {
                $linkFailures.Add(("{0}:{1}: '{2}' does not resolve to '{3}'" -f $relative, ($lineNumber + 1), $target, $repoRelative))
            }
        }
    }
}

# A rooted `samples/` or `src/` reference is a path into this repository unless it belongs to another
# layout (Next.js's `src/pages`): this repository names its projects in PascalCase, so a lowercase
# first segment is prose about another tree and is left alone. Relative links were resolved above.
$repoPathPattern = [regex]'(?<![\w./\\-])(samples|src)/([A-Za-z0-9_.\-]+(?:/[A-Za-z0-9_.\-]+)*)'
foreach ($file in @($docsContentFiles + $docsSourceFiles)) {
    $relative = Get-RelativePath $file.FullName
    $lines = @(Get-Content -LiteralPath $file.FullName)
    for ($lineNumber = 0; $lineNumber -lt $lines.Count; $lineNumber++) {
        foreach ($match in $repoPathPattern.Matches($lines[$lineNumber])) {
            $firstSegment = $match.Groups[2].Value.Split('/')[0]
            if ($firstSegment -cnotmatch '[A-Z]') { continue }
            $path = $match.Value.TrimEnd('.', ',', ';', ':')
            if (-not (Test-Path -LiteralPath (Join-Path $repository $path))) {
                $linkFailures.Add(("{0}:{1}: '{2}' names no file in the repository" -f $relative, ($lineNumber + 1), $path))
            }
        }
    }
}

# 5. Generated changelog --------------------------------------------------------

# One source: the repository CHANGELOG.md. docs/scripts/generate-changelog.mjs writes the documentation
# page and the homepage release feed from it; check mode fails when either output is stale, so a release
# that edits the changelog without regenerating fails here.

$changelogGenerator = Join-Path $docsRoot "scripts\generate-changelog.mjs"
if (Test-Path -LiteralPath $changelogGenerator) {
    $generatorOutput = & node $changelogGenerator --check 2>&1
    if ($LASTEXITCODE -ne 0) {
        $releaseFailures.Add("the generated changelog is stale; run node docs/scripts/generate-changelog.mjs")
        foreach ($line in $generatorOutput) { $releaseFailures.Add("  $line") }
    }
}
else {
    $releaseFailures.Add("docs/scripts/generate-changelog.mjs is missing")
}

# Summary -----------------------------------------------------------------------

$checkedFiles = $docsContentFiles.Count + $docsSourceFiles.Count + $factFiles.Count
$totalFailures = $forbiddenFailures.Count + $apiFailures.Count + $keyFailures.Count + $linkFailures.Count + $releaseFailures.Count

if ($totalFailures -gt 0) {
    Write-Host "Documentation checks failed:"
    if ($forbiddenFailures.Count -gt 0) {
        Write-Host ("  Forbidden symbols ({0}):" -f $forbiddenFailures.Count)
        foreach ($failure in $forbiddenFailures) { Write-Host "    $failure" }
    }
    if ($apiFailures.Count -gt 0) {
        Write-Host ("  Documented API names ({0}):" -f $apiFailures.Count)
        foreach ($failure in $apiFailures) { Write-Host "    $failure" }
    }
    if ($keyFailures.Count -gt 0) {
        Write-Host ("  Configuration keys ({0}):" -f $keyFailures.Count)
        foreach ($failure in $keyFailures) { Write-Host "    $failure" }
    }
    if ($linkFailures.Count -gt 0) {
        Write-Host ("  Repository paths ({0}):" -f $linkFailures.Count)
        foreach ($failure in $linkFailures) { Write-Host "    $failure" }
    }
    if ($releaseFailures.Count -gt 0) {
        Write-Host ("  Generated changelog ({0}):" -f $releaseFailures.Count)
        foreach ($failure in $releaseFailures) { Write-Host "    $failure" }
    }
}

$summary = "check-docs: checked {0} files ({1} docs pages, {2} source files, {3} fact sheets); {4} failure(s)." -f
    $checkedFiles, $docsContentFiles.Count, $docsSourceFiles.Count, $factFiles.Count, $totalFailures
if ($keyCrossCheckSkipped) {
    $summary += " The private fact sheets are absent; the key cross-check ran against docs/configuration-keys.json (source section constants and documented exceptions), so a docs key no source section backs still fails. The fact-sheet-to-docs half needs the private records checkout."
}
Write-Host $summary

if ($totalFailures -gt 0) { exit 1 }
exit 0
