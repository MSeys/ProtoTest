<#
.SYNOPSIS
Regenerates the committed trace archives from the Northstar sample.

.DESCRIPTION
lessons writes docs/static/lessons (one filtered run per lesson trace), recipes writes
viewer/public/demos/recipes, demo writes the viewer's landing demo from the whole suite with the
drills on. all runs the three in that order.

-Only limits lessons to the named traces, so adding one lesson does not rewrite the values the
other lessons quote from theirs.
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet("all", "lessons", "recipes", "demo")]
    [string]$Set = "all",
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$NoBuild,
    [string[]]$Only = @()
)

$ErrorActionPreference = "Stop"
Import-Module (Join-Path $PSScriptRoot "../lib/Proto.Eng.psm1") -Force
$repository = Get-ProtoRepository

# One filtered run per lesson trace, so the file a lesson embeds holds exactly the test the lesson
# names. A drill is expected to fail and needs ProtoTest:Sample:Drills; its paired fix must pass; the
# capability-gated broker journey must skip by itself. The tests, filters and order are fixed; the run
# id and timestamps vary with the run, the same limit the recipe traces have.
$lessonTraces = @(
    @{ Name = "l0-time-drill"; Filter = "FullyQualifiedName~FailureDrills.ARealWaitDoesNotCloseTheDueWindow"; Drills = $true; Expect = "Failed" },
    @{ Name = "l0-time-fix"; Filter = "FullyQualifiedName~FailureDrills.TheTestClockClosesTheDueWindow"; Expect = "Passed" },
    @{ Name = "l0-state-drill"; Filter = "FullyQualifiedName~FailureDrills.AnUnknownProjectIdIsTreatedAsMine"; Drills = $true; Expect = "Failed" },
    @{ Name = "l0-state-fix"; Filter = "FullyQualifiedName~FailureDrills.EachTenantSeesOnlyItsOwnProjects"; Expect = "Passed" },
    @{ Name = "l0-environment-drill"; Filter = "FullyQualifiedName~FailureDrills.TheAddressWasHardcodedForOneMachine"; Drills = $true; Expect = "Failed" },
    @{ Name = "l0-environment-fix"; Filter = "FullyQualifiedName~FailureDrills.TheAddressComesFromTheComposition"; Expect = "Passed" },
    @{ Name = "l0-visibility-drill"; Filter = "FullyQualifiedName~FailureDrills.ABareStatusHidesWhatTheApplicationSaid"; Drills = $true; Expect = "Failed" },
    @{ Name = "l0-visibility-fix"; Filter = "FullyQualifiedName~FailureDrills.TheProblemBodyNamesTheCodeAndDetail"; Expect = "Passed" },
    @{ Name = "l1-first-journey"; Filter = "FullyQualifiedName~ProjectsJourney.CreatingAProjectReturnsIt"; Expect = "Passed" },
    @{ Name = "l2-broker-skip"; Filter = "FullyQualifiedName~BrokerJourney.PayingAnInvoicePublishesAnInvoicePaidEvent"; Expect = "Skipped" },
    @{ Name = "l3-clock-window"; Filter = "FullyQualifiedName~ClockJourney.ClosingTheBillingPeriodIssuesTheInvoiceOnTheTestClock"; Expect = "Passed" },
    @{ Name = "l3-lagging-read"; Filter = "FullyQualifiedName~WebhookJourney.CreatingAProjectDeliversItsWebhook"; Expect = "Passed" },
    # The flaky drill passes or fails by timing (about one Release run in six fails), so the passing
    # trace re-runs it until it passes. The failing trace widens the race with a two-second dispatcher,
    # the way the lesson confirms the cause, and fails on the first run.
    @{ Name = "l4-flaky-pass"; Filter = "FullyQualifiedName~WebhookJourney.OneReadRacesTheDispatcher"; Drills = $true; Expect = "Passed"; Attempts = 10 },
    @{ Name = "l4-flaky-fail"; Filter = "FullyQualifiedName~WebhookJourney.OneReadRacesTheDispatcher"; Drills = $true; Expect = "Failed"; Attempts = 3; Environment = @{ Northstar__WebhookDispatchInterval = "00:00:02" } },
    @{ Name = "l4-coverage"; Filter = "FullyQualifiedName~PlatformJourney.RestWritesAreVisibleThroughGraphQL"; Expect = "Passed" },
    @{ Name = "l4-artifacts"; Filter = "FullyQualifiedName~SheetsJourney.TheMonthlyReportMatchesItsModel"; Expect = "Passed" },
    # NUnit reports a warning test as skipped, so the summary check expects Skipped; the archive check
    # after the copy proves the run recorded a partial outcome and a finding.
    @{ Name = "l4-partial"; Filter = "FullyQualifiedName~FailureDrills.APassingJourneyCanStillCarryAWarning"; Drills = $true; Expect = "Skipped"; ExpectPartial = $true }
)

