[CmdletBinding()]
param()

# Fixtures for the gate scripts themselves. Each fixture runs a real gate - verify.ps1, check-docs.ps1,
# lint.ps1 or release.ps1 - in a throwaway repository (or reads a workflow) and asserts the claim the
# gate makes: a docs-only stage cannot record code-green, a committed code stage without -Full is
# incomplete, a renamed helper is still caught, docs keys are cross-checked without the private fact
# sheets, publishing from a branch fails before any push, and the MTP zero-test guards fail on zero.
# verify.ps1 runs this script as its `scripts` gate when eng/**.ps1 or .github/workflows/** changed,
# and the CI build-test-pack job runs it after test.ps1 so the runner guards have built binaries.
#
# Set PROTOTEST_KEEP_GATE_FIXTURES=1 to keep the throwaway repositories for inspection.

$ErrorActionPreference = "Stop"
if (Test-Path variable:PSNativeCommandUseErrorActionPreference) { $PSNativeCommandUseErrorActionPreference = $false }

$repository = Split-Path -Parent $PSScriptRoot
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ("prototest-gate-fixtures-" + [Guid]::NewGuid().ToString("N").Substring(0, 12))
New-Item -ItemType Directory -Path $fixtureRoot -Force | Out-Null

$failures = New-Object System.Collections.Generic.List[string]

function Assert-Fixture {
    param([bool]$Condition, [string]$Message)

    if (-not $Condition) { throw $Message }
}

function Invoke-Fixture {
    param([string]$Name, [scriptblock]$Body)

    try {
        $result = & $Body
        if ($result -eq "skip") {
            Write-Host "gate-fixture: $Name SKIP"
        }
        else {
            Write-Host "gate-fixture: $Name ok"
        }
    }
    catch {
        $failures.Add("$Name`: $($_.Exception.Message)")
        Write-Host "gate-fixture: $Name FAILED: $($_.Exception.Message)"
    }
}

function New-FixtureRepository {
    param([string]$Name)

    $root = Join-Path $fixtureRoot $Name
    New-Item -ItemType Directory -Path (Join-Path $root "eng") -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "verify.ps1") -Destination (Join-Path $root "eng/verify.ps1")

    # The gate stubs stand in for the real gates: they record that they ran (with their arguments) and
    # succeed, so a fixture asserts which gates verify.ps1 invoked instead of building the solution.
    $gateStub = @'
param([string]$Include = "", [switch]$NoRestore)
$name = [IO.Path]::GetFileNameWithoutExtension($PSCommandPath)
$markers = Join-Path (Split-Path -Parent (Split-Path -Parent $PSCommandPath)) "artifacts/markers"
New-Item -ItemType Directory -Path $markers -Force | Out-Null
Add-Content -LiteralPath (Join-Path $markers "$name.txt") -Value ("Include=$Include;NoRestore=$NoRestore")
exit 0
'@
    foreach ($gate in @("lint", "test", "check-docs", "pack", "test-gates")) {
        Set-Content -LiteralPath (Join-Path $root "eng/$gate.ps1") -Value $gateStub -Encoding utf8
    }

    Set-Content -LiteralPath (Join-Path $root "Directory.Build.props") -Value "<Project><PropertyGroup><Version>9.9.9-fixture</Version></PropertyGroup></Project>" -Encoding utf8
    & git -C $root init --quiet
    & git -C $root config user.email "gate-fixture@prototest.invalid"
    & git -C $root config user.name "Gate Fixture"
    & git -C $root config core.autocrlf false
    & git -C $root add --all | Out-Null
    & git -C $root commit --quiet -m "fixture base"
    if ($LASTEXITCODE -ne 0) { throw "the fixture git commit failed" }

    return $root
}

function Add-FixtureFile {
    param([string]$Root, [string]$RelativePath, [string]$Content)

    $path = Join-Path $Root $RelativePath
    New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force | Out-Null
    Set-Content -LiteralPath $path -Value $Content -Encoding utf8
    return $path
}

function Invoke-FixtureVerify {
    param([string]$Root, [string[]]$Arguments)

    $output = & pwsh -NoProfile -File (Join-Path $Root "eng/verify.ps1") @Arguments 2>&1
    return [pscustomobject]@{ Text = ($output -join [Environment]::NewLine); ExitCode = $LASTEXITCODE }
}

function Get-FixtureRecord {
    param([string]$Root, [string]$Stage)

    $path = Join-Path $Root "artifacts/gates/$Stage.json"
    Assert-Fixture (Test-Path -LiteralPath $path) "the gate record '$path' was not written"
    return Get-Content -Raw -LiteralPath $path | ConvertFrom-Json
}

