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

# One home for the shared test helpers (audit TST-2). A local definition of a helper that lives in
# tests/ProtoTest.TestSupport fails here with the file and line, so the copies cannot drift back.
# The rule is deliberately narrow: only the known helper names, not a general duplicate-code scan.
$supportRoot = [IO.Path]::GetFullPath((Join-Path $repository "tests/ProtoTest.TestSupport"))
$duplicationRules = @(
    @{ Name = "FreePort"; Pattern = '\bstatic\b[^\r\n;{}=]*\bFreePort\s*\(' },
    @{ Name = "ServeOnceAsync"; Pattern = '\bstatic\b[^\r\n;{}=]*\bServeOnceAsync\s*\(' },
    @{ Name = "TemporaryTrace"; Pattern = '\bclass\s+TemporaryTrace\b' },
    @{ Name = "SingleConnectionListener"; Pattern = '\bclass\s+SingleConnectionListener\b' }
)

$duplicates = New-Object System.Collections.Generic.List[string]
$testFiles = Get-ChildItem -LiteralPath (Join-Path $repository "tests") -Recurse -File -Filter *.cs |
    Where-Object {
        -not $_.FullName.StartsWith($supportRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -and
        $_.FullName -notmatch '[\\/](bin|obj)[\\/]'
    }

foreach ($file in $testFiles) {
    foreach ($rule in $duplicationRules) {
        foreach ($match in Select-String -LiteralPath $file.FullName -Pattern $rule.Pattern) {
            $relative = $file.FullName.Substring($repository.Length + 1).Replace('\', '/')
            $duplicates.Add(("{0}:{1}: a local {2} belongs in tests/ProtoTest.TestSupport" -f $relative, $match.LineNumber, $rule.Name))
        }
    }
}

if ($duplicates.Count -gt 0) {
    Write-Host "Test-support duplication found (audit TST-2):"
    foreach ($duplicate in $duplicates) { Write-Host "  $duplicate" }
    throw "use the shared helpers in tests/ProtoTest.TestSupport instead of copying them."
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
