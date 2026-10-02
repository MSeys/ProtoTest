<#
.SYNOPSIS
Builds the solution and runs every discovered test project, or the ones -Include names.

.DESCRIPTION
Each project's full output goes to artifacts/test-logs/<project>.log; the console gets one line per project
and the tail of a failing one.
#>
[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$NoRestore,

    # Optional semicolon-separated project directories (relative to the repository or absolute) to run
    # instead of every discovered test project; verify passes the projects a stage can reach.
    [string]$Include = ""
)

$ErrorActionPreference = "Stop"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:TESTINGPLATFORM_TELEMETRY_OPTOUT = "1"

Import-Module (Join-Path $PSScriptRoot "../lib/Proto.Eng.psm1") -Force
$repository = Get-ProtoRepository
$solution = Join-Path $repository "ProtoTest.slnx"

$includeDirectories = @()
if (-not [string]::IsNullOrWhiteSpace($Include)) {
    # verify hands over absolute directories; callers may also pass repository-relative ones.
    $includeDirectories = @($Include -split ';' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        ForEach-Object {
            if ([IO.Path]::IsPathRooted($_)) { [IO.Path]::GetFullPath($_) }
            else { [IO.Path]::GetFullPath((Join-Path $repository $_)) }
        })
}

if (-not $NoRestore) {
    Invoke-ProtoNative -Name "test/restore" -FilePath "dotnet" -ArgumentList @("restore", $solution) | Out-Null
}

if ($includeDirectories.Count -gt 0) {
    # A scoped run builds only the projects it will run; each test project builds its own dependency
    # chain, so nothing the suite needs is missing.
    foreach ($directory in $includeDirectories) {
        $scopedProject = Get-ChildItem -LiteralPath $directory -Filter *.csproj -File | Select-Object -First 1
        Invoke-ProtoNative -Name "test/build-$($scopedProject.BaseName)" -FilePath "dotnet" `
            -ArgumentList @("build", $scopedProject.FullName, "--configuration", $Configuration, "--no-restore") | Out-Null
    }
}
else {
    Invoke-ProtoNative -Name "test/build" -FilePath "dotnet" `
        -ArgumentList @("build", $solution, "--configuration", $Configuration, "--no-restore") | Out-Null
}

# Test projects are discovered, not listed: a new project is in the suite the moment it is a test
# project. TUnit and xUnit.net v3 are Microsoft Testing Platform executables and run explicitly below;
# xUnit.net v2 still uses VSTest, so the repository intentionally runs both models.
$testsRoot = Join-Path $repository "tests"
# Each MTP project declares the run-test minimum its suite must meet, so a collapse (discovery
# predicate drift, engine change) fails instead of passing a one-test suite. The bases differ: TUnit's
# --minimum-expected-tests counts tests that actually ran, so the deliberate adapter skip is excluded
# (14 of 15 discovered), while the JUnit total xUnit.net v3 writes includes skipped tests (17, and the
# auto-wrap project's 6 including its skip, its theory counting one test per row).
# Raising a minimum with added tests is free; lowering one is a deliberate edit that names the removals.
$mtpProjects = @(
    @{ Project = (Join-Path $testsRoot "ProtoTest.TUnit.Tests/ProtoTest.TUnit.Tests.csproj"); MinimumTests = 14 },
    @{ Project = (Join-Path $testsRoot "ProtoTest.Xunit3.Tests/ProtoTest.Xunit3.Tests.csproj"); MinimumTests = 17 },
    @{ Project = (Join-Path $testsRoot "ProtoTest.Xunit3.AutoWrap.Tests/ProtoTest.Xunit3.AutoWrap.Tests.csproj"); MinimumTests = 6 }
)
foreach ($mtpProject in $mtpProjects) {
    if (-not (Test-Path -LiteralPath $mtpProject.Project)) {
        throw "The Microsoft Testing Platform project '$($mtpProject.Project)' does not exist."
    }
}

$mtpProjectPaths = @($mtpProjects | ForEach-Object { $_.Project })
$vstestProjects = Get-ChildItem -Path $testsRoot -Recurse -Filter *.csproj |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } |
    Where-Object { Select-String -Path $_.FullName -Pattern 'IsTestProject>true|Microsoft\.NET\.Test\.Sdk|MSTest\.TestAdapter|MSTest\.Sdk|Include="MSTest"|NUnit3TestAdapter|xunit\.runner\.visualstudio|TUnit' -Quiet } |
    ForEach-Object { $_.FullName } |
    Where-Object { $_ -notin $mtpProjectPaths } |
    Sort-Object