function Get-FixtureMarker {
    param([string]$Root, [string]$Gate)

    $path = Join-Path $Root "artifacts/markers/$Gate.txt"
    if (-not (Test-Path -LiteralPath $path)) { return "" }
    return (Get-Content -Raw -LiteralPath $path).Replace('\', '/')
}

Write-Host "gate fixtures: $fixtureRoot"
Write-Host ""

try {
    # A stage that genuinely changed no code runs only the docs check and records docs-only, never
    # code-green.
    Invoke-Fixture "verify-docs-only" {
        $root = New-FixtureRepository "verify-docs-only"
        Add-FixtureFile -Root $root -RelativePath "notes.md" -Content "# stage notes" | Out-Null

        $run = Invoke-FixtureVerify -Root $root -Arguments @("-Stage", "t-docs")
        Assert-Fixture ($run.ExitCode -eq 0) "expected exit 0, got $($run.ExitCode): $($run.Text)"
        $record = Get-FixtureRecord -Root $root -Stage "t-docs"
        Assert-Fixture ($record.classification -eq "docs-only") "expected classification docs-only, got '$($record.classification)'"
        Assert-Fixture ([bool]$record.green) "expected green"
        Assert-Fixture (-not [bool]$record.incomplete) "a docs-only stage is not incomplete"
        Assert-Fixture ((Get-FixtureMarker -Root $root -Gate "check-docs") -ne "") "the docs gate did not run"
        Assert-Fixture ((Get-FixtureMarker -Root $root -Gate "lint") -eq "") "lint ran for a docs-only stage"
        Assert-Fixture ((Get-FixtureMarker -Root $root -Gate "test") -eq "") "the tests ran for a docs-only stage"
        Assert-Fixture (@($record.skippedCodeGates).Count -eq 0) "a docs-only stage skipped no applicable code gate"
    }

    # One changed project runs its tests; lint always runs over the solution because a scoped format
    # check is a false green.
    Invoke-Fixture "verify-one-project" {
        $root = New-FixtureRepository "verify-one-project"
        Add-FixtureFile -Root $root -RelativePath "src/Foo/Foo.csproj" -Content "<Project />" | Out-Null
        Add-FixtureFile -Root $root -RelativePath "src/Foo/Foo.cs" -Content "// stage change" | Out-Null

        $run = Invoke-FixtureVerify -Root $root -Arguments @("-Stage", "t-project")
        Assert-Fixture ($run.ExitCode -eq 0) "expected exit 0, got $($run.ExitCode): $($run.Text)"
        $record = Get-FixtureRecord -Root $root -Stage "t-project"
        Assert-Fixture ($record.classification -eq "code") "expected classification code, got '$($record.classification)'"
        Assert-Fixture ([bool]$record.green) "expected green"
        Assert-Fixture ((Get-FixtureMarker -Root $root -Gate "lint") -ne "") "lint did not run for a code stage"
        Assert-Fixture ((Get-FixtureMarker -Root $root -Gate "test") -ne "") "the tests did not run for a code stage"
        Assert-Fixture (@($record.skippedCodeGates).Count -eq 0) "a code stage that ran its gates is not incomplete"
    }

    # A committed code stage on a clean tree can no longer skip the code gates silently: the record is
    # incomplete and non-green until -Full covers it.
    Invoke-Fixture "verify-committed-code-incomplete" {
        $root = New-FixtureRepository "verify-committed-code"
        Add-FixtureFile -Root $root -RelativePath "src/Foo/Foo.csproj" -Content "<Project />" | Out-Null
        Add-FixtureFile -Root $root -RelativePath "src/Foo/Foo.cs" -Content "// stage change" | Out-Null
        & git -C $root add --all | Out-Null
        & git -C $root commit --quiet -m "add Foo"
        if ($LASTEXITCODE -ne 0) { throw "the fixture stage commit failed" }

        $run = Invoke-FixtureVerify -Root $root -Arguments @("-Stage", "t-committed")
        Assert-Fixture ($run.ExitCode -eq 1) "expected exit 1 for an incomplete record, got $($run.ExitCode): $($run.Text)"
        $record = Get-FixtureRecord -Root $root -Stage "t-committed"
        Assert-Fixture (-not [bool]$record.green) "an incomplete record must not be green"
        Assert-Fixture ($record.classification -eq "incomplete") "expected classification incomplete, got '$($record.classification)'"
        Assert-Fixture ($record.scope -eq "head-commit") "expected scope head-commit, got '$($record.scope)'"
        Assert-Fixture (@($record.skippedCodeGates).Count -eq 2) "expected lint and test named as skipped code gates"
        Assert-Fixture ((Get-FixtureMarker -Root $root -Gate "lint") -eq "") "lint must not run for a committed stage without -Full"
        Assert-Fixture ((Get-FixtureMarker -Root $root -Gate "test") -eq "") "the tests must not run for a committed stage without -Full"
    }

    # The opt-out is explicit and named in the record.
    Invoke-Fixture "verify-committed-code-opt-out" {
        $root = New-FixtureRepository "verify-committed-code-opt-out"
        Add-FixtureFile -Root $root -RelativePath "src/Foo/Foo.csproj" -Content "<Project />" | Out-Null
        Add-FixtureFile -Root $root -RelativePath "src/Foo/Foo.cs" -Content "// stage change" | Out-Null
        & git -C $root add --all | Out-Null
        & git -C $root commit --quiet -m "add Foo"
        if ($LASTEXITCODE -ne 0) { throw "the fixture stage commit failed" }

        $run = Invoke-FixtureVerify -Root $root -Arguments @("-Stage", "t-approved", "-AllowSkippedCodeGates")
        Assert-Fixture ($run.ExitCode -eq 0) "expected exit 0 with the opt-out, got $($run.ExitCode): $($run.Text)"
        $record = Get-FixtureRecord -Root $root -Stage "t-approved"
        Assert-Fixture ([bool]$record.green) "the opt-out permits a green record"
        Assert-Fixture ([bool]$record.incomplete) "the record still says the code gates were skipped"
        Assert-Fixture ([bool]$record.allowSkippedCodeGates) "the record must name the opt-out"
        Assert-Fixture (@($record.skippedCodeGates).Count -eq 2) "the record must name the skipped code gates"
    }

    # -Full covers a committed code stage with the CI shape.
    Invoke-Fixture "verify-committed-code-full" {
        $root = New-FixtureRepository "verify-committed-code-full"
        Add-FixtureFile -Root $root -RelativePath "src/Foo/Foo.csproj" -Content "<Project />" | Out-Null
        Add-FixtureFile -Root $root -RelativePath "src/Foo/Foo.cs" -Content "// stage change" | Out-Null
        & git -C $root add --all | Out-Null
        & git -C $root commit --quiet -m "add Foo"
        if ($LASTEXITCODE -ne 0) { throw "the fixture stage commit failed" }

        $run = Invoke-FixtureVerify -Root $root -Arguments @("-Stage", "t-full", "-Full")
        Assert-Fixture ($run.ExitCode -eq 0) "expected exit 0 for -Full, got $($run.ExitCode): $($run.Text)"
        $record = Get-FixtureRecord -Root $root -Stage "t-full"
        Assert-Fixture ($record.classification -eq "code") "expected classification code, got '$($record.classification)'"
        Assert-Fixture ([bool]$record.green) "expected green"
        Assert-Fixture ((Get-FixtureMarker -Root $root -Gate "lint") -ne "") "lint did not run under -Full"
        Assert-Fixture ((Get-FixtureMarker -Root $root -Gate "test") -ne "") "the tests did not run under -Full"
        Assert-Fixture ((Get-FixtureMarker -Root $root -Gate "test-gates") -ne "") "the gate fixtures did not run under -Full"
    }

    # A tooling stage runs the gate fixtures and records tooling, not code.
    Invoke-Fixture "verify-tooling" {
        $root = New-FixtureRepository "verify-tooling"
        Add-FixtureFile -Root $root -RelativePath "eng/helper.ps1" -Content "Write-Host 'helper'" | Out-Null

        $run = Invoke-FixtureVerify -Root $root -Arguments @("-Stage", "t-tooling")
        Assert-Fixture ($run.ExitCode -eq 0) "expected exit 0, got $($run.ExitCode): $($run.Text)"
        $record = Get-FixtureRecord -Root $root -Stage "t-tooling"
        Assert-Fixture ($record.classification -eq "tooling") "expected classification tooling, got '$($record.classification)'"
        Assert-Fixture ([bool]$record.green) "expected green"
        Assert-Fixture ((Get-FixtureMarker -Root $root -Gate "test-gates") -ne "") "the gate fixtures did not run for a tooling stage"
        Assert-Fixture ((Get-FixtureMarker -Root $root -Gate "test") -eq "") "the tests ran for a scripts-only stage"
    }

    # A requested skip of a code gate on a code stage is incomplete too.
    Invoke-Fixture "verify-requested-skip-incomplete" {
        $root = New-FixtureRepository "verify-requested-skip"
        Add-FixtureFile -Root $root -RelativePath "src/Foo/Foo.csproj" -Content "<Project />" | Out-Null
        Add-FixtureFile -Root $root -RelativePath "src/Foo/Foo.cs" -Content "// stage change" | Out-Null

        $run = Invoke-FixtureVerify -Root $root -Arguments @("-Stage", "t-skip", "-SkipLint")
        Assert-Fixture ($run.ExitCode -eq 1) "expected exit 1 for the skipped gate, got $($run.ExitCode): $($run.Text)"
        $record = Get-FixtureRecord -Root $root -Stage "t-skip"
        Assert-Fixture ($record.classification -eq "incomplete") "expected classification incomplete, got '$($record.classification)'"
        Assert-Fixture (@($record.skippedCodeGates | Where-Object { $_.name -eq "lint" -and $_.reason -eq "requested" }).Count -eq 1) "the requested lint skip must be named"
        Assert-Fixture ((Get-FixtureMarker -Root $root -Gate "test") -ne "") "the tests still ran"
    }

    # The lint duplication rule catches an obvious rename of a shared helper.
    Invoke-Fixture "lint-renamed-helpers" {
        $root = New-FixtureRepository "lint-renamed-helpers"
        New-Item -ItemType Directory -Path (Join-Path $root "tests/ProtoTest.TestSupport") -Force | Out-Null
        Add-FixtureFile -Root $root -RelativePath "tests/Fixture.Tests/RenamedHelpers.cs" -Content @'
namespace Fixture.Tests;

internal static class Ports
{
    public static int GetFreePort() => 0;
}

internal sealed class LazyTemporaryTrace
{
}
'@ | Out-Null
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot "lint.ps1") -Destination (Join-Path $root "eng/lint.ps1")

        $output = & pwsh -NoProfile -File (Join-Path $root "eng/lint.ps1") -NoRestore 2>&1
        $text = $output -join [Environment]::NewLine
        Assert-Fixture ($LASTEXITCODE -ne 0) "the renamed helpers must fail the lint gate: $text"
        Assert-Fixture ($text.Contains("GetFreePort")) "the failure must name GetFreePort: $text"
        Assert-Fixture ($text.Contains("LazyTemporaryTrace")) "the failure must name LazyTemporaryTrace: $text"
    }

    # InternalsVisibleTo is a test-only edge: a grant to an integration package fails the lint gate in
    # both the attribute and csproj forms, while a *.Tests target passes the rule.
    Invoke-Fixture "lint-friend-edges" {
        $root = New-FixtureRepository "lint-friend-edges"
        Add-FixtureFile -Root $root -RelativePath "src/Fixture/Properties/AssemblyInfo.cs" -Content @'
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("ProtoTest.Rest")]
[assembly: InternalsVisibleToAttribute("ProtoTest.GraphQL")]
[assembly: InternalsVisibleTo(@"ProtoTest.Grpc")]
'@ | Out-Null
        Add-FixtureFile -Root $root -RelativePath "src/Fixture/Fixture.csproj" -Content @'
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <InternalsVisibleTo Include="ProtoTest.Http" />
    <InternalsVisibleTo Condition="'$(Configuration)' == 'Release'" Include="ProtoTest.Messaging" />
  </ItemGroup>
</Project>
'@ | Out-Null
        Add-FixtureFile -Root $root -RelativePath "tests/Fixture.Tests/Placeholder.cs" -Content "namespace Fixture.Tests; internal static class Placeholder; " | Out-Null
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot "lint.ps1") -Destination (Join-Path $root "eng/lint.ps1")

        $output = & pwsh -NoProfile -File (Join-Path $root "eng/lint.ps1") -NoRestore 2>&1
        $text = $output -join [Environment]::NewLine
        Assert-Fixture ($LASTEXITCODE -ne 0) "an integration friend edge must fail the lint gate: $text"
        Assert-Fixture ($text.Contains("ProtoTest.Rest")) "the failure must name the attribute-form target: $text"
        Assert-Fixture ($text.Contains("ProtoTest.Http")) "the failure must name the csproj-form target: $text"
        Assert-Fixture ($text.Contains("ProtoTest.GraphQL")) "the failure must name the Attribute-suffixed target: $text"
        Assert-Fixture ($text.Contains("ProtoTest.Grpc")) "the failure must name the verbatim-string target: $text"
        Assert-Fixture ($text.Contains("ProtoTest.Messaging")) "the failure must name the conditional csproj target: $text"

        # The same edges aimed at test assemblies pass the rule; an empty solution lets the rest of the
        # gate - the duplication scan and the format check - run and stay green too.
        Add-FixtureFile -Root $root -RelativePath "src/Fixture/Properties/AssemblyInfo.cs" -Content @'
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("ProtoTest.Rest.Tests")]
'@ | Out-Null
        Add-FixtureFile -Root $root -RelativePath "src/Fixture/Fixture.csproj" -Content @'
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <InternalsVisibleTo Include="ProtoTest.Http.Tests" />
  </ItemGroup>
