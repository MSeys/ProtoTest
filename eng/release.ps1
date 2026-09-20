[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$DryRun,
    [string]$Source = "https://api.nuget.org/v3/index.json",
    [string]$ApiKey = $env:NUGET_API_KEY
)

$ErrorActionPreference = "Stop"
$repository = Split-Path -Parent $PSScriptRoot
$packagesPath = Join-Path $repository "artifacts/packages"

Add-Type -AssemblyName System.IO.Compression.FileSystem

function Get-PackageMetadata {
    param([System.IO.FileInfo]$File)

    $archive = [System.IO.Compression.ZipFile]::OpenRead($File.FullName)
    try {
        $nuspecEntry = $archive.Entries |
            Where-Object { $_.FullName -notmatch '/' -and $_.FullName.EndsWith('.nuspec', [StringComparison]::OrdinalIgnoreCase) } |
            Select-Object -First 1
        if (-not $nuspecEntry) {
            throw "'$($File.Name)' contains no nuspec."
        }

        $reader = [System.IO.StreamReader]::new($nuspecEntry.Open())
        try {
            $nuspec = [xml]$reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }

        $metadata = $nuspec.package.metadata
        if (-not $metadata.id -or -not $metadata.version) {
            throw "'$($File.Name)' has a nuspec without an id or version."
        }

        $dependencies = @($nuspec.SelectNodes("//*[local-name()='dependency']") |
            ForEach-Object { $_.GetAttribute('id') } |
            Where-Object { $_ -like 'ProtoTest.*' } |
            Sort-Object -Unique)

        $hasAssemblies = @($archive.Entries | Where-Object { $_.FullName -match '^lib/.+\.dll$' }).Count -gt 0

        return [pscustomobject]@{
            Id = [string]$metadata.id
            Version = [string]$metadata.version
            Dependencies = $dependencies
            HasAssemblies = $hasAssemblies
        }
    }
    finally {
        $archive.Dispose()
    }
}

if (-not (Test-Path -LiteralPath $packagesPath)) {
    throw "The packages directory '$packagesPath' does not exist. Run eng/pack.ps1 -Configuration $Configuration first."
}

$packageFiles = @(Get-ChildItem -LiteralPath $packagesPath -File |
    Where-Object { $_.Extension -eq '.nupkg' } |
    Sort-Object Name)
if ($packageFiles.Count -eq 0) {
    throw "No .nupkg files found in '$packagesPath'. Run eng/pack.ps1 -Configuration $Configuration first."
}

$packages = @{}
foreach ($file in $packageFiles) {
    $metadata = Get-PackageMetadata $file
    if ($packages.ContainsKey($metadata.Id)) {
        throw "Duplicate package id '$($metadata.Id)' in '$packagesPath'; remove the stale package before releasing."
    }

    $expectedName = "$($metadata.Id).$($metadata.Version).nupkg"
    if ($file.Name -ne $expectedName) {
        throw "'$($file.Name)' does not match the nuspec id and version ('$expectedName')."
    }

    $packages[$metadata.Id] = [pscustomobject]@{
        Id = $metadata.Id
        Version = $metadata.Version
        PackagePath = $file.FullName
        SymbolPath = Join-Path $file.DirectoryName ($file.BaseName + '.snupkg')
        Dependencies = $metadata.Dependencies
        SymbolsExpected = $metadata.HasAssemblies
    }
}

foreach ($package in $packages.Values) {
    foreach ($dependency in $package.Dependencies) {
        if (-not $packages.ContainsKey($dependency)) {
            throw "Package '$($package.Id)' depends on '$dependency', which is not in '$packagesPath'."
        }
    }
    if ($package.SymbolsExpected -and -not (Test-Path -LiteralPath $package.SymbolPath)) {
        throw "The symbol package for '$($package.Id)' is missing: '$($package.SymbolPath)'."
    }
}

$versions = @($packages.Values.Version | Sort-Object -Unique)
if ($versions.Count -ne 1) {
    throw "The release folder contains multiple package versions: $($versions -join ', ')."
}
$releaseVersion = $versions[0]
if ($env:GITHUB_REF_TYPE -eq 'tag') {
    $expectedTag = "v$releaseVersion"
    if ($env:GITHUB_REF_NAME -ne $expectedTag) {
        throw "Release tag '$($env:GITHUB_REF_NAME)' does not match package version '$releaseVersion'. Expected '$expectedTag'."
    }
}

$remaining = @{}
foreach ($package in $packages.Values) {
    $remaining[$package.Id] = @($package.Dependencies)
}

$ordered = New-Object System.Collections.Generic.List[string]
$placed = @{}
while ($remaining.Count -gt 0) {
    $ready = @($remaining.Keys | Where-Object {
            $id = $_
            -not (@($remaining[$id]) | Where-Object { -not $placed.ContainsKey($_) })
        } | Sort-Object)

    if ($ready.Count -eq 0) {
        throw "The ProtoTest.* dependency graph has a cycle involving: $(@($remaining.Keys | Sort-Object) -join ', ')."
    }

    foreach ($id in $ready) {
        $ordered.Add($id) | Out-Null
        $placed[$id] = $true
        $remaining.Remove($id)
    }
}

$keyDisplay = if ($ApiKey) { '***' } else { '<NUGET_API_KEY>' }

Write-Host "Release plan ($Configuration): $($ordered.Count) package(s), dependencies first."
$index = 1
foreach ($id in $ordered) {
    $package = $packages[$id]
    Write-Host ("{0,2}. {1} {2}" -f $index, $package.Id, $package.Version)
    Write-Host "    package: $($package.PackagePath)"
    if ($package.SymbolsExpected) {
        Write-Host "    symbols: $($package.SymbolPath)"
    }
    else {
        Write-Host "    symbols: none expected (package contains no assemblies)"
    }
    $index++
}

if ($DryRun) {
    Write-Host ""
    Write-Host "Dry run: nothing will be pushed. Commands:"
    foreach ($id in $ordered) {
        $package = $packages[$id]
        Write-Host "dotnet nuget push `"$($package.PackagePath)`" --api-key $keyDisplay --source `"$Source`" --no-symbols"
        if ($package.SymbolsExpected) {
            Write-Host "dotnet nuget push `"$($package.SymbolPath)`" --api-key $keyDisplay --source `"$Source`""
        }
    }
    return
}

if (-not $ApiKey) {
    throw "No NuGet API key. Pass -ApiKey or set the NUGET_API_KEY environment variable."
}

foreach ($id in $ordered) {
    $package = $packages[$id]
    $paths = @($package.PackagePath)
    if ($package.SymbolsExpected) {
        $paths += $package.SymbolPath
    }

    foreach ($path in $paths) {
        Write-Host "Pushing $path"
        # The CLI pushes an adjacent .snupkg on its own, and symbols are pushed explicitly below, so the
        # The package push stays symbols-free because symbols are pushed explicitly below. Deliberately
        # do not use --skip-duplicate: dotnet may still upload the adjacent/newly built symbols after a
        # duplicate package is skipped. NuGet then compares those PDBs with the already immutable DLL and
        # rejects them when the version was rebuilt from another commit. A reused version must fail here.
        $arguments = @('nuget', 'push', $path, '--api-key', $ApiKey, '--source', $Source)
        if (-not $path.EndsWith('.snupkg', [StringComparison]::OrdinalIgnoreCase)) {
            $arguments += '--no-symbols'
        }

        & dotnet @arguments
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet nuget push '$path' failed with exit code $LASTEXITCODE."
        }
    }
}

Write-Host "Published $($ordered.Count) package(s) to $Source."
