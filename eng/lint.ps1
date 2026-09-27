[CmdletBinding()]
param(
    [switch]$NoRestore
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

# One home for the shared test helpers. A local definition of a helper whose name
# contains one of the known roots (FreePort, ServeOnceAsync, TemporaryTrace, SingleConnectionListener)
# fails here with the file and line, so GetFreePort or LazyTemporaryTrace cannot drift back either.
# The rule is deliberately a deny list of the known copies, not a shape scan: a helper renamed to an
# unrelated name (say AcquirePort) is not detected, and that limit is stated in the records checkout's
# facts/gotchas.md.
$supportRoot = [IO.Path]::GetFullPath((Join-Path $repository "tests/ProtoTest.TestSupport"))
$duplicationRules = @(
    @{ Name = "FreePort"; Pattern = '\bstatic\b[^\r\n;{}=]*\b(\w*FreePort\w*)\s*\(' },
    @{ Name = "ServeOnceAsync"; Pattern = '\bstatic\b[^\r\n;{}=]*\b(\w*ServeOnceAsync\w*)\s*\(' },
    @{ Name = "TemporaryTrace"; Pattern = '\bclass\s+(\w*TemporaryTrace\w*)\b' },
    @{ Name = "SingleConnectionListener"; Pattern = '\bclass\s+(\w*SingleConnectionListener\w*)\b' }
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
            $helper = if ($match.Matches.Count -gt 0 -and $match.Matches[0].Groups.Count -gt 1) {
                $match.Matches[0].Groups[1].Value
            }
            else {
                $rule.Name
            }

            $relative = $file.FullName.Substring($repository.Length + 1).Replace('\', '/')
            $duplicates.Add(("{0}:{1}: a local {2} belongs in tests/ProtoTest.TestSupport" -f $relative, $match.LineNumber, $helper))
        }
    }
}

if ($duplicates.Count -gt 0) {
    Write-Host "Test-support duplication found:"
    foreach ($duplicate in $duplicates) { Write-Host "  $duplicate" }
    throw "use the shared helpers in tests/ProtoTest.TestSupport instead of copying them."
}

# An InternalsVisibleTo grant is a test-only edge: no integration may reach into another package's
# internals, so every target must be a *.Tests assembly. When an integration needs a type it cannot see,
# the owning package publishes the contract or moves the code. The scan covers the attribute form in
# source (plain, Attribute-suffixed and verbatim strings) and the csproj item and AssemblyAttribute
# forms, and skips build output, tooling and the gitignored assets/ scratch so a fixture or worktree
# copy cannot trip it. The in-repo sample's own assemblies are the one recorded exception, and only
# under samples/.
$ignoredFriendEdgePaths = '[\\/](bin|obj|node_modules|\.git|artifacts|assets[\\/]internal)[\\/]'
$friendEdgePatterns = @(
    @{ Form = "assembly attribute"; Pattern = 'InternalsVisibleTo(?:Attribute)?\s*\(\s*@?"([^"]+)"' },
    @{ Form = "csproj item"; Pattern = 'InternalsVisibleTo[\s\S]{0,300}?Include\s*=\s*"([^"]+)"' },
    @{ Form = "assembly attribute item"; Pattern = 'InternalsVisibleToAttribute[\s\S]{0,300}?<_Parameter1>\s*([^<,]+?)\s*</_Parameter1>' }
)
# Runtime-generated proxy assemblies never ship and cannot be named a *.Tests project; none exist today.
$allowedFriendEdgeNames = @('^DynamicProxyGenProxies')
# The in-repo sample's own assemblies are neither shipped nor integrations: the sample app, its suite
# support and the demo suite grant each other internals like one product would. They are allowed only
# under samples/ and stay listed until the sample models the public-contract pattern; nothing under
# src/ may ever join them.
$allowedSampleFriendEdgeNames = @('^ProtoTest\.SampleApp$', '^Northstar\.ProtoTest$', '^ProtoTest\.Demo$')
$friendEdgeViolations = New-Object System.Collections.Generic.List[string]
$friendEdgeFiles = Get-ChildItem -LiteralPath $repository -Recurse -File -Include *.cs,*.csproj |
    Where-Object { $_.FullName -notmatch $ignoredFriendEdgePaths }

foreach ($file in $friendEdgeFiles) {
    $text = Get-Content -Raw -LiteralPath $file.FullName
    foreach ($pattern in $friendEdgePatterns) {
        foreach ($match in [regex]::Matches($text, $pattern.Pattern)) {
            $target = $match.Groups[1].Value.Trim()
            if ($target.EndsWith(".Tests", [StringComparison]::OrdinalIgnoreCase)) {
                continue
            }

            if ($allowedFriendEdgeNames.Count -gt 0 -and $target -match ($allowedFriendEdgeNames -join '|')) {
                continue
            }

            $relative = $file.FullName.Substring($repository.Length + 1).Replace('\', '/')
            if ($relative.StartsWith("samples/", [StringComparison]::OrdinalIgnoreCase) -and
                $target -match ($allowedSampleFriendEdgeNames -join '|')) {
                continue
            }

            $line = ($text.Substring(0, $match.Index) -split "`n").Count
            $friendEdgeViolations.Add(("{0}:{1}: InternalsVisibleTo targets '{2}'; only *.Tests assemblies may receive internals" -f $relative, $line, $target))
        }
    }
}

if ($friendEdgeViolations.Count -gt 0) {
    Write-Host "Integration packages must not receive internals through InternalsVisibleTo:"
    foreach ($violation in $friendEdgeViolations) { Write-Host "  $violation" }
    throw "publish the contract the integration needs, or move the code, instead of granting it internals."
}

# The build enforces analyzers and the repository .editorconfig with warnings as errors; the format
# check additionally proves no file needs rewriting. Both run on every verify so a style regression
# fails the same run that would have introduced it. The check is not scoped on purpose:
# `dotnet format --include` silently reports nothing for code-style/analyzer diagnostics, so a scoped
# check was a false green (measured twice).
$formatArguments = @($solution, "--verify-no-changes", "--no-restore")

& dotnet format @formatArguments
if ($LASTEXITCODE -ne 0) {
    throw "dotnet format found files that need formatting; run 'dotnet format ProtoTest.slnx' and commit the result."
}