$recipeTraces = @(
    @{ Name = "rest-graphql"; Filter = "FullyQualifiedName~PlatformJourney.RestWritesAreVisibleThroughGraphQL" },
    @{ Name = "rest-database"; Filter = "FullyQualifiedName~DomainAccessJourney.AProjectCreatedThroughRestIsCommittedToTheDatabase" },
    @{ Name = "workbook"; Filter = "FullyQualifiedName~SheetsJourney.TheMonthlyReportMatchesItsModel" }
)

function Copy-FreshTrace([object]$Run, [string]$Destination) {
    if (-not $Run.Trace) { throw "ProtoTest did not write a trace for this run. Log: $($Run.Log)" }
    Copy-Item -LiteralPath $Run.Trace -Destination $Destination -Force
}

function Assert-PartialTrace([string]$archivePath) {
    # The runner summary cannot tell a warning from a skip, so read the archive itself: one test
    # resource with a partial outcome and at least one finding event.
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($archivePath)
    try {
        $reader = [System.IO.StreamReader]::new($archive.GetEntry("spans.json").Open())
        try { $spans = $reader.ReadToEnd() | ConvertFrom-Json }
        finally { $reader.Dispose() }
        $tests = @($spans.resourceSpans | Where-Object { $_.resource.attributes.testId })
        if ($tests.Count -ne 1) {
            throw "Expected one test resource in $archivePath, but found $($tests.Count)."
        }
        if ($tests[0].resource.attributes.testOutcome -ne "partial") {
            throw "Expected a partial outcome in $archivePath, but found $($tests[0].resource.attributes.testOutcome)."
        }
        $events = @($spans.resourceSpans | ForEach-Object { $_.scopeSpans } | ForEach-Object {
            @($_.spans | ForEach-Object { $_.events }) + @($_.events)
        } | Where-Object { $_ })
        if (-not ($events | Where-Object { $_.record -eq "finding" })) {
            throw "Expected a finding event in $archivePath."
        }
    }
    finally {
        $archive.Dispose()
    }
}

