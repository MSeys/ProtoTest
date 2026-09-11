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

$vstestProjects = @(
    "tests/ProtoTest.Core.Tests/ProtoTest.Core.Tests.csproj",
    "tests/ProtoTest.Http.Tests/ProtoTest.Http.Tests.csproj",
    "tests/ProtoTest.Json.Tests/ProtoTest.Json.Tests.csproj",
    "tests/ProtoTest.NUnit.Tests/ProtoTest.NUnit.Tests.csproj",
    "tests/ProtoTest.MSTest.Tests/ProtoTest.MSTest.Tests.csproj",
    "tests/ProtoTest.Xunit.Tests/ProtoTest.Xunit.Tests.csproj",
    "tests/ProtoTest.Rest.Tests/ProtoTest.Rest.Tests.csproj",
    "tests/ProtoTest.GraphQL.Tests/ProtoTest.GraphQL.Tests.csproj",
    "tests/ProtoTest.AspNetCore.Tests/ProtoTest.AspNetCore.Tests.csproj",
    "tests/ProtoTest.OpenApi.Tests/ProtoTest.OpenApi.Tests.csproj",
    "tests/ProtoTest.Reporting.Tests/ProtoTest.Reporting.Tests.csproj",
    "samples/ProtoTest.Rest.Demo/ProtoTest.Rest.Demo.csproj",
    "samples/ProtoTest.GraphQL.Demo/ProtoTest.GraphQL.Demo.csproj",
    "samples/ProtoTest.AspNetCore.Demo/ProtoTest.AspNetCore.Demo.csproj",
    "samples/ProtoTest.SampleApp.RestDemo/ProtoTest.SampleApp.RestDemo.csproj",
    "samples/ProtoTest.SampleApp.GraphQLDemo/ProtoTest.SampleApp.GraphQLDemo.csproj"
)

foreach ($project in $vstestProjects) {
    Invoke-DotNet test $project --configuration $Configuration --no-build --no-restore --verbosity minimal
}

# TUnit and xUnit.net v3 are Microsoft Testing Platform executables. xUnit.net v2
# still uses VSTest, so the repository intentionally runs both models explicitly.
Invoke-DotNet run --project tests/ProtoTest.TUnit.Tests/ProtoTest.TUnit.Tests.csproj `
    --configuration $Configuration --no-build --no-restore
Invoke-DotNet run --project tests/ProtoTest.Xunit3.Tests/ProtoTest.Xunit3.Tests.csproj `
    --configuration $Configuration --no-build --no-restore
