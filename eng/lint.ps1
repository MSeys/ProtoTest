[CmdletBinding()]
param(
    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"

$repository = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repository "ProtoTest.slnx"

if (-not $NoRestore) {
    & dotnet restore $solution
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet restore failed with exit code $LASTEXITCODE."
    }
}

# The build enforces analyzers and the repository .editorconfig with warnings as errors; the format
# check additionally proves no file needs rewriting. Both run on every verify so a style regression
# fails the same run that would have introduced it.
& dotnet format $solution --verify-no-changes --no-restore
if ($LASTEXITCODE -ne 0) {
    throw "dotnet format found files that need formatting; run 'dotnet format ProtoTest.slnx' and commit the result."
}
