param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"
$repository = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repository "samples/Northstar.ProtoTest/Northstar.ProtoTest.csproj"
$traceDir = Join-Path $repository "samples/Northstar.ProtoTest/bin/$Configuration/net8.0/TestResults"
$traceFilter = "prototest-*.prototrace"
$destinationDirectory = Join-Path $repository "viewer/public/demos"
$destination = Join-Path $destinationDirectory "prototest-demo.prototrace"
$runStartedAtUtc = [DateTime]::UtcNow

# The bundled demo is the whole Northstar suite with the drills switched on, so the viewer's landing
# demo shows real failures next to the passing journeys. The four drills fail on purpose; the warning
# journey passes with a warning, so the archive also carries one partial outcome and its finding.
# The archive checks below require exactly those and no others, and the run's own gate verdict
# travels with it.
$intentionalFailureNames = @(
    "ARealWaitDoesNotCloseTheDueWindow",
    "AnUnknownProjectIdIsTreatedAsMine",
    "ABareStatusHidesWhatTheApplicationSaid",
    "TheAddressWasHardcodedForOneMachine"
)
$intentionalPartialNames = @(
    "APassingJourneyCanStillCarryAWarning"
)

New-Item -ItemType Directory -Force -Path $destinationDirectory | Out-Null

# Both files come from this run: the destination is deleted first so a stale committed trace can never
# pass the copy, and the source is deleted so a run that writes no trace fails instead of reusing one.
Remove-Item -LiteralPath $destination -Force -ErrorAction SilentlyContinue
Remove-Item -Path (Join-Path $traceDir $traceFilter) -Force -ErrorAction SilentlyContinue

$arguments = @(
    "test", $project,
    "--configuration", $Configuration,
    "--verbosity", "minimal"
)
if ($NoBuild) { $arguments += "--no-build" }

$previousDrills = $env:ProtoTest__Sample__Drills
$env:ProtoTest__Sample__Drills = "true"
try {
    $output = & dotnet @arguments 2>&1
    $exitCode = $LASTEXITCODE
}
finally {
    if ($null -eq $previousDrills) { Remove-Item Env:ProtoTest__Sample__Drills -ErrorAction SilentlyContinue }
    else { $env:ProtoTest__Sample__Drills = $previousDrills }
}

$text = $output -join [Environment]::NewLine
$summary = [regex]::Match($text, 'Failed:\s*(\d+),\s*Passed:\s*(\d+),\s*Skipped:\s*(\d+),\s*Total:\s*(\d+)')
if (-not $summary.Success) {
    $output | ForEach-Object { Write-Host $_ }
    throw "The demo run wrote no NUnit summary line; fix the run before regenerating the demo."
}
if ($exitCode -eq 0) {
    throw "The demo run unexpectedly succeeded; its intentional failures were not exercised."
}
foreach ($name in $intentionalFailureNames) {
    if ($text -notmatch "Failed\s+$([regex]::Escape($name))\b") {
        $output | ForEach-Object { Write-Host $_ }
        throw "The demo run did not exercise the intentional failure '$name'."
    }
}
$traceFiles = @(Get-ChildItem -Path $traceDir -Filter $traceFilter -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTimeUtc -Descending)
if ($traceFiles.Count -eq 0) { throw "ProtoTest did not write the expected trace: $(Join-Path $traceDir $traceFilter)" }
$trace = $traceFiles[0].FullName
$traceFile = Get-Item -LiteralPath $trace
if ($traceFile.LastWriteTimeUtc -lt $runStartedAtUtc.AddSeconds(-1)) {
    throw "The test run did not produce a fresh ProtoTrace archive."
}

# The viewer reads spans.json and state.json; validate those, not a compatibility view.
Add-Type -AssemblyName System.IO.Compression.FileSystem
function Read-ArchiveJson($archive, [string]$name) {
    $entry = $archive.GetEntry($name)
    if ($null -eq $entry) { throw "The generated archive has no $name entry." }
    $reader = [System.IO.StreamReader]::new($entry.Open())
    try { return $reader.ReadToEnd() | ConvertFrom-Json }
    finally { $reader.Dispose() }
}

