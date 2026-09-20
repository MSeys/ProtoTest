[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$OutputPath = "artifacts/packages",
    [switch]$NoBuild,
    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"

# Windows PowerShell 5 cannot load the modern System.Reflection.Metadata assembly used by the
# PDB/DLL identity checks below. Re-enter through PowerShell 7 when someone starts this script from
# the legacy `powershell.exe`; CI already runs it with pwsh.
if ($PSVersionTable.PSEdition -ne "Core") {
    $pwsh = Get-Command pwsh -ErrorAction SilentlyContinue
    if (-not $pwsh) {
        throw "eng/pack.ps1 requires PowerShell 7. Install it or run this script with pwsh."
    }

    $forwarded = @("-NoProfile", "-File", $PSCommandPath, "-Configuration", $Configuration, "-OutputPath", $OutputPath)
    if ($NoBuild) { $forwarded += "-NoBuild" }
    if ($NoRestore) { $forwarded += "-NoRestore" }
    & $pwsh.Source @forwarded
    if ($LASTEXITCODE -ne 0) {
        throw "PowerShell 7 package validation failed with exit code $LASTEXITCODE."
    }
    return
}

$repository = Split-Path -Parent $PSScriptRoot
$packages = @(
    "src/ProtoTest.Core/ProtoTest.Core.csproj",
    "src/ProtoTest.Testcontainers/ProtoTest.Testcontainers.csproj",
    "src/ProtoTest.Http/ProtoTest.Http.csproj",
    "src/ProtoTest.Json/ProtoTest.Json.csproj",
    "src/ProtoTest.Data/ProtoTest.Data.csproj",
    "src/ProtoTest.NUnit/ProtoTest.NUnit.csproj",
    "src/ProtoTest.MSTest/ProtoTest.MSTest.csproj",
    "src/ProtoTest.TUnit/ProtoTest.TUnit.csproj",
    "src/ProtoTest.Xunit/ProtoTest.Xunit.csproj",
    "src/ProtoTest.Xunit3/ProtoTest.Xunit3.csproj",
    "src/ProtoTest.Rest/ProtoTest.Rest.csproj",
    "src/ProtoTest.Sql/ProtoTest.Sql.csproj",
    "src/ProtoTest.Sql.EntityFrameworkCore/ProtoTest.Sql.EntityFrameworkCore.csproj",
    "src/ProtoTest.Sql.Testcontainers/ProtoTest.Sql.Testcontainers.csproj",
    "src/ProtoTest.GraphQL/ProtoTest.GraphQL.csproj",
    "src/ProtoTest.Grpc/ProtoTest.Grpc.csproj",
    "src/ProtoTest.Messaging/ProtoTest.Messaging.csproj",
    "src/ProtoTest.Messaging.RabbitMq/ProtoTest.Messaging.RabbitMq.csproj",
    "src/ProtoTest.Messaging.RabbitMq.Testcontainers/ProtoTest.Messaging.RabbitMq.Testcontainers.csproj",
    "src/ProtoTest.Sheets/ProtoTest.Sheets.csproj",
    "src/ProtoTest.AspNetCore/ProtoTest.AspNetCore.csproj",
    "src/ProtoTest.OpenApi/ProtoTest.OpenApi.csproj",
    "src/ProtoTest.OpenTelemetry/ProtoTest.OpenTelemetry.csproj",
    "src/ProtoTest.Reporting/ProtoTest.Reporting.csproj",
    "src/ProtoTest.Web/ProtoTest.Web.csproj",
    "src/ProtoTest.Web.Playwright/ProtoTest.Web.Playwright.csproj",
    "src/ProtoTest.Web.Selenium/ProtoTest.Web.Selenium.csproj",
    "src/ProtoTest.Templates/ProtoTest.Templates.csproj"
)

$packable = Get-ChildItem -Path (Join-Path $repository "src/*/*.csproj") |
    Where-Object { Select-String -Path $_.FullName -Pattern "<IsPackable>true</IsPackable>" -Quiet } |
    ForEach-Object { $_.FullName.Substring($repository.Length + 1).Replace('\', '/') }

$missing = @($packable | Where-Object { $packages -notcontains $_ })
if ($missing.Count -gt 0) {
    throw "Packable projects missing from eng/pack.ps1: $($missing -join ', ')."
}

$extra = @($packages | Where-Object { $packable -notcontains $_ })
if ($extra.Count -gt 0) {
    throw "eng/pack.ps1 lists projects that are not packable: $($extra -join ', ')."
}

$unknown = @($packages | Where-Object { -not (Test-Path -LiteralPath (Join-Path $repository $_)) })
if ($unknown.Count -gt 0) {
    throw "eng/pack.ps1 lists projects that do not exist: $($unknown -join ', ')."
}

$output = if ([IO.Path]::IsPathRooted($OutputPath)) { $OutputPath } else { Join-Path $repository $OutputPath }
$packArguments = @("--configuration", $Configuration, "--output", $output)
if ($NoBuild) { $packArguments += "--no-build" }
if ($NoRestore) { $packArguments += "--no-restore" }

# Pack into a clean folder: dotnet pack skips packages whose inputs it considers unchanged, and the
# verification below reads every package this run produced.
New-Item -ItemType Directory -Path $output -Force | Out-Null
Remove-Item -Path (Join-Path $output "*.nupkg"), (Join-Path $output "*.snupkg") -Force -ErrorAction SilentlyContinue

$packStarted = Get-Date

foreach ($project in $packages) {
    & dotnet pack (Join-Path $repository $project) @packArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Packing '$project' failed with exit code $LASTEXITCODE."
    }
}

# Every package has to agree with the rest of the family: same version, a README, and ProtoTest
# dependencies that point at a package in this set, at exactly this version.
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.Reflection.Metadata

function Copy-ZipEntryToMemoryStream {
    param([System.IO.Compression.ZipArchiveEntry]$Entry)

    $memory = [IO.MemoryStream]::new()
    $source = $Entry.Open()
    try {
        $source.CopyTo($memory)
        $memory.Position = 0
        return $memory
    }
    catch {
        $memory.Dispose()
        throw
    }
    finally {
        $source.Dispose()
    }
}

function Get-DllPdbIdentity {
    param([System.IO.Compression.ZipArchiveEntry]$Entry)

    $memory = Copy-ZipEntryToMemoryStream $Entry
    $reader = [Reflection.PortableExecutable.PEReader]::new($memory)
    try {
        $debugEntries = @($reader.ReadDebugDirectory() | Where-Object Type -eq CodeView)
        if ($debugEntries.Count -ne 1) {
            throw "'$($Entry.FullName)' contains $($debugEntries.Count) CodeView debug entries; expected one."
        }
        $codeView = $reader.ReadCodeViewDebugDirectoryData($debugEntries[0])
        return [pscustomobject]@{ Guid = $codeView.Guid; Stamp = $debugEntries[0].Stamp }
    }
    finally {
        $reader.Dispose()
        $memory.Dispose()
    }
}

function Get-PortablePdbIdentity {
    param([System.IO.Compression.ZipArchiveEntry]$Entry)

    $memory = Copy-ZipEntryToMemoryStream $Entry
    $provider = [Reflection.Metadata.MetadataReaderProvider]::FromPortablePdbStream($memory)
    try {
        [byte[]]$id = $provider.GetMetadataReader().DebugMetadataHeader.Id
        if ($id.Length -ne 20) {
            throw "'$($Entry.FullName)' has an invalid portable PDB identifier."
        }
        return [pscustomobject]@{
            Guid = [Guid]::new([byte[]]$id[0..15])
            Stamp = [BitConverter]::ToUInt32($id, 16)
        }
    }
    finally {
        $provider.Dispose()
        $memory.Dispose()
    }
}

$expectedVersion = ([xml](Get-Content -LiteralPath (Join-Path $repository "Directory.Build.props") -Raw)).Project.PropertyGroup.Version | ForEach-Object { $_.Trim() } | Where-Object { $_ } | Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($expectedVersion)) {
    throw "Directory.Build.props does not declare a <Version>."
}
$produced = @(Get-ChildItem -LiteralPath $output -Filter "*.nupkg" -File |
    Where-Object { $_.LastWriteTime -ge $packStarted })
if ($produced.Count -ne $packages.Count) {
    throw "Expected $($packages.Count) packages in '$output', found $($produced.Count) written by this run."
}

$packagesById = @{}
$dependencies = @()
$verifiedSymbols = 0

foreach ($file in $produced) {
    $archive = [IO.Compression.ZipFile]::OpenRead($file.FullName)
    try {
        $entries = @($archive.Entries | Where-Object { $_.FullName -like '*.nuspec' })
        if ($entries.Count -ne 1) {
            throw "'$($file.Name)' contains $($entries.Count) nuspec files, expected one."
        }
        $stream = $entries[0].Open()
        $reader = New-Object System.IO.StreamReader -ArgumentList $stream
        try {
            [xml]$nuspec = $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }

        $assemblyEntries = @($archive.Entries | Where-Object { $_.FullName -match '^lib/.+\.dll$' })
        $assemblyPaths = @($assemblyEntries | ForEach-Object { $_.FullName.ToLowerInvariant() })
        if ($assemblyPaths.Count -gt 0) {
            $symbolPath = Join-Path $file.DirectoryName ($file.BaseName + '.snupkg')
            if (-not (Test-Path -LiteralPath $symbolPath)) {
                throw "The symbol package for '$($file.Name)' is missing: '$symbolPath'."
            }

            $symbolArchive = [IO.Compression.ZipFile]::OpenRead($symbolPath)
            try {
                $pdbEntries = @($symbolArchive.Entries | Where-Object { $_.FullName -match '^lib/.+\.pdb$' })
                $pdbPaths = @($pdbEntries | ForEach-Object { $_.FullName.ToLowerInvariant() })
                foreach ($pdbEntry in $pdbEntries) {
                    $pdbPath = $pdbEntry.FullName.ToLowerInvariant()
                    $expectedDll = [IO.Path]::ChangeExtension($pdbPath, '.dll')
                    if ($assemblyPaths -notcontains $expectedDll) {
                        throw "'$([IO.Path]::GetFileName($symbolPath))' contains '$pdbPath', but '$($file.Name)' has no matching '$expectedDll'."
                    }

                    $assemblyEntry = $assemblyEntries | Where-Object { $_.FullName.Equals($expectedDll, [StringComparison]::OrdinalIgnoreCase) } | Select-Object -First 1
                    $dllIdentity = Get-DllPdbIdentity $assemblyEntry
                    $pdbIdentity = Get-PortablePdbIdentity $pdbEntry
                    if ($dllIdentity.Guid -ne $pdbIdentity.Guid -or $dllIdentity.Stamp -ne $pdbIdentity.Stamp) {
                        throw "'$pdbPath' does not belong to '$expectedDll'; their portable PDB identifiers differ."
                    }
                    $verifiedSymbols++
                }
                foreach ($assemblyPath in $assemblyPaths) {
                    $expectedPdb = [IO.Path]::ChangeExtension($assemblyPath, '.pdb')
                    if ($pdbPaths -notcontains $expectedPdb) {
                        throw "'$($file.Name)' contains '$assemblyPath', but its symbol package has no matching '$expectedPdb'."
                    }
                }
            }
            finally {
                $symbolArchive.Dispose()
            }
        }
    }
    finally {
        $archive.Dispose()
    }

    $metadata = $nuspec.package.metadata
    $id = $metadata.id
    if ($metadata.version -ne $expectedVersion) {
        throw "'$($file.Name)' is version '$($metadata.version)'; this release packs '$expectedVersion'."
    }
    if ([string]::IsNullOrWhiteSpace($metadata.readme)) {
        throw "'$($file.Name)' has no README entry."
    }
    if ($packagesById.ContainsKey($id)) {
        throw "'$($file.Name)' repeats package id '$id'."
    }
    $packagesById[$id] = $metadata.version

    $groups = @($metadata.dependencies.group) + @(@{ dependency = $metadata.dependencies.dependency })
    foreach ($group in $groups) {
        foreach ($dependency in @($group.dependency)) {
            if ($null -ne $dependency -and $dependency.id -like 'ProtoTest.*') {
                $dependencies += [pscustomobject] @{
                    Package = $id
                    Id = $dependency.id
                    Version = ([string]$dependency.version).Trim()
                }
            }
        }
    }
}

$verified = 0
foreach ($dependency in $dependencies) {
    if (-not $packagesById.ContainsKey($dependency.Id)) {
        throw "'$($dependency.Package)' depends on '$($dependency.Id)', which is not one of the packed packages."
    }
    $declared = (($dependency.Version -replace '^[\[\(]', '') -replace '[\]\)]$', '').Split(',')[0].Trim()
    if ($declared -ne $packagesById[$dependency.Id]) {
        throw "'$($dependency.Package)' depends on '$($dependency.Id)' $($dependency.Version); packed version is $($packagesById[$dependency.Id])."
    }
    $verified++
}

Write-Host "Packed $($packagesById.Count) packages and verified $verified ProtoTest dependency references and $verifiedSymbols PDB/DLL pairs at version $expectedVersion."