</Project>
'@ | Out-Null
        Set-Content -LiteralPath (Join-Path $root "ProtoTest.slnx") -Value '<Solution></Solution>' -Encoding utf8

        $output = & pwsh -NoProfile -File (Join-Path $root "eng/lint.ps1") -NoRestore 2>&1
        $text = $output -join [Environment]::NewLine
        Assert-Fixture ($LASTEXITCODE -eq 0) "a test-only target must pass the lint gate: $text"
        Assert-Fixture (-not $text.Contains("must not receive internals")) "the guard must not report a test target: $text"
    }

    # The docs key cross-check runs without the private facts checkout, against the tracked public key
    # list, and covers both content roots: docs/docs and the Learn track under docs/learn.
    Invoke-Fixture "check-docs-public-keys" {
        if (-not (Get-Command node -ErrorAction SilentlyContinue)) { return "skip" }

        function New-CheckDocsFixture {
            param([string]$Name)

            $root = Join-Path $fixtureRoot $Name
            New-Item -ItemType Directory -Path (Join-Path $root "eng"), (Join-Path $root "docs/docs"), (Join-Path $root "docs/learn"), (Join-Path $root "docs/src"), (Join-Path $root "docs/scripts"), (Join-Path $root "src") -Force | Out-Null
            Copy-Item -LiteralPath (Join-Path $PSScriptRoot "check-docs.ps1") -Destination (Join-Path $root "eng/check-docs.ps1")
            Set-Content -LiteralPath (Join-Path $root "docs/scripts/generate-changelog.mjs") -Value "process.exit(0);" -Encoding utf8
            Set-Content -LiteralPath (Join-Path $root "src/Fixture.cs") -Value 'namespace Fixture; public static class FixtureOptions { public const string ConfigurationSectionName = "ProtoTest:Fixture"; }' -Encoding utf8
            Set-Content -LiteralPath (Join-Path $root "docs/docs/page.md") -Value 'Set `ProtoTest:Fixture` and `ProtoTest:Mystery:Key` to configure the fixture.' -Encoding utf8
            return $root
        }

        function Invoke-CheckDocsFixture {
            param([string]$Name, [string]$KeyList, [string]$LearnPage = '')

            $root = New-CheckDocsFixture $Name
            Set-Content -LiteralPath (Join-Path $root "docs/configuration-keys.json") -Value $KeyList -Encoding utf8
            if ($LearnPage) {
                Set-Content -LiteralPath (Join-Path $root "docs/learn/page.md") -Value $LearnPage -Encoding utf8
            }
            $output = & pwsh -NoProfile -File (Join-Path $root "eng/check-docs.ps1") 2>&1
            return [pscustomobject]@{ Root = $root; Text = ($output -join [Environment]::NewLine); ExitCode = $LASTEXITCODE }
        }

        # A docs key no section constant and no allowlist entry backs fails, although the private facts
        # checkout is absent.
        $missing = Invoke-CheckDocsFixture -Name "check-docs-keys-missing" -KeyList '{"sections":["ProtoTest:Fixture"],"allowedKeys":[]}'
        Assert-Fixture ($missing.ExitCode -ne 0) "a docs key no source section backs must fail without the facts checkout: $($missing.Text)"
        Assert-Fixture ($missing.Text.Contains("ProtoTest:Mystery:Key")) "the failure must name the key: $($missing.Text)"

        # Listing it with a reason (and it staying taught) passes.
        $covered = Invoke-CheckDocsFixture -Name "check-docs-keys-covered" -KeyList '{"sections":["ProtoTest:Fixture"],"allowedKeys":[{"key":"ProtoTest:Mystery:Key","reason":"fixture"}]}'
        Assert-Fixture ($covered.ExitCode -eq 0) "the allowlist must cover the key: $($covered.Text)"

        # A tracked section list that drifted from the source constants fails.
        $drift = Invoke-CheckDocsFixture -Name "check-docs-keys-drift" -KeyList '{"sections":["ProtoTest:Stale"],"allowedKeys":[{"key":"ProtoTest:Mystery:Key","reason":"fixture"}]}'
        Assert-Fixture ($drift.ExitCode -ne 0) "a stale section list must fail: $($drift.Text)"
        Assert-Fixture ($drift.Text.Contains("stale")) "the drift failure must say so: $($drift.Text)"

        # A Learn page is scanned like a reference page: a key it teaches that no section or allowlist
        # covers fails, and a Learn page whose keys are covered passes.
        $learnMissing = Invoke-CheckDocsFixture -Name "check-docs-learn-keys" -KeyList '{"sections":["ProtoTest:Fixture"],"allowedKeys":[{"key":"ProtoTest:Mystery:Key","reason":"fixture"}]}' -LearnPage 'Set `ProtoTest:LessonMystery:Key` in the lesson host.'
        Assert-Fixture ($learnMissing.ExitCode -ne 0) "a key on a Learn page that no source section backs must fail: $($learnMissing.Text)"
        Assert-Fixture ($learnMissing.Text.Contains("ProtoTest:LessonMystery:Key")) "the failure must name the Learn key: $($learnMissing.Text)"

        $learnCovered = Invoke-CheckDocsFixture -Name "check-docs-learn-covered" -KeyList '{"sections":["ProtoTest:Fixture"],"allowedKeys":[{"key":"ProtoTest:Mystery:Key","reason":"fixture"}]}' -LearnPage 'Set `ProtoTest:Fixture` in the lesson host.'
        Assert-Fixture ($learnCovered.ExitCode -eq 0) "a Learn page whose keys are covered must pass: $($learnCovered.Text)"

        # The Add* name check reaches the Learn root too.
        $learnApi = Invoke-CheckDocsFixture -Name "check-docs-learn-api" -KeyList '{"sections":["ProtoTest:Fixture"],"allowedKeys":[{"key":"ProtoTest:Mystery:Key","reason":"fixture"}]}' -LearnPage 'Call `AddLessonOnly` to compose the lesson host.'
        Assert-Fixture ($learnApi.ExitCode -ne 0) "an Add* name on a Learn page that exists nowhere must fail: $($learnApi.Text)"
        Assert-Fixture ($learnApi.Text.Contains("AddLessonOnly")) "the failure must name the Learn API name: $($learnApi.Text)"
    }

    # The generated changelog is release history: a breaking-change note names the symbols and methods
    # it removed on purpose, so the removed-name checks skip it while a normal page still fails.
    Invoke-Fixture "check-docs-generated-changelog" {
        if (-not (Get-Command node -ErrorAction SilentlyContinue)) { return "skip" }

        function New-ChangelogFixture {
            param([string]$Name, [string]$ChangelogPage, [string]$RegularPage = '')

            $root = Join-Path $fixtureRoot $Name
            New-Item -ItemType Directory -Path (Join-Path $root "eng"), (Join-Path $root "docs/docs"), (Join-Path $root "docs/learn"), (Join-Path $root "docs/src/pages"), (Join-Path $root "docs/src/data"), (Join-Path $root "docs/scripts"), (Join-Path $root "src") -Force | Out-Null
            Copy-Item -LiteralPath (Join-Path $PSScriptRoot "check-docs.ps1") -Destination (Join-Path $root "eng/check-docs.ps1")
            Set-Content -LiteralPath (Join-Path $root "docs/scripts/generate-changelog.mjs") -Value "process.exit(0);" -Encoding utf8
            Set-Content -LiteralPath (Join-Path $root "docs/configuration-keys.json") -Value '{"sections":[],"allowedKeys":[]}' -Encoding utf8
            Set-Content -LiteralPath (Join-Path $root "src/Fixture.cs") -Value 'namespace Fixture; public sealed class Widget { }' -Encoding utf8
            Set-Content -LiteralPath (Join-Path $root "src/CompatibilitySuppressions.xml") -Value @'
<?xml version="1.0" encoding="utf-8"?>
<Suppressions xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
  <Suppression>
    <DiagnosticId>CP0002</DiagnosticId>
    <Target>M:Fixture.Widget.get_Gone</Target>
  </Suppression>
</Suppressions>
'@ -Encoding utf8
            Set-Content -LiteralPath (Join-Path $root "docs/src/pages/changelog.md") -Value $ChangelogPage -Encoding utf8
            Set-Content -LiteralPath (Join-Path $root "docs/src/data/changelog.generated.ts") -Value "export const body = '';" -Encoding utf8
            if ($RegularPage) {
                Set-Content -LiteralPath (Join-Path $root "docs/docs/page.md") -Value $RegularPage -Encoding utf8
            }
            $output = & pwsh -NoProfile -File (Join-Path $root "eng/check-docs.ps1") 2>&1
            return [pscustomobject]@{ Text = ($output -join [Environment]::NewLine); ExitCode = $LASTEXITCODE }
        }

        # The changelog may name a removed member and an Add* method that no longer exists.
        $exempt = New-ChangelogFixture -Name "check-docs-changelog-exempt" -ChangelogPage 'The reshape removed `Widget.Gone` and `AddGoneThing`.'
        Assert-Fixture ($exempt.ExitCode -eq 0) "the generated changelog must be exempt from the removed-name checks: $($exempt.Text)"

        # A normal docs page still fails on the same names.
        $regular = New-ChangelogFixture -Name "check-docs-changelog-regular" -ChangelogPage 'Nothing was removed.' -RegularPage 'The reshape removed `Widget.Gone`.'
        Assert-Fixture ($regular.ExitCode -ne 0) "a normal page naming a removed member must still fail: $($regular.Text)"
        Assert-Fixture ($regular.Text.Contains("Widget.Gone")) "the failure must name the member: $($regular.Text)"
    }

    # A repository path named in docs prose must resolve: the check that catches a retired path after
    # its files were deleted, without waiting for the site build to fail on a bundled file.
    Invoke-Fixture "check-docs-repository-paths" {
        if (-not (Get-Command node -ErrorAction SilentlyContinue)) { return "skip" }

        $root = Join-Path $fixtureRoot "check-docs-repo-paths"
        New-Item -ItemType Directory -Path (Join-Path $root "eng"), (Join-Path $root "docs/docs"), (Join-Path $root "docs/learn"), (Join-Path $root "docs/src"), (Join-Path $root "docs/scripts"), (Join-Path $root "samples/Real.Project"), (Join-Path $root "src/Real.Package"), (Join-Path $root "tests/Real.Tests") -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot "check-docs.ps1") -Destination (Join-Path $root "eng/check-docs.ps1")
        Set-Content -LiteralPath (Join-Path $root "docs/scripts/generate-changelog.mjs") -Value "process.exit(0);" -Encoding utf8
        Set-Content -LiteralPath (Join-Path $root "docs/configuration-keys.json") -Value '{"sections":[],"allowedKeys":[]}' -Encoding utf8
        Set-Content -LiteralPath (Join-Path $root "samples/Real.Project/File.cs") -Value "// real" -Encoding utf8
        Set-Content -LiteralPath (Join-Path $root "src/Real.Package/Options.cs") -Value "// real" -Encoding utf8
        Set-Content -LiteralPath (Join-Path $root "tests/Real.Tests/File.cs") -Value "// real" -Encoding utf8

        # Existing repository paths pass, another layout's `src/pages` is not a repository path, and the
        # demo checkout's suite path is named but owned by another repository.
        Set-Content -LiteralPath (Join-Path $root "docs/docs/page.md") -Value 'See `samples/Real.Project/File.cs`, `src/Real.Package/Options.cs` and `tests/Real.Tests/File.cs`; a Next.js app keeps pages under `src/pages/`, and the demo runs `dotnet test tests/OpenCsms.Suite` in its own checkout.' -Encoding utf8
        $output = & pwsh -NoProfile -File (Join-Path $root "eng/check-docs.ps1") 2>&1
        $text = $output -join [Environment]::NewLine
        Assert-Fixture ($LASTEXITCODE -eq 0) "existing repository paths, a framework path and the demo checkout path must pass: $text"

        # A prose path with nothing behind it fails and names the path, `tests/` included.
        Set-Content -LiteralPath (Join-Path $root "docs/docs/page.md") -Value 'The retired suite lived at `samples/Missing.Project/File.cs`.' -Encoding utf8
        $output = & pwsh -NoProfile -File (Join-Path $root "eng/check-docs.ps1") 2>&1
        $text = $output -join [Environment]::NewLine
        Assert-Fixture ($LASTEXITCODE -ne 0) "a repository path with no file must fail: $text"
        Assert-Fixture ($text.Contains("samples/Missing.Project/File.cs")) "the failure must name the path: $text"

        Set-Content -LiteralPath (Join-Path $root "docs/docs/page.md") -Value 'The retired suite lived at `tests/Missing.Tests/File.cs`.' -Encoding utf8
        $output = & pwsh -NoProfile -File (Join-Path $root "eng/check-docs.ps1") 2>&1
        $text = $output -join [Environment]::NewLine
        Assert-Fixture ($LASTEXITCODE -ne 0) "a tests/ path with no file must fail: $text"
        Assert-Fixture ($text.Contains("tests/Missing.Tests/File.cs")) "the failure must name the tests path: $text"
    }

    # The integration pages carry the six template headings; the map and the deep task pages are
    # exempt, and a section index missing one heading fails naming the page and the heading.
    Invoke-Fixture "check-docs-integration-shape" {
        if (-not (Get-Command node -ErrorAction SilentlyContinue)) { return "skip" }

        $root = Join-Path $fixtureRoot "check-docs-integration-shape"
        New-Item -ItemType Directory -Path (Join-Path $root "eng"), (Join-Path $root "docs/docs/integrations/rest"), (Join-Path $root "docs/learn"), (Join-Path $root "docs/src"), (Join-Path $root "docs/scripts"), (Join-Path $root "src") -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot "check-docs.ps1") -Destination (Join-Path $root "eng/check-docs.ps1")
        Set-Content -LiteralPath (Join-Path $root "docs/scripts/generate-changelog.mjs") -Value "process.exit(0);" -Encoding utf8
        Set-Content -LiteralPath (Join-Path $root "docs/configuration-keys.json") -Value '{"sections":[],"allowedKeys":[]}' -Encoding utf8
        Set-Content -LiteralPath (Join-Path $root "docs/docs/integrations/overview.md") -Value "# Integrations map`n`nThe map." -Encoding utf8
        Set-Content -LiteralPath (Join-Path $root "docs/docs/integrations/rest/requests.md") -Value "# Requests`n`nA deep task page." -Encoding utf8

        $shape = @'
