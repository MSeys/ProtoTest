<#
.SYNOPSIS
Builds the Docusaurus site and the API reference into docs/build, ready to upload.
#>
[CmdletBinding()]
param(
    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"
Import-Module (Join-Path $PSScriptRoot "../lib/Proto.Eng.psm1") -Force
$repository = Get-ProtoRepository
$docs = Join-Path $repository "docs"
$docsBuild = [System.IO.Path]::GetFullPath((Join-Path $docs "build"))
$apiBuild = [System.IO.Path]::GetFullPath((Join-Path $repository "artifacts/api-reference"))
$apiDestination = [System.IO.Path]::GetFullPath((Join-Path $docsBuild "api"))

Push-Location $docs
try {
    Invoke-ProtoNative -Name "docs-site/build" -FilePath "npm" -ArgumentList @("run", "build") -Failure "Building the Docusaurus site failed." | Out-Null
}
finally {
    Pop-Location
}

& (Join-Path $PSScriptRoot "docs-api.ps1") -NoRestore:$NoRestore
if (-not (Test-Path -LiteralPath $apiBuild)) {
    throw "DocFX did not write the expected site: $apiBuild"
}

# The destination is generated output. Validate it before replacing it so this script can never
# recursively remove anything outside the freshly built Docusaurus folder.
$docsBuildPrefix = $docsBuild.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
if (-not $apiDestination.StartsWith($docsBuildPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Unexpected API reference destination: $apiDestination"
}

if (Test-Path -LiteralPath $apiDestination) {
    Remove-Item -LiteralPath $apiDestination -Recurse -Force
}
New-Item -ItemType Directory -Path $apiDestination | Out-Null
Copy-Item -Path (Join-Path $apiBuild "*") -Destination $apiDestination -Recurse -Force

$fileCount = (Get-ChildItem -LiteralPath $docsBuild -Recurse -File).Count
Write-Host "Combined docs site ready: $docsBuild ($fileCount files)"
Write-Host "Upload the contents of this folder to Cloudflare Pages."
