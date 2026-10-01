<#
.SYNOPSIS
Builds the DocFX API reference into artifacts/api-reference.
#>
[CmdletBinding()]
param(
    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"
Import-Module (Join-Path $PSScriptRoot "../lib/Proto.Eng.psm1") -Force
$repository = Get-ProtoRepository

Push-Location $repository
try {
    if (-not $NoRestore) {
        Invoke-ProtoNative -Name "docs-api/restore" -FilePath "dotnet" -ArgumentList @("tool", "restore") -Failure "Restoring the DocFX tool failed." | Out-Null
    }
    Invoke-ProtoNative -Name "docs-api/docfx" -FilePath "dotnet" -ArgumentList @("docfx", "docs/api-reference/docfx.json") -Failure "Building the API reference failed." | Out-Null
}
finally {
    Pop-Location
}
