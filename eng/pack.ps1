[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$OutputPath = "artifacts/packages",
    [switch]$NoBuild,
    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"
$repository = Split-Path -Parent $PSScriptRoot
$packages = @(
    "src/ProtoTest.Core/ProtoTest.Core.csproj",
    "src/ProtoTest.Testcontainers/ProtoTest.Testcontainers.csproj",
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
    "src/ProtoTest.Grpc/ProtoTest.Grpc.csproj",
    "src/ProtoTest.Messaging/ProtoTest.Messaging.csproj",
    "src/ProtoTest.Messaging.RabbitMq/ProtoTest.Messaging.RabbitMq.csproj",
    "src/ProtoTest.Messaging.RabbitMq.Testcontainers/ProtoTest.Messaging.RabbitMq.Testcontainers.csproj",
    "src/ProtoTest.Sheets/ProtoTest.Sheets.csproj",
    "src/ProtoTest.AspNetCore/ProtoTest.AspNetCore.csproj",
    "src/ProtoTest.OpenApi/ProtoTest.OpenApi.csproj",
    "src/ProtoTest.OpenTelemetry/ProtoTest.OpenTelemetry.csproj",
    "src/ProtoTest.Reporting/ProtoTest.Reporting.csproj",
    "src/ProtoTest.Web/ProtoTest.Web.csproj",
    "src/ProtoTest.Web.Playwright/ProtoTest.Web.Playwright.csproj",
    "src/ProtoTest.Web.Selenium/ProtoTest.Web.Selenium.csproj"
)

$packable = Get-ChildItem -Path (Join-Path $repository "src/*/*.csproj") |
    Where-Object { Select-String -Path $_.FullName -Pattern "<IsPackable>true</IsPackable>" -Quiet } |
    ForEach-Object { $_.FullName.Substring($repository.Length + 1).Replace('\', '/') }

$missing = @($packable | Where-Object { $packages -notcontains $_ })
if ($missing.Count -gt 0) {
    throw "Packable projects missing from eng/pack.ps1: $($missing -join ', ')."
}

$extra = @($packages | Where-Object { $packable -notcontains $_ })
if ($extra.Count -gt 0) {
    throw "eng/pack.ps1 lists projects that are not packable: $($extra -join ', ')."
}

$unknown = @($packages | Where-Object { -not (Test-Path -LiteralPath (Join-Path $repository $_)) })
if ($unknown.Count -gt 0) {
    throw "eng/pack.ps1 lists projects that do not exist: $($unknown -join ', ')."
}

$output = if ([IO.Path]::IsPathRooted($OutputPath)) { $OutputPath } else { Join-Path $repository $OutputPath }
$packArguments = @("--configuration", $Configuration, "--output", $output)
if ($NoBuild) { $packArguments += "--no-build" }
if ($NoRestore) { $packArguments += "--no-restore" }

foreach ($project in $packages) {
    & dotnet pack (Join-Path $repository $project) @packArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Packing '$project' failed with exit code $LASTEXITCODE."
    }
}