$archive = [System.IO.Compression.ZipFile]::OpenRead($trace)
try {
    $spans = Read-ArchiveJson $archive "spans.json"
    $state = Read-ArchiveJson $archive "state.json"

    # Where in the suite's code each operation started, with that code embedded for the viewer.
    $manifest = Read-ArchiveJson $archive "manifest.json"
    if ($null -eq $manifest.sources) { throw "Expected embedded sources in the manifest." }
    $located = @($spans.resourceSpans | ForEach-Object { @($_.scopeSpans) | ForEach-Object { $_.spans } } |
        Where-Object { $_ -and $_.attributes.'code.file.path' })
    if ($located.Count -lt 1) { throw "Expected operations with a code.file.path in spans.json." }
    foreach ($path in @($located | ForEach-Object { $_.attributes.'code.file.path' } | Sort-Object -Unique)) {
        $embedded = $manifest.sources.PSObject.Properties[$path].Value
        if (-not $embedded -or $null -eq $archive.GetEntry($embedded)) {
            throw "Source '$path' is located by an operation but not embedded in the archive."
        }
        if ([System.IO.Path]::IsPathRooted($path)) { throw "Source '$path' is recorded as an absolute path." }
    }

    # Every declared artifact must be in the archive; every attachment must refer to a declared artifact.
    foreach ($group in @($spans.resourceSpans)) {
        $declared = @{}
        foreach ($artifact in @($group.artifacts)) {
            if ($null -eq $artifact) { continue }
            $declared[$artifact.id] = $artifact
            if (-not $artifact.error -and $null -eq $archive.GetEntry($artifact.archivePath)) {
                throw "Artifact '$($artifact.id)' is declared but '$($artifact.archivePath)' is not in the archive."
            }
        }
        $groupSpans = @(@($group.scopeSpans) | ForEach-Object { $_.spans })
        $groupEvents = @(@($group.scopeSpans) | ForEach-Object { $_.events })
        $attachments = @(@($groupSpans | ForEach-Object { $_.events }) + $groupEvents |
            Where-Object { $_ -and $_.record -eq "attachment" })
        foreach ($attachment in $attachments) {
            if (-not $attachment.artifactId -or -not $declared.ContainsKey($attachment.artifactId)) {
                throw "Attachment '$($attachment.name)' does not refer to an artifact declared on its resource."
            }
        }
    }
}
finally {
    $archive.Dispose()
}

$groups = @($spans.resourceSpans)
$testGroups = @($groups | Where-Object { $_.resource.attributes.testId })
$runGroup = @($groups | Where-Object { $_.resource.attributes.runId })
if ($runGroup.Count -ne 1) { throw "Expected exactly one run resource in spans.json." }
if (-not $runGroup[0].resource.attributes.'environment.runtime') {
    throw "Expected the run environment on the run resource attributes."
}

# The suite grows with every journey, so the exact totals change: require at least the journeys that
# existed when this gate was written and allow new succeeded, partial or skipped tests.
$minimumTestCount = 15
if ($testGroups.Count -lt $minimumTestCount) {
    throw "Expected at least $minimumTestCount test resources in spans.json, but found $($testGroups.Count)."
}

$unionOutcomes = @("succeeded", "partial", "failed", "skipped")
$unknownTests = @($testGroups | Where-Object {
    $_.resource.attributes.testOutcome -notin $unionOutcomes
})
if ($unknownTests.Count -gt 0) {
    throw "Expected every test resource to report a succeeded, partial, failed or skipped outcome, but $($unknownTests.Count) did not."
}

