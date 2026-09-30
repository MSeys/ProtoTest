# The viewer's own gates: install, unit tests and the production build, the same three
# commands CI runs in its docs-and-viewer job. verify.ps1 runs this when a stage touched viewer/.
param()

$ErrorActionPreference = "Stop"
$repository = Split-Path -Parent $PSScriptRoot
$viewer = Join-Path $repository "viewer"

Push-Location $viewer
try {
    & npm ci
    if ($LASTEXITCODE -ne 0) { throw "Installing the viewer dependencies failed." }
    & npm test
    if ($LASTEXITCODE -ne 0) { throw "The viewer tests failed." }
    & npm run build
    if ($LASTEXITCODE -ne 0) { throw "Building the viewer failed." }
}
finally {
    Pop-Location
}

Write-Host "Viewer gates passed."
