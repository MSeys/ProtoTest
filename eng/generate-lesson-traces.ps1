param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"
$repository = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repository "samples/Northstar.ProtoTest/Northstar.ProtoTest.csproj"
$trace = Join-Path $repository "samples/Northstar.ProtoTest/bin/$Configuration/net8.0/TestResults/Northstar.ProtoTest/northstar.prototrace"
$destination = Join-Path $repository "docs/static/lessons"

# One filtered run per lesson trace, so the file a lesson embeds holds exactly the test the lesson
# names. A drill is expected to fail and needs ProtoTest:Sample:Drills; its paired fix must pass; the
# capability-gated broker journey must skip by itself. The tests, filters and order are fixed; the run
# id and timestamps vary with the run, the same limit the recipe traces have.
$traces = @(
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
    @{ Name = "l4-coverage"; Filter = "FullyQualifiedName~PlatformJourney.RestWritesAreVisibleThroughGraphQL"; Expect = "Passed" },
    @{ Name = "l4-artifacts"; Filter = "FullyQualifiedName~SheetsJourney.TheMonthlyReport_ShouldMatchItsModel"; Expect = "Passed" }
)

New-Item -ItemType Directory -Force -Path $destination | Out-Null

foreach ($item in $traces) {
    $arguments = @(
        "test", $project,
        "--configuration", $Configuration,
        "--filter", $item.Filter,
        "--verbosity", "minimal"
    )
    if ($NoBuild) { $arguments += "--no-build" }

    # Both files come from this run: the destination is deleted first so a stale committed trace can
    # never pass the copy, and the source is deleted so a run that writes no trace fails instead of
    # reusing one. A filter that matches no test fails loudly (the guard eng/test.ps1 uses).
    $destinationFile = Join-Path $destination "$($item.Name).prototrace"
    Remove-Item -LiteralPath $destinationFile, $trace -Force -ErrorAction SilentlyContinue

    $previousDrills = $env:ProtoTest__Sample__Drills
    if ($item.Drills) { $env:ProtoTest__Sample__Drills = "true" }
    try {
        $output = & dotnet @arguments 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        if ($null -eq $previousDrills) { Remove-Item Env:ProtoTest__Sample__Drills -ErrorAction SilentlyContinue }
        else { $env:ProtoTest__Sample__Drills = $previousDrills }
    }

    $text = $output -join [Environment]::NewLine
    if ($text -match 'No test is available in' -or $text -match 'No test matches the given testcase filter') {
        throw "The lesson filter '$($item.Filter)' matched zero tests; fix the filter or the test name."
    }

    $summary = [regex]::Match($text, 'Failed:\s*(\d+),\s*Passed:\s*(\d+),\s*Skipped:\s*(\d+),\s*Total:\s*(\d+)')
    $matched = $summary.Success
    if ($matched) {
        $matched = [int]$summary.Groups[1].Value -eq $(if ($item.Expect -eq "Failed") { 1 } else { 0 })
        $matched = $matched -and [int]$summary.Groups[2].Value -eq $(if ($item.Expect -eq "Passed") { 1 } else { 0 })
        $matched = $matched -and [int]$summary.Groups[3].Value -eq $(if ($item.Expect -eq "Skipped") { 1 } else { 0 })
        $matched = $matched -and [int]$summary.Groups[4].Value -eq 1
        $matched = $matched -and $exitCode -eq $(if ($item.Expect -eq "Failed") { 1 } else { 0 })
    }

    if (-not $matched) {
        # The run output is long; keep it for the mismatch instead of streaming it on every success.
        $output | ForEach-Object { Write-Host $_ }
        throw "The run for $($item.Name) did not $($item.Expect.ToLowerInvariant()) its one test (exit $exitCode)."
    }

    if (-not (Test-Path -LiteralPath $trace)) { throw "ProtoTest did not write the expected trace: $trace" }

    Copy-Item -LiteralPath $trace -Destination $destinationFile -Force
    Write-Host "Wrote $($item.Name).prototrace ($($item.Expect.ToLowerInvariant())): $($summary.Value.Trim())"
}

Write-Host "Wrote $($traces.Count) lesson traces to $destination"
