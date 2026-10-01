<#
.SYNOPSIS
Installs the packed template and builds and tests a starter for every runner.
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$PackagesDirectory = ""
)

# The starter template proved end to end against the packages a run packed: install the template
# package, generate a project for every runner it offers, then restore, build and test each one
# against the local feed with nuget.org as the fallback. CI runs this after pack; the gate fixtures
# run it locally wherever the packed feed exists, so the two never drift.

$ErrorActionPreference = "Stop"
if (Test-Path variable:PSNativeCommandUseErrorActionPreference) { $PSNativeCommandUseErrorActionPreference = $false }

Import-Module (Join-Path $PSScriptRoot "../lib/Proto.Eng.psm1") -Force
$repository = Get-ProtoRepository
if ([string]::IsNullOrWhiteSpace($PackagesDirectory)) {
    $PackagesDirectory = Join-Path $repository "artifacts/packages"
}

if (-not (Test-Path -LiteralPath $PackagesDirectory)) {
    throw "The packages directory '$PackagesDirectory' does not exist; run ./proto pack first."
}

$package = Get-ChildItem -LiteralPath $PackagesDirectory -Filter "ProtoTest.Templates.*.nupkg" |
    Where-Object { $_.Name -notlike '*.snupkg' } |
    Select-Object -First 1
if (-not $package) {
    throw "The packed ProtoTest.Templates package was not found in '$PackagesDirectory'."
}

# A previous local run may have installed the same version, and the installer refuses to overwrite
# its own package file; replace it so the starter is always generated from the feed under test.
dotnet new uninstall ProtoTest.Templates 2>$null | Out-Null
dotnet new install $package.FullName
if ($LASTEXITCODE -ne 0) { throw "dotnet new install failed." }

$packages = (Resolve-Path -LiteralPath $PackagesDirectory).Path
$starterRoot = Join-Path ([IO.Path]::GetTempPath()) ("prototest-starter-" + [Guid]::NewGuid().ToString("N").Substring(0, 8))
New-Item -ItemType Directory -Path $starterRoot -Force | Out-Null
try {
    foreach ($runner in @("nunit", "xunit", "xunit3", "tunit", "mstest")) {
        $starter = Join-Path $starterRoot "starters/$runner"
        dotnet new prototest --no-restore -n Starter -o $starter --runner $runner
        if ($LASTEXITCODE -ne 0) { throw "dotnet new prototest --runner $runner failed." }

        # The starter resolves the packages this run just packed, and falls back to nuget.org for
        # everything else.
        @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$packages" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
"@ | Set-Content -Path (Join-Path $starter "NuGet.config")

        # Run from the starter root: the Microsoft Testing Platform variants select the MTP test
        # runner in their global.json, which the SDK resolves from the working directory.
        Push-Location $starter
        try {
            dotnet restore Starter.slnx
            if ($LASTEXITCODE -ne 0) { throw "restore failed for the $runner starter." }
            dotnet build Starter.slnx --configuration $Configuration --no-restore
            if ($LASTEXITCODE -ne 0) { throw "build failed for the $runner starter." }
            # The MTP variants select the Microsoft Testing Platform in global.json, where dotnet test
            # takes the project through --project; the VSTest variants take it as the argument.
            $usesMtp = (Test-Path -LiteralPath "global.json") -and
                ((Get-Content -LiteralPath "global.json" -Raw) -match "Microsoft\.Testing\.Platform")
            $project = @("Starter.Tests/Starter.Tests.csproj")
            if ($usesMtp) { $project = @("--project") + $project }
            dotnet test @project --configuration $Configuration --no-build --no-restore
            if ($LASTEXITCODE -ne 0) { throw "test failed for the $runner starter." }

            # The agent setup works as shipped: the pinned tools restore from this feed, and the CLI
            # reads the trace the run just wrote.
            foreach ($agentFile in @("AGENTS.md", ".mcp.json", ".claude/skills/prototest-evidence-loop/SKILL.md", ".claude/skills/prototest-write-test/SKILL.md")) {
                if (-not (Test-Path -LiteralPath $agentFile)) { throw "the $runner starter has no $agentFile." }
            }
            dotnet tool restore
            if ($LASTEXITCODE -ne 0) { throw "dotnet tool restore failed for the $runner starter." }
            $trace = Get-ChildItem -Path "Starter.Tests" -Recurse -Filter "*.prototrace" | Select-Object -First 1
            if (-not $trace) { throw "the $runner starter wrote no trace." }
            dotnet tool run prototest summary $trace.FullName
            if ($LASTEXITCODE -ne 0) { throw "prototest summary failed for the $runner starter's trace." }
        }
        finally {
            Pop-Location
        }

        Write-Host "template starter: $runner generated, restored, built, tested and read back."
    }
}
finally {
    Remove-Item -LiteralPath $starterRoot -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "template starter: all five runners passed against '$packages'."
exit 0
