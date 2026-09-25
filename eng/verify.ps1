[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Stage,

    [switch]$Pack,
    [switch]$SkipLint,
    [switch]$SkipDocs,
    [switch]$SkipTests,
    [switch]$NoRestore
)

# One gate command per stage: it runs the same checks CI runs, in the cheapest-first order, and writes
# a machine-readable record to artifacts/gates/<stage>.json so a plan row can quote evidence instead
# of prose. A failed gate does not stop the remaining gates: the record should say what was green and
# what was not in one run.

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

$gates = New-Object System.Collections.Generic.List[object]

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

if (-not $SkipLint) { Invoke-Gate -Name "lint" -Script "eng/lint.ps1" -ScriptArguments $lintArguments }
if (-not $SkipDocs) { Invoke-Gate -Name "docs" -Script "eng/check-docs.ps1" }
if (-not $SkipTests) { Invoke-Gate -Name "test" -Script "eng/test.ps1" -ScriptArguments $testArguments }
if ($Pack) { Invoke-Gate -Name "pack" -Script "eng/pack.ps1" }

$green = -not ($gates | Where-Object { $_.exitCode -ne 0 })
$record = [pscustomobject]@{
    stage        = $stageName
    timestampUtc = (Get-Date).ToUniversalTime().ToString("o")
    branch       = $branch
    commit       = $sha
    dirty        = $dirty
    version      = $version
    gates        = $gates
    green        = $green
}

$recordPath = Join-Path $gatesRoot "$stageName.json"
$record | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $recordPath -Encoding utf8

$summary = @($gates | ForEach-Object {
        "{0}={1}({2}s)" -f $_.name, $(if ($_.exitCode -eq 0) { "PASS" } else { "FAIL" }), $_.seconds
    }) -join " "
$dirtyMark = if ($dirty) { "*" } else { "" }
$evidence = "verify $stageName $sha$dirtyMark ($version): $summary -> artifacts/gates/$stageName.json"
Write-Host ""
Write-Host $evidence

if (-not $green) { exit 1 }
exit 0
