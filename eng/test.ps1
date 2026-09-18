[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:TESTINGPLATFORM_TELEMETRY_OPTOUT = "1"

function Invoke-DotNet {
    param([Parameter(ValueFromRemainingArguments)][string[]]$Arguments)

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

if (-not $NoRestore) {
    Invoke-DotNet restore ProtoTest.slnx
}

Invoke-DotNet build ProtoTest.slnx --configuration $Configuration --no-restore

# Test projects are discovered, not listed: a new project is in the suite the moment it is a test
# project. TUnit and xUnit.net v3 are Microsoft Testing Platform executables and run explicitly below;
# xUnit.net v2 still uses VSTest, so the repository intentionally runs both models.
$repository = Split-Path -Parent $PSScriptRoot
$testsRoot = Join-Path $repository "tests"
$mtpProjects = @(
    "tests/ProtoTest.TUnit.Tests/ProtoTest.TUnit.Tests.csproj",
    "tests/ProtoTest.Xunit3.Tests/ProtoTest.Xunit3.Tests.csproj"
)
$vstestProjects = Get-ChildItem -Path $testsRoot -Recurse -Filter *.csproj |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
    Where-Object { Select-String -Path $_.FullName -Pattern 'IsTestProject>true|Microsoft\.NET\.Test\.Sdk|MSTest\.TestAdapter|MSTest\.Sdk|Include="MSTest"|NUnit3TestAdapter|xunit\.runner\.visualstudio|TUnit' -Quiet } |
    ForEach-Object { $_.FullName.Substring($repository.Length + 1).Replace('\', '/') } |
    Where-Object { $_ -notin $mtpProjects } |
    Sort-Object
$vstestProjects += "samples/ProtoTest.Demo/ProtoTest.Demo.csproj"

foreach ($project in $vstestProjects) {
    Invoke-DotNet test $project --configuration $Configuration --no-build --no-restore --verbosity minimal
}

Invoke-DotNet run --project tests/ProtoTest.TUnit.Tests/ProtoTest.TUnit.Tests.csproj `
    --configuration $Configuration --no-build --no-restore
Invoke-DotNet run --project tests/ProtoTest.Xunit3.Tests/ProtoTest.Xunit3.Tests.csproj `
    --configuration $Configuration --no-build --no-restore
