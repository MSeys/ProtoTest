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
$vstestProjects += (Join-Path $repository "samples/ProtoTest.Demo/ProtoTest.Demo.csproj")

# A change in package names must fail loudly instead of silently dropping projects from the suite.
if ($vstestProjects.Count -lt 2) {
    throw "Test discovery matched only $($vstestProjects.Count) project(s); the discovery predicate no longer sees the test projects."
}

foreach ($project in $vstestProjects) {
    Invoke-DotNet test $project --configuration $Configuration --no-build --no-restore --verbosity minimal
}

foreach ($mtpProject in $mtpProjects) {
    Invoke-DotNet run --project $mtpProject `
        --configuration $Configuration --no-build --no-restore
}
