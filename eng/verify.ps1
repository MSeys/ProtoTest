[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Stage,

    [switch]$Pack,
    [switch]$SkipLint,
    [switch]$SkipDocs,
    [switch]$SkipTests,
    [switch]$NoRestore,
    [switch]$Full,

    # A code stage that skipped its code gates may only record green when the caller says so
    # explicitly; the record then names the approval and the skipped gates (audit A5-05).
    [switch]$AllowSkippedCodeGates
)

# One gate command per stage: it runs the checks CI runs, in the cheapest-first order, and writes a
# machine-readable record to artifacts/gates/<stage>.json so a plan row can quote evidence instead of
# prose. The scope follows the stage: uncommitted changes while the tree is dirty, the HEAD commit
# when it is not. A docs-only stage pays only the docs check, a tooling stage runs the gate fixtures,
# and a code stage runs dotnet format over the projects it touched plus the full test suite. A code
# change whose code gates were skipped (it is already committed, or -SkipLint/-SkipTests was passed)
# records `incomplete` and is not green unless -AllowSkippedCodeGates names the exception. -Full
# forces the CI shape. A failed gate does not stop the remaining gates: the record says what was
# green and what was not.

$ErrorActionPreference = "Stop"
$repository = Split-Path -Parent $PSScriptRoot
$gatesRoot = Join-Path $repository "artifacts/gates"
New-Item -ItemType Directory -Path $gatesRoot -Force | Out-Null

$stageName = ($Stage -replace '[^A-Za-z0-9._-]', '-')
$sha = (& git -C $repository rev-parse --short HEAD).Trim()
$branch = (& git -C $repository branch --show-current).Trim()
$propsText = Get-Content -LiteralPath (Join-Path $repository "Directory.Build.props") -Raw
$version = ([regex]'<Version>([^<]+)</Version>').Match($propsText).Groups[1].Value.Trim()

function ConvertFrom-PorcelainPath {
    param([string]$Line)

    $path = $Line.Substring(3).Trim()
    if ($path -match ' -> ') { $path = ($path -split ' -> ')[-1].Trim() }
    return $path.Trim('"')
}

function Get-ChangedPath {
    $lines = @(& git -C $repository status --porcelain=v1 --untracked-files=all)
    $paths = New-Object System.Collections.Generic.List[string]
    foreach ($line in $lines) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        $paths.Add((ConvertFrom-PorcelainPath $line))
    }

    return $paths
}

function Get-CommittedPath {
    # The stage is the HEAD commit when the tree is clean (the normal end state after a stage commit).
    # --root so a repository's first commit also reports its files; a merge commit reports nothing,
    # which leaves the stage scoped to no changes - -Full is the way to cover it.
    $lines = @(& git -C $repository diff-tree --root --no-commit-id --name-only -r HEAD)
    $paths = New-Object System.Collections.Generic.List[string]
    foreach ($line in $lines) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        $paths.Add($line.Trim())
    }

    return $paths
}

$changed = Get-ChangedPath
$dirty = $changed.Count -gt 0
$scope = if ($dirty) { "working-tree" } else { "head-commit" }
$stageChanged = if ($dirty) { $changed } else { Get-CommittedPath }

$codePattern = '\.(cs|csproj|props|targets|slnx)$|^global\.json$'
$scriptPattern = '^eng/.*\.(ps1|psm1)$|^\.github/workflows/.*\.ya?ml$'
$codeChanges = @($stageChanged | Where-Object { $_ -match $codePattern })
$scriptChanges = @($stageChanged | Where-Object { $_ -match $scriptPattern })

# The code gates apply to a code change no matter where it lives; they can run it only while the
# change is uncommitted (the working-tree scope they are computed over is gone once committed) or
# under -Full. A committed code change with skipped gates is an incomplete record, not a green one.
$expectsCodeGates = $Full -or $codeChanges.Count -gt 0
$canRunCodeGates = $Full -or ($dirty -and $codeChanges.Count -gt 0)
$codeSkipReason = if ($codeChanges.Count -gt 0) { "committed code changes; re-run with -Full to verify them" } else { "no code changes" }

# Scope format to the projects the change touched; a wide or unrecognisable change falls back to the
# full solution (empty include list).
$lintInclude = @()
if (-not $SkipLint -and $canRunCodeGates -and -not $Full) {
    $directories = New-Object System.Collections.Generic.HashSet[string]
    foreach ($path in $changed) {
        if ($path -notmatch '\.(cs|csproj)$') { continue }
        $directory = Split-Path -Parent (Join-Path $repository $path)
        while ($directory -and $directory.Length -gt $repository.Length) {
            if (Test-Path -Path (Join-Path $directory '*.csproj')) { break }
            $directory = Split-Path -Parent $directory
        }

        if ($directory -and $directory.Length -gt $repository.Length -and (Test-Path -Path (Join-Path $directory '*.csproj'))) {
            [void]$directories.Add($directory)
        }
    }

    if ($directories.Count -gt 0 -and $directories.Count -le 12) {
        $lintInclude = @($directories)
    }
}