# Test projects follow the *.Tests naming convention, so the directories under tests/ are the expected
# discovered set. Comparing against them fails loudly when the discovery predicate drifts instead of
# silently dropping a suite; a new *.Tests project is expected the moment it exists. The sample suites
# are appended after this check because they live under samples/, not tests/.
$expectedProjects = @(Get-ChildItem -Path $testsRoot -Directory |
    Where-Object { $_.Name.EndsWith(".Tests", [StringComparison]::Ordinal) } |
    ForEach-Object { $_.Name } |
    Sort-Object)
$discoveredProjects = @($vstestProjects + $mtpProjectPaths) |
    ForEach-Object { Split-Path -Leaf (Split-Path -Parent $_) } |
    Sort-Object -Unique
$missingProjects = @($expectedProjects | Where-Object { $_ -notin $discoveredProjects })
if ($missingProjects.Count -gt 0) {
    throw "Test discovery missed $($missingProjects.Count) project(s): $($missingProjects -join ', ')."
}

# The app-specific test layer under samples/ is the Learning demo suite; it is discovered by path
# because the *.Tests convention lives under tests/.
$vstestProjects += (Join-Path $repository "samples/Northstar.ProtoTest/Northstar.ProtoTest.csproj")

# A scoped run passes the project directories a stage can reach; the discovery guard above still
# checked every expected project before this filter runs.
if ($includeDirectories.Count -gt 0) {
    $isIncluded = { param($project) $includeDirectories -contains (Split-Path -Parent $project) }
    $vstestProjects = @($vstestProjects | Where-Object { & $isIncluded $_ })
    $mtpProjects = @($mtpProjects | Where-Object { & $isIncluded $_.Project })
}

# The per-project `dotnet test` host startup, not the tests, is most of this stage's wall clock, so the
# suites that own nothing shared run in a small pool while the container/browser/process suites run one
# at a time in front of it. The pool is an allow list: a new project is serial unless it is named here,
# and a name must own no container, browser, port or background process.
$parallelProjectNames = @(
    "ProtoTest.Analyzers.Tests",
    "ProtoTest.AspNetCore.Tests",
    "ProtoTest.Core.Tests",
    "ProtoTest.Data.Tests",
    "ProtoTest.Devices.Tests",
    "ProtoTest.Devices.WebSocket.AspNetCore.Tests",
    "ProtoTest.Devices.WebSocket.Tests",
    "ProtoTest.Diagnosis.Tests",
    "ProtoTest.Extensibility.Tests",
    "ProtoTest.Feedback.Tests",
    "ProtoTest.GraphQL.Tests",
    "ProtoTest.Grpc.Tests",
    "ProtoTest.Hosting.Tests",
    "ProtoTest.Http.Tests",
    "ProtoTest.Json.Tests",
    "ProtoTest.Mcp.Tests",
    "ProtoTest.Messaging.Tests",
    "ProtoTest.MSTest.Tests",
    "ProtoTest.NUnit.Tests",
    "ProtoTest.OpenApi.Tests",
    "ProtoTest.Reporting.Tests",
    "ProtoTest.Rest.Tests",
    "ProtoTest.SampleApp.Domain.Tests",
    "ProtoTest.Sheets.Tests",
    "ProtoTest.Traces.Tests",
    "ProtoTest.Verification.Tests",
    "ProtoTest.Xunit.Tests"
)

$projectName = { param($path) Split-Path -Leaf (Split-Path -Parent $path) }
$parallelProjects = @($vstestProjects | Where-Object { (& $projectName $_) -in $parallelProjectNames })
$serialProjects = @($vstestProjects | Where-Object { (& $projectName $_) -notin $parallelProjectNames })

$logRoot = Join-Path $repository "artifacts/test-logs"
New-Item -ItemType Directory -Path $logRoot -Force | Out-Null

# A benchmark measures wall-clock time, so it must not share the machine: the pool and the serial suites
# run beside each other, and a timing bound then fails on contention instead of on a regression. The pool
# leaves the benchmark category out, and each project that has one runs it alone once everything else is
# done. A project is found by its [Category("Benchmark")] attribute, so there is no list to keep.
$hasBenchmarks = {
    param($project)
    $found = Get-ChildItem -LiteralPath (Split-Path -Parent $project) -Filter *.cs -Recurse -File |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } |
        Select-String -Pattern 'Category\("Benchmark"\)' -List
    @($found).Count -gt 0
}
$benchmarkProjects = @($parallelProjects | Where-Object { & $hasBenchmarks $_ })

$pool = $null
if ($parallelProjects.Count -gt 0 -and (Get-Command Start-Job -ErrorAction SilentlyContinue)) {
    $pool = Start-Job -ArgumentList ($parallelProjects -join "`n"), $Configuration -ScriptBlock {
        param($ProjectList, $Configuration)

        ($ProjectList -split "`n") | ForEach-Object -Parallel {
            $project = $_
            $output = & dotnet test $project --configuration $using:Configuration --no-build --no-restore --verbosity minimal --filter "Category!=Benchmark" 2>&1
            [pscustomobject]@{
                Project  = $project
                ExitCode = $LASTEXITCODE
                Output   = ($output | Out-String)
            }
        } -ThrottleLimit 4
    }
}

