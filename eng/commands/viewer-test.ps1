<#
.SYNOPSIS
Runs the viewer's gates: install, unit tests and the production build.

.DESCRIPTION
The same three commands CI runs in its docs-and-viewer job. verify runs this when a stage touched viewer/.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
Import-Module (Join-Path $PSScriptRoot "../lib/Proto.Eng.psm1") -Force
$repository = Get-ProtoRepository

Push-Location (Join-Path $repository "viewer")
try {
    Invoke-ProtoNative -Name "viewer/install" -FilePath "npm" -ArgumentList @("ci") -Failure "Installing the viewer dependencies failed." | Out-Null
    Invoke-ProtoNative -Name "viewer/test" -FilePath "npm" -ArgumentList @("test") -Failure "The viewer tests failed." | Out-Null
    Invoke-ProtoNative -Name "viewer/build" -FilePath "npm" -ArgumentList @("run", "build") -Failure "Building the viewer failed." | Out-Null
}
finally {
    Pop-Location
}

Write-Host "Viewer gates passed."
