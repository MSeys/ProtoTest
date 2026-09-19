[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:TESTINGPLATFORM_TELEMETRY_OPTOUT = "1"

$repository = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repository "ProtoTest.slnx"

function Invoke-DotNet {
    param([Parameter(ValueFromRemainingArguments)][string[]]$Arguments)

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

if (-not $NoRestore) {
    Invoke-DotNet restore $solution
}

Invoke-DotNet build $solution --configuration $Configuration --no-restore

# Test projects are discovered, not listed: a new project is in the suite the moment it is a test
# project. TUnit and xUnit.net v3 are Microsoft Testing Platform executables and run explicitly below;
# xUnit.net v2 still uses VSTest, so the repository intentionally runs both models.
$testsRoot = Join-Path $repository "tests"
$mtpProjects = @(
    (Join-Path $testsRoot "ProtoTest.TUnit.Tests/ProtoTest.TUnit.Tests.csproj"),
    (Join-Path $testsRoot "ProtoTest.Xunit3.Tests/ProtoTest.Xunit3.Tests.csproj")
)
foreach ($mtpProject in $mtpProjects) {
    if (-not (Test-Path -LiteralPath $mtpProject)) {
        throw "The Microsoft Testing Platform project '$mtpProject' does not exist."
    }
}

$vstestProjects = Get-ChildItem -Path $testsRoot -Recurse -Filter *.csproj |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } |
    Where-Object { Select-String -Path $_.FullName -Pattern 'IsTestProject>true|Microsoft\.NET\.Test\.Sdk|MSTest\.TestAdapter|MSTest\.Sdk|Include="MSTest"|NUnit3TestAdapter|xunit\.runner\.visualstudio|TUnit' -Quiet } |
    ForEach-Object { $_.FullName } |
    Where-Object { $_ -notin $mtpProjects } |
    Sort-Object

# Test projects follow the *.Tests naming convention, so the directories under tests/ are the expected
# discovered set. Comparing against them fails loudly when the discovery predicate drifts instead of
# silently dropping a suite; a new *.Tests project is expected the moment it exists. The Demo sample is
# appended after this check because it lives under samples/, not tests/.
$expectedProjects = @(Get-ChildItem -Path $testsRoot -Directory |
    Where-Object { $_.Name.EndsWith(".Tests", [StringComparison]::Ordinal) } |
    ForEach-Object { $_.Name } |
    Sort-Object)
$discoveredProjects = @($vstestProjects + $mtpProjects) |
    ForEach-Object { Split-Path -Leaf (Split-Path -Parent $_) } |
    Sort-Object -Unique
$missingProjects = @($expectedProjects | Where-Object { $_ -notin $discoveredProjects })
if ($missingProjects.Count -gt 0) {
    throw "Test discovery missed $($missingProjects.Count) project(s): $($missingProjects -join ', ')."
}

$vstestProjects += (Join-Path $repository "samples/ProtoTest.Demo/ProtoTest.Demo.csproj")

foreach ($project in $vstestProjects) {
    # A discovered project that runs zero tests means the predicate matched a project the runner cannot
    # execute; the exit code alone would not say so.
    $output = & dotnet test $project --configuration $Configuration --no-build --no-restore --verbosity minimal 2>&1
    $output | ForEach-Object { Write-Host $_ }
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet test $project failed with exit code $LASTEXITCODE."
    }

    $text = $output -join [Environment]::NewLine
    if ($text -match 'No test is available in' -or $text -match 'No test matches the given testcase filter') {
        throw "The discovered test project '$project' ran zero tests; fix the discovery predicate or the project."
    }
}

foreach ($mtpProject in $mtpProjects) {
    # The MTP executables have no "No test is available" text to match, so each run carries its own
    # zero-test guard. The separator travels as an array element: a literal -- is swallowed by the
    # PowerShell parser. TUnit's platform exposes --minimum-expected-tests; xUnit.net v3's in-process
    # runner does not, so its execution summary is inspected instead.
    $mtpArguments = @(
        "run", "--project", $mtpProject,
        "--configuration", $Configuration, "--no-build", "--no-restore",
        "--"
    )
    if ((Split-Path -Leaf (Split-Path -Parent $mtpProject)) -eq "ProtoTest.TUnit.Tests") {
        $mtpArguments += @("--minimum-expected-tests", "1")
        Invoke-DotNet @mtpArguments
        continue
    }

    $output = & dotnet @mtpArguments 2>&1
    $output | ForEach-Object { Write-Host $_ }
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet run $mtpProject failed with exit code $LASTEXITCODE."
    }
    if (($output -join [Environment]::NewLine) -match 'Total:\s*0\b') {
        throw "The Microsoft Testing Platform project '$mtpProject' ran zero tests."
    }
}
