<#
.SYNOPSIS
Runs the gates a stage needs and writes artifacts/gates/<stage>.json.

.DESCRIPTION
Docs-only stages run the docs check, viewer stages the viewer gate, tooling stages the gate fixtures,
code stages lint and the tests their projects reach. The last line is the evidence line for the plan.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Stage,

    [switch]$Pack,
    [switch]$SkipLint,
    [switch]$SkipDocs,
    [switch]$SkipTests,
    [switch]$SkipViewer,
    [switch]$NoRestore,
    [switch]$Full,

    # A code stage that skipped its code gates may only record green when the caller says so
    # explicitly; the record then names the approval and the skipped gates.
    [switch]$AllowSkippedCodeGates
)

# One gate command per stage: it runs the checks CI runs, in the cheapest-first order, and writes a
# machine-readable record to artifacts/gates/<stage>.json so a plan row can quote evidence instead of
# prose. The scope follows the stage: uncommitted changes while the tree is dirty, the HEAD commit
# when it is not. A docs-only stage pays only the docs check, a viewer-only stage pays only
# the viewer gate (install, unit tests and build in viewer/), a tooling stage runs the gate
# fixtures, and a code stage runs dotnet format over the projects it touched plus the test projects those
# projects reach, falling back to the full solution and suite when the change is wide or unmappable.
# A code change whose code gates were skipped (it is already committed, or -SkipLint/-SkipTests was
# passed) records `incomplete` and is not green unless -AllowSkippedCodeGates names the exception.
# -Full forces the full test suite and the gate fixtures; the full-solution lint runs in CI's own
# step. A failed gate does not stop the remaining gates: the record says what was green and what was
# not.

$ErrorActionPreference = "Stop"
Import-Module (Join-Path $PSScriptRoot "../lib/Proto.Eng.psm1") -Force
$repository = Get-ProtoRepository
$gatesRoot = Join-Path $repository "artifacts/gates"
New-Item -ItemType Directory -Path $gatesRoot -Force | Out-Null

$stageName = ($Stage -replace '[^A-Za-z0-9._-]', '-')
$sha = (& git -C $repository rev-parse --short HEAD).Trim()
$branch = (& git -C $repository branch --show-current).Trim()
$version = Get-ProtoVersion

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