# Fakes

## What it adds

A fake HTTP service for a test.

## Install

```bash
dotnet add package ProtoTest.WireMock
```

## Compose

Registered on the builder.

## The tasks

Stub a route, verify a call, reset between tests.

## In the trace and coverage

Every served request is recorded.

## Limits

HTTP only.
'@
        Set-Content -LiteralPath (Join-Path $root "docs/docs/integrations/fakes.md") -Value $shape -Encoding utf8

        # A complete top-level page passes, and the map and the deep task page are not checked.
        $output = & pwsh -NoProfile -File (Join-Path $root "eng/check-docs.ps1") 2>&1
        $text = $output -join [Environment]::NewLine
        Assert-Fixture ($LASTEXITCODE -eq 0) "a complete integration page and the exempt pages must pass: $text"
        Assert-Fixture ($text.Contains("covered 1 page(s)")) "the check must cover the top-level page only: $text"

        # A section index missing one required heading fails and names both.
        $missing = $shape -replace '(?ms)^## Limits\r?\n\r?\nHTTP only\.\r?\n?$', ''
        Set-Content -LiteralPath (Join-Path $root "docs/docs/integrations/rest/index.md") -Value $missing -Encoding utf8
        $output = & pwsh -NoProfile -File (Join-Path $root "eng/check-docs.ps1") 2>&1
        $text = $output -join [Environment]::NewLine
        Assert-Fixture ($LASTEXITCODE -ne 0) "a section index missing a heading must fail: $text"
        Assert-Fixture ($text.Contains("rest/index.md")) "the failure must name the page: $text"
        Assert-Fixture ($text.Contains("## Limits")) "the failure must name the missing heading: $text"
    }

    # Every internal link fragment must name a heading in its target file: a stale anchor fails
    # naming file, line and target, while relative, absolute, same-page, index and changelog links
    # with live anchors pass.
    Invoke-Fixture "check-docs-link-fragments" {
        if (-not (Get-Command node -ErrorAction SilentlyContinue)) { return "skip" }

        $root = Join-Path $fixtureRoot "check-docs-link-fragments"
        New-Item -ItemType Directory -Path (Join-Path $root "eng"), (Join-Path $root "docs/docs/guide"), (Join-Path $root "docs/learn/track"), (Join-Path $root "docs/src"), (Join-Path $root "docs/scripts"), (Join-Path $root "src") -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot "check-docs.ps1") -Destination (Join-Path $root "eng/check-docs.ps1")
        Set-Content -LiteralPath (Join-Path $root "docs/scripts/generate-changelog.mjs") -Value "process.exit(0);" -Encoding utf8
        Set-Content -LiteralPath (Join-Path $root "docs/configuration-keys.json") -Value '{"sections":[],"allowedKeys":[]}' -Encoding utf8
        Set-Content -LiteralPath (Join-Path $root "src/Fixture.cs") -Value 'namespace Fixture; public sealed class Fixture { }' -Encoding utf8
        Set-Content -LiteralPath (Join-Path $root "docs/docs/guide/index.md") -Value "# Guide Index`n`n## Guide Start`n" -Encoding utf8
        Set-Content -LiteralPath (Join-Path $root "docs/docs/guide/target.md") -Value "# Target Page`n`n## Setup Options`n" -Encoding utf8
        Set-Content -LiteralPath (Join-Path $root "docs/learn/track/lesson.md") -Value "# Lesson One`n`n## First Steps`n`nSee [the options](/docs/guide/target#setup-options) and [the start](#first-steps).`n" -Encoding utf8
        Set-Content -LiteralPath (Join-Path $root "docs/docs/guide/page.md") -Value @'
