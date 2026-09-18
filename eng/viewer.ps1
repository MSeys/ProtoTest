[CmdletBinding()]
param(
    [ValidateSet("dev", "preview")]
    [string]$Mode = "preview",
    [int]$Port = 4174,
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"
$viewer = Join-Path $PSScriptRoot "..\viewer"
$log = Join-Path $env:TEMP "prototest-viewer.log"
$errorLog = Join-Path $env:TEMP "prototest-viewer.err.log"

if ($Mode -eq "preview" -and -not $NoBuild -and -not (Test-Path (Join-Path $viewer "dist"))) {
    Write-Host "No dist yet; building the viewer (this is the only step that takes a while)..."
    & npm --prefix $viewer run build
    if ($LASTEXITCODE -ne 0) { throw "The viewer build failed." }
}

# Start-Process needs the real shim: `npm` alone is not a Win32 executable.
$npm = (Get-Command npm.cmd -ErrorAction SilentlyContinue).Source
if (-not $npm) { $npm = (Get-Command npm).Source }

if ($Mode -eq "dev") {
    $arguments = @("run", "dev", "--", "--port", $Port, "--strictPort")
}
else {
    $arguments = @("run", "preview", "--", "--port", $Port, "--strictPort")
}

# Fire and return: redirect both handles so the shell holds nothing, and never poll here.
# --strictPort makes a busy port exit immediately with the reason in the error log.
Start-Process -FilePath $npm -ArgumentList $arguments -WorkingDirectory $viewer `
    -RedirectStandardOutput $log -RedirectStandardError $errorLog -WindowStyle Hidden | Out-Null

Write-Host "Viewer ($Mode) starting on http://localhost:$Port"
Write-Host "  log: $log"
Write-Host "  error: $errorLog"