# Maps changed files to the project directories that own them, walking up to the nearest csproj.
function Get-ChangedProjectDirectories {
    param([string[]]$Paths)

    $directories = New-Object System.Collections.Generic.HashSet[string]([StringComparer]::OrdinalIgnoreCase)
    foreach ($path in $Paths) {
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

    return $directories
}

# The test projects a change can reach: the touched projects plus every project that references them,
# transitively. A wide or unmappable change returns empty, and the caller runs the full suite.
function Get-TestProjectInclude {
    param([System.Collections.Generic.HashSet[string]]$ChangedDirectories)

    if ($ChangedDirectories.Count -eq 0 -or $ChangedDirectories.Count -gt 12) { return "" }

    $references = @{}
    $roots = @("src", "tests", "samples") | ForEach-Object { Join-Path $repository $_ }
    $projects = @(Get-ChildItem -Path $roots -Recurse -Filter *.csproj -File |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' })
    foreach ($project in $projects) {
        $directory = Split-Path -Parent $project.FullName
        try { [xml]$xml = Get-Content -LiteralPath $project.FullName } catch { continue }
        $referenced = New-Object System.Collections.Generic.List[string]
        foreach ($reference in @($xml.Project.ItemGroup.ProjectReference)) {
            if ($null -eq $reference -or -not $reference.Include) { continue }
            $resolved = [IO.Path]::GetFullPath((Join-Path $directory ($reference.Include -replace '\\', '/')))
            $referenced.Add((Split-Path -Parent $resolved))
        }

        $references[$directory] = $referenced
    }

    $affected = New-Object System.Collections.Generic.HashSet[string]($ChangedDirectories, [StringComparer]::OrdinalIgnoreCase)
    $grew = $true
    while ($grew) {
        $grew = $false
        foreach ($directory in @($references.Keys)) {
            if ($affected.Contains($directory)) { continue }
            foreach ($reference in $references[$directory]) {
                if ($affected.Contains($reference)) {
                    [void]$affected.Add($directory)
                    $grew = $true
                    break
                }
            }
        }
    }

    $tests = @($affected | Where-Object {
            $_ -match '[\\/]tests[\\/]' -or $_ -match '[\\/]samples[\\/]Northstar\.ProtoTest'
        })
    if ($tests.Count -eq 0 -or $tests.Count -gt 12) { return "" }
    return ($tests -join ';')
}

$changed = Get-ChangedPath
$dirty = $changed.Count -gt 0
$scope = if ($dirty) { "working-tree" } else { "head-commit" }
$stageChanged = if ($dirty) { $changed } else { Get-CommittedPath }

$codePattern = '\.(cs|csproj|props|targets|slnx)$|^global\.json$'
$scriptPattern = '^eng/.*\.(ps1|psm1)$|^proto(\.ps1|\.cmd)?$|^\.github/workflows/.*\.ya?ml$'
$viewerPattern = '^viewer/'
$codeChanges = @($stageChanged | Where-Object { $_ -match $codePattern })
$scriptChanges = @($stageChanged | Where-Object { $_ -match $scriptPattern })
$viewerChanges = @($stageChanged | Where-Object { $_ -match $viewerPattern })

# The code gates apply to a code change no matter where it lives; they can run it only while the
# change is uncommitted (the working-tree scope they are computed over is gone once committed) or
# under -Full. A committed code change with skipped gates is an incomplete record, not a green one.
$expectsCodeGates = $Full -or $codeChanges.Count -gt 0
$canRunCodeGates = $Full -or ($dirty -and $codeChanges.Count -gt 0)
$codeSkipReason = if ($codeChanges.Count -gt 0) { "committed code changes; re-run with -Full to verify them" } else { "no code changes" }

$changedProjectDirectories = Get-ChangedProjectDirectories -Paths $changed

# Format checking always runs over the whole solution: `dotnet format --include` silently checks
# nothing for code-style/analyzer diagnostics (measured twice), so a scoped format check was a false
# green. The include scoping that remains is the test-project filter below and the duplication scan.
# The test stage scopes to the touched projects plus everything that references them. A wide
# or unmappable change falls back to the full suite, and -Full always runs every discovered project.
$testInclude = ""
if (-not $SkipTests -and $canRunCodeGates -and -not $Full) {
    $testInclude = Get-TestProjectInclude -ChangedDirectories $changedProjectDirectories
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

# A gate that shares nothing with the .NET build (documentation checks, or real gate fixtures running
# in throwaway repositories) can run in a child process alongside the next gate. Start returns null
# when jobs are unavailable or fail to start; the caller then runs the gate directly.
function Start-GateJob {
    param(
        [string]$Name,
        [string]$Script
    )

    if (-not (Get-Command Start-Job -ErrorAction SilentlyContinue)) { return $null }

    Write-Host ""
    Write-Host "=== verify: $Name ($Script, runs alongside the next gate) ==="
    $started = Get-Date
    try {
        $job = Start-Job -ArgumentList (Join-Path $repository $Script) -ScriptBlock {
            param($ScriptPath)

            $output = & pwsh -NoProfile -File $ScriptPath 2>&1
            [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = ($output | Out-String) }
        }
    }
    catch {
        Write-Host "=== verify: $Name could not start in a child process; it will run after the next gate ==="
        return $null
    }

    return [pscustomobject]@{ Name = $Name; Script = $Script; Started = $started; Job = $job }
}

function Complete-GateJob {
    param([object]$Pending)

    $received = Receive-Job -Wait -Job $Pending.Job
    Remove-Job -Job $Pending.Job
    $exitCode = if ($null -eq $received) { 1 } else { [int]$received.ExitCode }
    if ($exitCode -ne 0 -and $null -ne $received) { Write-Host $received.Output }
    $seconds = [math]::Round(((Get-Date) - $Pending.Started).TotalSeconds, 1)
    $gates.Add([pscustomobject]@{
            name     = $Pending.Name
            script   = $Pending.Script
            exitCode = $exitCode
            seconds  = $seconds
        })
    $result = if ($exitCode -eq 0) { "passed" } else { "FAILED ($exitCode)" }
    Write-Host "=== verify: $($Pending.Name) $result in ${seconds}s ==="
}

$lintArguments = @()
$testArguments = @()
if ($NoRestore) {
    $lintArguments += "-NoRestore"
    $testArguments += "-NoRestore"
}

if ($testInclude) {
    $testArguments += @("-Include", $testInclude)
}

$runLint = -not ($SkipLint -or -not $canRunCodeGates)
$runDocs = -not $SkipDocs
$runTests = -not ($SkipTests -or -not $canRunCodeGates)
$runScripts = $Full -or $scriptChanges.Count -gt 0

# The viewer gate shares nothing with the .NET build, so it runs in a child process while lint or
# the suite runs, mirroring the docs gate. A viewer-only stage has no such next gate, so it runs
# directly below. Unlike the code gates it needs no scoping, so it also runs on a clean tree.
$runViewer = (-not $SkipViewer) -and ($viewerChanges.Count -gt 0)

# The documentation check reads files only, so it runs in a child process while lint builds.
$docsPending = if ($runDocs -and $runLint) { Start-GateJob -Name "docs" -Script "eng/commands/docs-check.ps1" } else { $null }
$viewerPending = if ($runViewer -and ($runLint -or $runTests)) { Start-GateJob -Name "viewer" -Script "eng/commands/viewer-test.ps1" } else { $null }

if (-not $runLint) {
    $reason = if ($SkipLint) { "requested" } else { $codeSkipReason }
    $skipped.Add([pscustomobject]@{ name = "lint"; reason = $reason })
    if ($expectsCodeGates) { $skippedCodeGates.Add([pscustomobject]@{ name = "lint"; reason = $reason }) }
}
else {
    Invoke-Gate -Name "lint" -Script "eng/commands/lint.ps1" -ScriptArguments $lintArguments
}

if (-not $runDocs) {
    $skipped.Add([pscustomobject]@{ name = "docs"; reason = "requested" })
}
elseif ($null -ne $docsPending) {
    Complete-GateJob -Pending $docsPending
}
else {
    Invoke-Gate -Name "docs" -Script "eng/commands/docs-check.ps1"
}

# The gate fixtures run the real gate scripts in throwaway repositories with stubbed gates; they
# execute the repository's own MTP binaries, so they run after the suite, not next to it.
if (-not $runTests) {
    $reason = if ($SkipTests) { "requested" } else { $codeSkipReason }
    $skipped.Add([pscustomobject]@{ name = "test"; reason = $reason })
    if ($expectsCodeGates) { $skippedCodeGates.Add([pscustomobject]@{ name = "test"; reason = $reason }) }
}
else {
    Invoke-Gate -Name "test" -Script "eng/commands/test.ps1" -ScriptArguments $testArguments
}

if (-not $runViewer) {
    if ($SkipViewer -and $viewerChanges.Count -gt 0) {
        $skipped.Add([pscustomobject]@{ name = "viewer"; reason = "requested" })
    }
}
elseif ($null -ne $viewerPending) {
    Complete-GateJob -Pending $viewerPending
}
else {
    Invoke-Gate -Name "viewer" -Script "eng/commands/viewer-test.ps1"
}

# The gate scripts are themselves under test; a stage that touches them (or -Full) proves them.
if ($runScripts) {
    # The fixtures execute the real MTP test binaries in this repository, so running them next to the
    # suite races the same projects; this gate stays sequential.
    Invoke-Gate -Name "scripts" -Script "eng/commands/gates-test.ps1"
}

if ($Pack) {
    # The suite builds the Release tree, so pack packs what the tests just ran; only a skipped suite
    # needs the build inside the pack gate.
    $packArguments = @()
    if ($runTests) { $packArguments += "-NoBuild" }
    Invoke-Gate -Name "pack" -Script "eng/commands/pack.ps1" -ScriptArguments $packArguments
}

$incomplete = $skippedCodeGates.Count -gt 0
$classification = if ($incomplete) { "incomplete" } elseif ($expectsCodeGates) { "code" } elseif ($scriptChanges.Count -gt 0) { "tooling" } elseif ($viewerChanges.Count -gt 0) { "viewer" } else { "docs-only" }
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
    changed               = $stageChanged.Count
    codeChanges           = $codeChanges.Count
    scriptChanges         = $scriptChanges.Count
    viewerChanges         = $viewerChanges.Count
    scopedTests           = $testInclude
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
if ($testInclude) {
    $flags += "tests=scoped"
}
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
