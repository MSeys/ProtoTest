param(
    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"
$repository = Split-Path -Parent $PSScriptRoot

Push-Location $repository
try {
    if (-not $NoRestore) {
        dotnet tool restore
        if ($LASTEXITCODE -ne 0) { throw "Restoring the DocFX tool failed." }
    }

    dotnet docfx "docs/api-reference/docfx.json"
    if ($LASTEXITCODE -ne 0) { throw "Building the API reference failed." }
}
finally {
    Pop-Location
}
