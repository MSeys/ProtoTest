[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Stage,

    [switch]$Pack,
    [switch]$SkipLint,
    [switch]$SkipDocs,
    [switch]$SkipTests,
    [switch]$NoRestore,
    [switch]$Full
)

# One gate command per stage: it runs the checks CI runs, in the cheapest-first order, and writes a
# machine-readable record to artifacts/gates/<stage>.json so a plan row can quote evidence instead of
# prose. The scope follows the change: a docs-only stage pays only the docs check, and a code stage
# runs dotnet format over the projects it touched instead of the whole solution. -Full forces the CI
# shape. A failed gate does not stop the remaining gates: the record says what was green and what was not.

$ErrorActionPreference = "Stop"
$repository = Split-Path -Parent $PSScriptRoot
$gatesRoot = Join-Path $repository "artifacts/gates"
New-Item -ItemType Directory -Path $gatesRoot -Force | Out-Null

$stageName = ($Stage -replace '[^A-Za-z0-9._-]', '-')
$sha = (& git -C $repository rev-parse --short HEAD).Trim()
$branch = (& git -C $repository branch --show-current).Trim()
$dirty = @(& git -C $repository status --porcelain).Count -gt 0
$propsText = Get-Content -LiteralPath (Join-Path $repository "Directory.Build.props") -Raw
$version = ([regex]'<Version>([^<]+)</Version>').Match($propsText).Groups[1].Value.Trim()

function Get-ChangedPath {
    $lines = @(& git -C $repository status --porcelain=v1 --untracked-files=all)
    $paths = New-Object System.Collections.Generic.List[string]
    foreach ($line in $lines) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        $path = $line.Substring(3).Trim()
        if ($path -match ' -> ') { $path = ($path -split ' -> ')[-1].Trim() }
        $paths.Add($path.Trim('"'))
    }

    return $paths
}

$changed = Get-ChangedPath
$codeChanges = @($changed | Where-Object { $_ -match '\.(cs|csproj|props|targets|slnx)$' -or $_ -eq 'global.json' })
$hasCodeChanges = $Full -or $codeChanges.Count -gt 0
$autoSkipLint = -not $hasCodeChanges
$autoSkipTests = -not $hasCodeChanges

# Scope format to the projects the change touched; a wide or unrecognisable change falls back to the
# full solution (empty include list).
$lintInclude = @()
if (-not $SkipLint -and -not $autoSkipLint -and -not $Full) {
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

if ($SkipLint) {
    $skipped.Add([pscustomobject]@{ name = "lint"; reason = "requested" })
}
elseif ($autoSkipLint) {
    $skipped.Add([pscustomobject]@{ name = "lint"; reason = "no code changes" })
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

if ($SkipTests) {
    $skipped.Add([pscustomobject]@{ name = "test"; reason = "requested" })
}
elseif ($autoSkipTests) {
    $skipped.Add([pscustomobject]@{ name = "test"; reason = "no code changes" })
}
else {
    Invoke-Gate -Name "test" -Script "eng/test.ps1" -ScriptArguments $testArguments
}

if ($Pack) {
    Invoke-Gate -Name "pack" -Script "eng/pack.ps1"
}

$green = -not ($gates | Where-Object { $_.exitCode -ne 0 })
$record = [pscustomobject]@{
    stage        = $stageName
    timestampUtc = (Get-Date).ToUniversalTime().ToString("o")
    branch       = $branch
    commit       = $sha
    dirty        = $dirty
    version      = $version
    full         = [bool]$Full
    changed      = $codeChanges.Count
    gates        = $gates
    skipped      = $skipped
    green        = $green
}

$recordPath = Join-Path $gatesRoot "$stageName.json"
$record | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $recordPath -Encoding utf8

$summaryParts = @($gates | ForEach-Object {
        "{0}={1}({2}s)" -f $_.name, $(if ($_.exitCode -eq 0) { "PASS" } else { "FAIL" }), $_.seconds
    })
$summaryParts += @($skipped | ForEach-Object { "{0}=SKIP({1})" -f $_.name, $_.reason })
$summary = $summaryParts -join " "
$dirtyMark = if ($dirty) { "*" } else { "" }
$evidence = "verify $stageName $sha$dirtyMark ($version): $summary -> artifacts/gates/$stageName.json"
Write-Host ""
Write-Host $evidence

if (-not $green) { exit 1 }
exit 0
