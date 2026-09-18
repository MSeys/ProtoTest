[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$OutputPath = "artifacts/packages",
    [switch]$NoBuild,
    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"
$packages = @(
    "src/ProtoTest.Core/ProtoTest.Core.csproj",
    "src/ProtoTest.Http/ProtoTest.Http.csproj",
    "src/ProtoTest.Json/ProtoTest.Json.csproj",
    "src/ProtoTest.Data/ProtoTest.Data.csproj",
    "src/ProtoTest.NUnit/ProtoTest.NUnit.csproj",
    "src/ProtoTest.MSTest/ProtoTest.MSTest.csproj",
    "src/ProtoTest.TUnit/ProtoTest.TUnit.csproj",
    "src/ProtoTest.Xunit/ProtoTest.Xunit.csproj",
    "src/ProtoTest.Xunit3/ProtoTest.Xunit3.csproj",
    "src/ProtoTest.Rest/ProtoTest.Rest.csproj",
    "src/ProtoTest.Sql/ProtoTest.Sql.csproj",
    "src/ProtoTest.Sql.EntityFrameworkCore/ProtoTest.Sql.EntityFrameworkCore.csproj",
    "src/ProtoTest.Sql.Testcontainers/ProtoTest.Sql.Testcontainers.csproj",
    "src/ProtoTest.GraphQL/ProtoTest.GraphQL.csproj",
    "src/ProtoTest.AspNetCore/ProtoTest.AspNetCore.csproj",
    "src/ProtoTest.OpenApi/ProtoTest.OpenApi.csproj",
    "src/ProtoTest.OpenTelemetry/ProtoTest.OpenTelemetry.csproj",
    "src/ProtoTest.Reporting/ProtoTest.Reporting.csproj",
    "src/ProtoTest.Web/ProtoTest.Web.csproj",
    "src/ProtoTest.Web.Playwright/ProtoTest.Web.Playwright.csproj",
    "src/ProtoTest.Web.Selenium/ProtoTest.Web.Selenium.csproj"
)

$packable = Get-ChildItem -Path "src/*/*.csproj" |
    Where-Object { Select-String -Path $_.FullName -Pattern "<IsPackable>true</IsPackable>" -Quiet } |
    ForEach-Object { (Resolve-Path -Relative $_.FullName) -replace '^\.\\', '' -replace '\\', '/' }

$missing = @($packable | Where-Object { $packages -notcontains $_ })
if ($missing.Count -gt 0) {
    throw "Packable projects missing from eng/pack.ps1: $($missing -join ', ')."
}

$unknown = @($packages | Where-Object { -not (Test-Path -LiteralPath $_) })
if ($unknown.Count -gt 0) {
    throw "eng/pack.ps1 lists projects that do not exist: $($unknown -join ', ')."
}

$packArguments = @("--configuration", $Configuration, "--output", $OutputPath)
if ($NoBuild) { $packArguments += "--no-build" }
if ($NoRestore) { $packArguments += "--no-restore" }

foreach ($project in $packages) {
    & dotnet pack $project @packArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Packing '$project' failed with exit code $LASTEXITCODE."
    }
}