$failedTests = @($testGroups | Where-Object { $_.resource.attributes.testOutcome -eq "failed" })
foreach ($intentionalFailure in $intentionalFailureNames) {
    $intentionalFailures = @($failedTests | Where-Object {
        $_.resource.attributes.testMethod -eq $intentionalFailure
    })
    if ($intentionalFailures.Count -ne 1) {
        throw "Expected exactly one intentional failure '$intentionalFailure' in spans.json, but found $($intentionalFailures.Count)."
    }
}

$unexpectedFailures = @($failedTests | Where-Object {
    $_.resource.attributes.testMethod -notin $intentionalFailureNames
})
if ($unexpectedFailures.Count -gt 0) {
    $names = @($unexpectedFailures | ForEach-Object { $_.resource.attributes.testMethod }) -join ", "
    throw "Expected no failures other than the intentional ones, but found: $names."
}

$partialTests = @($testGroups | Where-Object { $_.resource.attributes.testOutcome -eq "partial" })
foreach ($intentionalPartial in $intentionalPartialNames) {
    $intentionalPartials = @($partialTests | Where-Object {
        $_.resource.attributes.testMethod -eq $intentionalPartial
    })
    if ($intentionalPartials.Count -ne 1) {
        throw "Expected exactly one intentional partial '$intentionalPartial' in spans.json, but found $($intentionalPartials.Count)."
    }
}

$unexpectedPartials = @($partialTests | Where-Object {
    $_.resource.attributes.testMethod -notin $intentionalPartialNames
})
if ($unexpectedPartials.Count -gt 0) {
    $names = @($unexpectedPartials | ForEach-Object { $_.resource.attributes.testMethod }) -join ", "
    throw "Expected no partials other than the intentional one, but found: $names."
}

$allEvents = @($groups | ForEach-Object {
        $scopes = @($_.scopeSpans)
        @($scopes | ForEach-Object { $_.spans } | ForEach-Object { $_.events }) + @($scopes | ForEach-Object { $_.events })
    } | Where-Object { $_ })
if ($testGroups | Where-Object { @(@($_.scopeSpans) | ForEach-Object { $_.spans }).Count -lt 1 }) {
    throw "Expected every test resource to carry spans."
}

# The run's own trace: how its gates judged it. Gate verdicts have no operation above them, so they
# travel as scope events.
$runScopes = @($runGroup[0].scopeSpans)
$runSpans = @($runScopes | ForEach-Object { $_.spans })
$runKinds = @($runSpans | ForEach-Object { $_.kind }) +
    @(@($runSpans | ForEach-Object { $_.events }) + @($runScopes | ForEach-Object { $_.events }) |
        Where-Object { $_ } | ForEach-Object { $_.kind })
if ($runKinds -notcontains "gate.evaluate") {
    throw "Expected the run's gate verdicts in the viewer trace."
}

# Evidence is span events: observations and attachments. The warning journey records a finding,
# so the demo carries one too; the two the viewer always reads must be there.
foreach ($record in @("observation", "attachment", "finding")) {
    if (-not ($allEvents | Where-Object { $_.record -eq $record })) {
        throw "Expected $record events in spans.json."
    }
}

# State: what existed and how it changed, including values the application reported itself.
$items = @($state.run.items) + @($state.tests | ForEach-Object { $_.items }) | Where-Object { $_ }
if ($items.Count -lt 1) { throw "Expected tracked items in state.json." }
$changes = @($items | ForEach-Object { $_.changes } | Where-Object { $_ })
if (-not ($changes | Where-Object { $_.change -eq "released" })) {
    throw "Expected resource releases as state changes."
}
if (-not ($changes | Where-Object { $_.source -eq "applicationside" })) {
    throw "Expected application-side values from the OpenTelemetry converter in state.json."
}

Copy-Item -LiteralPath $trace -Destination $destination -Force
Write-Host "Updated viewer demo trace: $destination"
Write-Host "Run: $($summary.Value.Trim()) - $($testGroups.Count) test resources, $($failedTests.Count) intentional failures, $($partialTests.Count) intentional partials"