foreach ($project in $serialProjects) {
    # A discovered project that runs zero tests means the predicate matched a project the runner cannot
    # execute; the exit code alone would not say so.
    # Keep every project's output, serial ones included: a flake must be diagnosable after the run.
    $name = Split-Path -Leaf (Split-Path -Parent $project)
    $run = Invoke-ProtoNative -Name "dotnet test $name" -FilePath "dotnet" -Log (Join-Path $logRoot "$name.log") `
        -ArgumentList @("test", $project, "--configuration", $Configuration, "--no-build", "--no-restore", "--verbosity", "minimal")

    $text = $run.Text
    if ($text -match 'No test is available in' -or $text -match 'No test matches the given testcase filter') {
        throw "The discovered test project '$project' ran zero tests; fix the discovery predicate or the project."
    }
}

if ($null -ne $pool) {
    $pooled = Receive-Job -Wait -Job $pool
    Remove-Job -Job $pool
    foreach ($result in $pooled) {
        $name = Split-Path -Leaf (Split-Path -Parent $result.Project)
        $log = Join-Path $logRoot "$name.log"
        Set-Content -LiteralPath $log -Value $result.Output -Encoding utf8
        $text = [string]$result.Output
        if ($result.ExitCode -ne 0) {
            Write-ProtoTail $text
            throw "dotnet test $($result.Project) failed with exit code $($result.ExitCode). Log: $log"
        }

        if ($text -match 'No test is available in' -or $text -match 'No test matches the given testcase filter') {
            throw "The discovered test project '$($result.Project)' ran zero tests; fix the discovery predicate or the project."
        }

        Write-Host "dotnet test $name passed (full output: $log)."
    }
}

foreach ($project in $benchmarkProjects) {
    $name = Split-Path -Leaf (Split-Path -Parent $project)
    Invoke-ProtoNative -Name "dotnet test $name benchmarks" -FilePath "dotnet" -Log (Join-Path $logRoot "$name.benchmarks.log") `
        -ArgumentList @("test", $project, "--configuration", $Configuration, "--no-build", "--no-restore", "--verbosity", "minimal", "--filter", "Category=Benchmark") | Out-Null
}

foreach ($mtpProject in $mtpProjects) {
    # The MTP executables have no "No test is available" text to match, so each run carries its own
    # zero-test guard. The separator travels as an array element: a literal -- is swallowed by the
    # PowerShell parser. TUnit's platform enforces --minimum-expected-tests itself; xUnit.net v3's
    # in-process runner exits 0 on a zero-test run, so its structured JUnit result is parsed and
    # compared with the same minimum. the gate fixtures (./proto gates test) prove both guards fail on a zero-test filter.
    $projectPath = $mtpProject.Project
    $minimumTests = [int]$mtpProject.MinimumTests
    $mtpArguments = @(
        "run", "--project", $projectPath,
        "--configuration", $Configuration, "--no-build", "--no-restore",
        "--"
    )
    if ((Split-Path -Leaf (Split-Path -Parent $projectPath)) -eq "ProtoTest.TUnit.Tests") {
        $mtpArguments += @("--minimum-expected-tests", [string]$minimumTests)
        $name = Split-Path -Leaf (Split-Path -Parent $projectPath)
        Invoke-ProtoNative -Name "dotnet run $name" -FilePath "dotnet" -ArgumentList $mtpArguments -Log (Join-Path $logRoot "$name.log") | Out-Null
        continue
    }

    $resultPath = Join-Path ([IO.Path]::GetTempPath()) ("prototest-mtp-" + [Guid]::NewGuid().ToString("N") + ".xml")
    try {
        $mtpArguments += @("-result-junit", $resultPath)
        $name = Split-Path -Leaf (Split-Path -Parent $projectPath)
        Invoke-ProtoNative -Name "dotnet run $name" -FilePath "dotnet" -ArgumentList $mtpArguments -Log (Join-Path $logRoot "$name.log") | Out-Null
        if (-not (Test-Path -LiteralPath $resultPath)) {
            throw "The Microsoft Testing Platform project '$projectPath' wrote no structured result to '$resultPath'."
        }
        [xml]$result = Get-Content -Raw -LiteralPath $resultPath
        $tests = [int]$result.testsuites.tests
        if ($tests -lt $minimumTests) {
            throw "The Microsoft Testing Platform project '$projectPath' ran $tests test(s); the declared minimum is $minimumTests."
        }
    }
    finally {
        Remove-Item -LiteralPath $resultPath -Force -ErrorAction SilentlyContinue
    }
}