function Write-LessonTraces {
    $destination = Join-Path $repository "docs/static/lessons"
    New-Item -ItemType Directory -Force -Path $destination | Out-Null
    # ./proto forwards "-Only a,b" as one string, so split it here as well.
    $names = @($Only | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    $selected = @($lessonTraces | Where-Object { $names.Count -eq 0 -or $names -contains $_.Name })
    $unknown = @($names | Where-Object { $_ -notin $lessonTraces.Name })
    if ($unknown.Count -gt 0) {
        throw "Unknown lesson trace(s): $($unknown -join ', '). Known: $($lessonTraces.Name -join ', ')."
    }
    foreach ($item in $selected) {
        # The destination is deleted first so a stale committed trace can never pass the copy.
        $destinationFile = Join-Path $destination "$($item.Name).prototrace"
        Remove-Item -LiteralPath $destinationFile -Force -ErrorAction SilentlyContinue

        $attempts = if ($item.Attempts) { [int]$item.Attempts } else { 1 }
        $environment = if ($item.Environment) { $item.Environment } else { @{} }
        $previous = @{}
        foreach ($key in $environment.Keys) {
            $previous[$key] = [Environment]::GetEnvironmentVariable($key)
            [Environment]::SetEnvironmentVariable($key, $environment[$key])
        }
        try {
            for ($attempt = 1; $attempt -le $attempts; $attempt++) {
                $run = Invoke-ProtoSampleRun -Configuration $Configuration -Filter $item.Filter -NoBuild:$NoBuild -Drills:([bool]$item.Drills) -Name $item.Name
                $summary = $run.Summary
                $matched = $summary.Success
                if ($matched) {
                    $matched = [int]$summary.Groups[1].Value -eq $(if ($item.Expect -eq "Failed") { 1 } else { 0 })
                    $matched = $matched -and [int]$summary.Groups[2].Value -eq $(if ($item.Expect -eq "Passed") { 1 } else { 0 })
                    $matched = $matched -and [int]$summary.Groups[3].Value -eq $(if ($item.Expect -eq "Skipped") { 1 } else { 0 })
                    $matched = $matched -and [int]$summary.Groups[4].Value -eq 1
                    $matched = $matched -and $run.ExitCode -eq $(if ($item.Expect -eq "Failed") { 1 } else { 0 })
                }
                if ($matched) { break }
            }
        }
        finally {
            foreach ($key in $environment.Keys) { [Environment]::SetEnvironmentVariable($key, $previous[$key]) }
        }
        if (-not $matched) {
            Write-ProtoTail $run.Text
            throw "The run for $($item.Name) did not end with its one test $($item.Expect.ToLowerInvariant()) in $attempts run(s) (exit $($run.ExitCode)). Log: $($run.Log)"
        }

        Copy-FreshTrace $run $destinationFile
        Write-Host "Wrote $($item.Name).prototrace ($($item.Expect.ToLowerInvariant())): $($summary.Value.Trim())"
        if ($item.ExpectPartial) { Assert-PartialTrace $destinationFile }
    }
    Write-Host "Wrote $($selected.Count) lesson traces to $destination"
}

function Write-RecipeTraces {
    $destination = Join-Path $repository "viewer/public/demos/recipes"
    New-Item -ItemType Directory -Force -Path $destination | Out-Null
    foreach ($recipe in $recipeTraces) {
        $destinationFile = Join-Path $destination "$($recipe.Name).prototrace"
        Remove-Item -LiteralPath $destinationFile -Force -ErrorAction SilentlyContinue

        $run = Invoke-ProtoSampleRun -Configuration $Configuration -Filter $recipe.Filter -NoBuild:$NoBuild -Name $recipe.Name
        if ($run.ExitCode -ne 0) {
            Write-ProtoTail $run.Text
            throw "Trace generation failed for $($recipe.Name). Log: $($run.Log)"
        }
        Copy-FreshTrace $run $destinationFile
        Write-Host "Generated $($recipe.Name).prototrace"
    }
}

function Write-DemoTrace {
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
    Remove-Item -LiteralPath $destination -Force -ErrorAction SilentlyContinue

    # OneReadRacesTheDispatcher is the flaky lesson's race and fails some of the time with the drills on.
    # The demo shows the four intentional failures only, so a run where the race lost is run again.
    $flakyName = "OneReadRacesTheDispatcher"
    for ($attempt = 1; $attempt -le 5; $attempt++) {
        $run = Invoke-ProtoSampleRun -Configuration $Configuration -NoBuild:($NoBuild -or $attempt -gt 1) -Drills -Name "demo"
        if ($run.Text -notmatch "Failed\s+$flakyName\b") { break }
        Write-Host "The demo run lost the $flakyName race (attempt $attempt of 5); running it again."
    }
    $text = $run.Text
    $summary = $run.Summary
    if (-not $summary.Success) {
        Write-ProtoTail $text
        throw "The demo run wrote no NUnit summary line; fix the run before regenerating the demo. Log: $($run.Log)"
    }
    if ($run.ExitCode -eq 0) {
        throw "The demo run unexpectedly succeeded; its intentional failures were not exercised."
    }
    foreach ($name in $intentionalFailureNames) {
        if ($text -notmatch "Failed\s+$([regex]::Escape($name))\b") {
            Write-ProtoTail $text
            throw "The demo run did not exercise the intentional failure '$name'. Log: $($run.Log)"
        }
    }
    if (-not $run.Trace) { throw "ProtoTest did not write the expected trace. Log: $($run.Log)" }
    $trace = $run.Trace
    if ((Get-Item -LiteralPath $trace).LastWriteTimeUtc -lt $runStartedAtUtc.AddSeconds(-1)) {
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
}

$sets = if ($Set -eq "all") { @("lessons", "recipes", "demo") } else { @($Set) }

# Build once; every run after it skips the build instead of re-checking it per filter.
if (-not $NoBuild) {
    $project = Join-Path $repository "samples/Northstar.ProtoTest/Northstar.ProtoTest.csproj"
    Invoke-ProtoNative -Name "traces/build" -FilePath "dotnet" -ArgumentList @("build", $project, "--configuration", $Configuration) | Out-Null
    $NoBuild = [switch]$true
}
foreach ($name in $sets) {
    switch ($name) {
        "lessons" { Write-LessonTraces }
        "recipes" { Write-RecipeTraces }
        "demo" { Write-DemoTrace }
    }
}