# Source Page

## Local Section

See [the options](./target.md#setup-options), [the same page](#local-section),
[the absolute target](/docs/guide/target#setup-options), [the guide start](./#guide-start)
and [the process](https://github.com/MSeys/ProtoTest/blob/main/CONTRIBUTING.md#context-lookups).
'@ -Encoding utf8
        Set-Content -LiteralPath (Join-Path $root "CHANGELOG.md") -Value @'
# Changelog

## Unreleased

- A thing. [Target](https://prototest.dev/docs/guide/target#setup-options)
'@ -Encoding utf8

        # A stale anchor fails and names the file, the line and the target.
        Add-Content -LiteralPath (Join-Path $root "docs/docs/guide/page.md") -Value "`nSee [the gone options](./target.md#no-such-section).`n" -Encoding utf8
        Add-Content -LiteralPath (Join-Path $root "CHANGELOG.md") -Value "- Gone. [Target](https://prototest.dev/docs/guide/target#no-such-changelog-section)`n" -Encoding utf8
        $output = & pwsh -NoProfile -File (Join-Path $root "eng/check-docs.ps1") 2>&1
        $text = $output -join [Environment]::NewLine
        Assert-Fixture ($LASTEXITCODE -ne 0) "a stale link fragment must fail: $text"
        Assert-Fixture ($text.Contains("guide/page.md") -and $text.Contains("target.md#no-such-section") -and $text.Contains("->")) "the failure must read 'file:line -> target#fragment': $text"
        Assert-Fixture ($text.Contains("CHANGELOG.md") -and $text.Contains("#no-such-changelog-section")) "the changelog anchor failure must name its target: $text"

        # The same tree with live anchors passes: relative, absolute, same-page, index, external
        # and changelog links alike.
        Set-Content -LiteralPath (Join-Path $root "docs/docs/guide/page.md") -Value @'
# Source Page

## Local Section

See [the options](./target.md#setup-options), [the same page](#local-section),
[the absolute target](/docs/guide/target#setup-options), [the guide start](./#guide-start)
and [the process](https://github.com/MSeys/ProtoTest/blob/main/CONTRIBUTING.md#context-lookups).
'@ -Encoding utf8
        Set-Content -LiteralPath (Join-Path $root "CHANGELOG.md") -Value @'
# Changelog

## Unreleased

- A thing. [Target](https://prototest.dev/docs/guide/target#setup-options)
'@ -Encoding utf8
        $output = & pwsh -NoProfile -File (Join-Path $root "eng/check-docs.ps1") 2>&1
        $text = $output -join [Environment]::NewLine
        Assert-Fixture ($LASTEXITCODE -eq 0) "live anchors must pass: $text"
    }

    # The workflow's template smoke step stays on the shared script, so CI and the local fixture test
    # the same five runners instead of drifting apart.
    Invoke-Fixture "template-workflow-script" {
        $workflow = Get-Content -Raw -LiteralPath (Join-Path $repository ".github/workflows/verify.yml")
        Assert-Fixture ($workflow -match "run: \./eng/test-template\.ps1") "the template smoke step must call eng/test-template.ps1"
    }

    # The starter template is proved end to end whenever the packed feed exists: generation, restore,
    # build and test for all five runners, the same script CI runs after pack. Without the feed
    # (a plain source checkout) the fixture skips like the MTP guards without built binaries.
    Invoke-Fixture "template-starter-runners" {
        $packages = Join-Path $repository "artifacts/packages"
        $template = if (Test-Path -LiteralPath $packages) {
            Get-ChildItem -LiteralPath $packages -Filter "ProtoTest.Templates.*.nupkg" -File |
                Where-Object { $_.Name -notlike '*.snupkg' } |
                Select-Object -First 1
        }
        else { $null }
        if (-not $template) { return "skip" }

        $output = & pwsh -NoProfile -File (Join-Path $repository "eng/test-template.ps1") 2>&1
        $text = $output -join [Environment]::NewLine
        Assert-Fixture ($LASTEXITCODE -eq 0) "the starter template must generate, restore, build and test for every runner: $text"
    }

    # Publishing from a branch ref fails before any push; dry runs stay allowed.
    Invoke-Fixture "release-branch-ref" {
        $root = New-FixtureRepository "release-branch-ref"
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot "release.ps1") -Destination (Join-Path $root "eng/release.ps1")

        try {
            $env:GITHUB_REF_TYPE = "branch"
            $env:GITHUB_REF_NAME = "main"
            $output = & pwsh -NoProfile -File (Join-Path $root "eng/release.ps1") 2>&1
            $text = $output -join [Environment]::NewLine
            Assert-Fixture ($LASTEXITCODE -ne 0) "publishing from a branch ref must fail: $text"
            Assert-Fixture ($text.Contains("tag")) "the failure must name the tag requirement: $text"
            Assert-Fixture (-not $text.Contains("packages directory")) "the ref guard must fire before package validation: $text"

            $dry = & pwsh -NoProfile -File (Join-Path $root "eng/release.ps1") -DryRun 2>&1
            $dryText = $dry -join [Environment]::NewLine
            Assert-Fixture ($dryText.Contains("packages directory")) "a dry run must pass the ref guard and fail on the missing packages instead: $dryText"
        }
        finally {
            Remove-Item Env:GITHUB_REF_TYPE -ErrorAction SilentlyContinue
            Remove-Item Env:GITHUB_REF_NAME -ErrorAction SilentlyContinue
        }
    }

    # The release workflow keeps the publish path tag-gated.
    Invoke-Fixture "release-workflow-tag-guard" {
        $workflow = Get-Content -Raw -LiteralPath (Join-Path $repository ".github/workflows/release.yml")
        Assert-Fixture ($workflow -match "(?s)id: login.*?if: [^\n]*github\.ref_type == 'tag'") "the NuGet login step must be gated on a tag"
        Assert-Fixture ($workflow -match "(?s)name: Publish to NuGet.*?github\.ref_type[^\n]*'tag'") "the publish step must refuse non-tag refs"
        Assert-Fixture ($workflow -match "(?s)Create or update GitHub Release\s*\n\s*if: github\.ref_type == 'tag'") "the GitHub release step stays tag-gated"
    }

    # Both MTP zero-test guards fail on a filter that matches no test. The xUnit v3
    # runner exits 0 on a zero-test run, so the structured JUnit result is what the gate compares with
    # its minimum; TUnit enforces --minimum-expected-tests itself. Needs the Release test binaries.
    Invoke-Fixture "mtp-zero-test-guards" {
        $xunit3Project = Join-Path $repository "tests/ProtoTest.Xunit3.Tests/ProtoTest.Xunit3.Tests.csproj"
        $tunitProject = Join-Path $repository "tests/ProtoTest.TUnit.Tests/ProtoTest.TUnit.Tests.csproj"
        $xunit3Dll = Join-Path $repository "tests/ProtoTest.Xunit3.Tests/bin/Release/net8.0/ProtoTest.Xunit3.Tests.dll"
        $tunitDll = Join-Path $repository "tests/ProtoTest.TUnit.Tests/bin/Release/net8.0/ProtoTest.TUnit.Tests.dll"
        if (-not (Test-Path -LiteralPath $xunit3Dll) -or -not (Test-Path -LiteralPath $tunitDll)) { return "skip" }

        $testScript = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot "test.ps1")
        $xunit3Minimum = [int][regex]::Match($testScript, 'ProtoTest\.Xunit3\.Tests.*?MinimumTests\s*=\s*(\d+)', [Text.RegularExpressions.RegexOptions]::Singleline).Groups[1].Value
        $tunitMinimum = [int][regex]::Match($testScript, 'ProtoTest\.TUnit\.Tests.*?MinimumTests\s*=\s*(\d+)', [Text.RegularExpressions.RegexOptions]::Singleline).Groups[1].Value
        Assert-Fixture ($xunit3Minimum -gt 0) "test.ps1 must declare the xUnit.net v3 minimum"
        Assert-Fixture ($tunitMinimum -gt 0) "test.ps1 must declare the TUnit minimum"

        $resultPath = Join-Path $fixtureRoot "xunit3-zero.xml"
        $xunitOutput = & dotnet run --project $xunit3Project --configuration Release --no-build --no-restore -- -method "*__GateFixtureNoSuchTest__*" -result-junit $resultPath 2>&1
        $xunitText = $xunitOutput -join [Environment]::NewLine
        Assert-Fixture ($LASTEXITCODE -eq 0) "the xUnit.net v3 runner exits 0 on a zero-test run, which is why the JUnit result is parsed: $xunitText"
        Assert-Fixture (Test-Path -LiteralPath $resultPath) "the zero-test run wrote no JUnit result: $xunitText"
        $xunitTests = [int]([xml](Get-Content -Raw -LiteralPath $resultPath)).testsuites.tests
        Assert-Fixture ($xunitTests -eq 0) "the zero-test run must report zero tests, got $xunitTests"
        Assert-Fixture ($xunitTests -lt $xunit3Minimum) "the declared xUnit.net v3 minimum ($xunit3Minimum) must reject zero"

        $tunitOutput = & dotnet run --project $tunitProject --configuration Release --no-build --no-restore -- --treenode-filter "/*/*/*/__GateFixtureNoSuchTest__" --minimum-expected-tests $tunitMinimum 2>&1
        $tunitText = $tunitOutput -join [Environment]::NewLine
        Assert-Fixture ($LASTEXITCODE -ne 0) "TUnit must fail when fewer tests run than the declared minimum: $tunitText"
        Assert-Fixture ($tunitText.Contains("Minimum expected tests")) "the TUnit failure must name the policy: $tunitText"
    }

    if ($failures.Count -gt 0) {
        Write-Host ""
        Write-Host "gate fixtures failed ($($failures.Count)):"
        foreach ($failure in $failures) { Write-Host "  $failure" }
        exit 1
    }

    Write-Host ""
    Write-Host "all gate fixtures passed."
    exit 0
}
finally {
    if ($env:PROTOTEST_KEEP_GATE_FIXTURES -eq "1") {
        Write-Host "keeping gate fixtures at $fixtureRoot"
    }
    else {
        Remove-Item -LiteralPath $fixtureRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
