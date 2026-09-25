[CmdletBinding()]
param(
    [switch]$NoRestore,

    # Optional semicolon-separated files or directories (relative to the repository or absolute) to
    # format-check instead of the whole solution; verify.ps1 passes the projects a stage touched.
    [string]$Include = ""
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
$formatArguments = @($solution, "--verify-no-changes", "--no-restore")
if (-not [string]::IsNullOrWhiteSpace($Include)) {
    $resolvedInclude = @($Include.Split(';', [StringSplitOptions]::RemoveEmptyEntries) | ForEach-Object {
            $path = $_.Trim()
            if ([IO.Path]::IsPathRooted($path)) { $path } else { Join-Path $repository $path }
        })
    $formatArguments += @("--include") + $resolvedInclude
}

& dotnet format @formatArguments
if ($LASTEXITCODE -ne 0) {
    throw "dotnet format found files that need formatting; run 'dotnet format ProtoTest.slnx' and commit the result."
}
