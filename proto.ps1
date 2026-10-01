#!/usr/bin/env pwsh
# The repository's engineering commands behind one entry point. `./proto help` lists them,
# `./proto help <command>` shows a command's parameters. Each command lives in eng/commands.
param(
    [Parameter(Position = 0)]
    [string]$Command = "help",
    [Parameter(ValueFromRemainingArguments)]
    [string[]]$Arguments = @()
)

$ErrorActionPreference = "Stop"
$commands = Join-Path $PSScriptRoot "eng/commands"

# Name, script and the one line `help` prints. Two-word names take their second word from the arguments.
$table = [ordered]@{
    "verify"          = @("verify.ps1", "Run the gates a stage needs and write artifacts/gates/<stage>.json")
    "test"            = @("test.ps1", "Build and run the test projects (-Include to scope)")
    "lint"            = @("lint.ps1", "Shared-helper, internals and formatting checks")
    "pack"            = @("pack.ps1", "Pack and validate every package into artifacts/packages")
    "docs check"      = @("docs-check.ps1", "Check the docs against the source: APIs, keys, links, secrets")
    "docs prose"      = @("../../docs/scripts/prose-check.mjs", "Prose and lesson-length report (--base <ref> for changed pages)")
    "docs site"       = @("docs-site.ps1", "Build the docs site and the API reference into docs/build")
    "docs api"        = @("docs-api.ps1", "Build the DocFX API reference")
    "viewer test"     = @("viewer-test.ps1", "Install, test and build the viewer")
    "traces"          = @("traces.ps1", "Regenerate committed traces: lessons, recipes, demo or all")
    "template test"   = @("template-test.ps1", "Generate, build and test the starter template for every runner")
    "gates test"      = @("gates-test.ps1", "Run the fixtures that prove the gate commands")
    "release cut"     = @("release-cut.ps1", "Roll CHANGELOG.md for the release version")
    "release publish" = @("release-publish.ps1", "Push the packed packages to NuGet (-DryRun to plan)")
}

function Show-Help {
    Write-Host "Usage: ./proto <command> [options]    (-Verbose streams the full output)"
    Write-Host ""
    foreach ($name in $table.Keys) { Write-Host ("  {0,-16} {1}" -f $name, $table[$name][1]) }
    Write-Host ""
    Write-Host "  help <command>   Show a command's parameters"
}

$name = $Command
if (-not $table.Contains($name) -and $Arguments.Count -gt 0 -and $table.Contains("$Command $($Arguments[0])")) {
    $name = "$Command $($Arguments[0])"
    $Arguments = @($Arguments | Select-Object -Skip 1)
}

if ($name -eq "help") {
    if ($Arguments.Count -eq 0) { Show-Help; exit 0 }
    $topic = $Arguments -join " "
    if (-not $table.Contains($topic)) { Write-Host "Unknown command '$topic'."; Show-Help; exit 2 }
    $script = Join-Path $commands $table[$topic][0]
    if ($script.EndsWith(".mjs")) {
        foreach ($line in Get-Content -LiteralPath $script) {
            if ($line -notlike "//*") { break }
            Write-Host $line.TrimStart("/ ")
        }
        exit 0
    }
    Get-Help $script | Out-String | Write-Host
    exit 0
}

if (-not $table.Contains($name)) {
    Write-Host "Unknown command '$Command'."
    Show-Help
    exit 2
}

# -Verbose reaches every child the command starts, including the gates verify runs.
if ($Arguments -contains "-Verbose") { $env:PROTO_VERBOSE = "1" }

$script = Join-Path $commands $table[$name][0]
if ($script.EndsWith(".mjs")) {
    & node $script @Arguments
}
else {
    # A child process parses the arguments the way they were typed, named parameters and switches included.
    & pwsh -NoProfile -File $script @Arguments
}
exit $LASTEXITCODE