$gates = New-Object System.Collections.Generic.List[object]
$skipped = New-Object System.Collections.Generic.List[object]
$skippedCodeGates = New-Object System.Collections.Generic.List[object]

function Invoke-Gate {
    param(
        [string]$Name,
        [string]$Script,
        [string[]]$ScriptArguments = @()
    )

    $started = Get-Date
    Write-Host ""
    Write-Host "=== verify: $Name ($Script) ==="
    & pwsh -NoProfile -File (Join-Path $repository $Script) @ScriptArguments
    $exitCode = $LASTEXITCODE
    $seconds = [math]::Round(((Get-Date) - $started).TotalSeconds, 1)
    $gates.Add([pscustomobject]@{
            name     = $Name
            script   = $Script
            exitCode = $exitCode
            seconds  = $seconds
        })
    $result = if ($exitCode -eq 0) { "passed" } else { "FAILED ($exitCode)" }
    Write-Host "=== verify: $Name $result in ${seconds}s ==="
}

$lintArguments = @()
$testArguments = @()
if ($NoRestore) {
    $lintArguments += "-NoRestore"
    $testArguments += "-NoRestore"
}

if ($lintInclude.Count -gt 0) {
    $lintArguments += @("-Include", ($lintInclude -join ";"))
}

if ($SkipLint -or -not $canRunCodeGates) {
    $reason = if ($SkipLint) { "requested" } else { $codeSkipReason }
    $skipped.Add([pscustomobject]@{ name = "lint"; reason = $reason })
    if ($expectsCodeGates) { $skippedCodeGates.Add([pscustomobject]@{ name = "lint"; reason = $reason }) }
}
else {
    Invoke-Gate -Name "lint" -Script "eng/lint.ps1" -ScriptArguments $lintArguments
}

if ($SkipDocs) {
    $skipped.Add([pscustomobject]@{ name = "docs"; reason = "requested" })
}
else {
    Invoke-Gate -Name "docs" -Script "eng/check-docs.ps1"
}

if ($SkipTests -or -not $canRunCodeGates) {
    $reason = if ($SkipTests) { "requested" } else { $codeSkipReason }
    $skipped.Add([pscustomobject]@{ name = "test"; reason = $reason })
    if ($expectsCodeGates) { $skippedCodeGates.Add([pscustomobject]@{ name = "test"; reason = $reason }) }
}
else {
    Invoke-Gate -Name "test" -Script "eng/test.ps1" -ScriptArguments $testArguments
}

# The gate scripts are themselves under test; a stage that touches them (or -Full) proves them.
if ($Full -or $scriptChanges.Count -gt 0) {
    Invoke-Gate -Name "scripts" -Script "eng/test-gates.ps1"
}

if ($Pack) {
    Invoke-Gate -Name "pack" -Script "eng/pack.ps1"
}

$incomplete = $skippedCodeGates.Count -gt 0
$classification = if ($incomplete) { "incomplete" } elseif ($expectsCodeGates) { "code" } elseif ($scriptChanges.Count -gt 0) { "tooling" } else { "docs-only" }
$green = -not ($gates | Where-Object { $_.exitCode -ne 0 }) -and ((-not $incomplete) -or $AllowSkippedCodeGates)
$record = [pscustomobject]@{
    stage                 = $stageName
    timestampUtc          = (Get-Date).ToUniversalTime().ToString("o")
    branch                = $branch
    commit                = $sha
    dirty                 = $dirty
    version               = $version
    scope                 = $scope
    full                  = [bool]$Full
    classification        = $classification
    incomplete            = $incomplete
    allowSkippedCodeGates = [bool]$AllowSkippedCodeGates
    changed               = $codeChanges.Count
    codeChanges           = $codeChanges.Count
    scriptChanges         = $scriptChanges.Count
    gates                 = $gates
    skipped               = $skipped
    skippedCodeGates      = $skippedCodeGates
    green                 = $green
}

$recordPath = Join-Path $gatesRoot "$stageName.json"
$record | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $recordPath -Encoding utf8

$summaryParts = @($gates | ForEach-Object {
        "{0}={1}({2}s)" -f $_.name, $(if ($_.exitCode -eq 0) { "PASS" } else { "FAIL" }), $_.seconds
    })
$summaryParts += @($skipped | ForEach-Object { "{0}=SKIP({1})" -f $_.name, $_.reason })
$summary = $summaryParts -join " "
$flags = @("scope=$scope", "class=$classification")
if ($incomplete) {
    $flags += "skipped-code-gates=" + (($skippedCodeGates | ForEach-Object { $_.name }) -join ",")
    if ($AllowSkippedCodeGates) { $flags += "allow-skipped-code-gates" }
}

$dirtyMark = if ($dirty) { "*" } else { "" }
$evidence = "verify $stageName $sha$dirtyMark ($version) [$($flags -join ' ')]: $summary -> artifacts/gates/$stageName.json"
Write-Host ""
Write-Host $evidence

if (-not $green) { exit 1 }
exit 0
