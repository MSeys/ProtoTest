[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repositoryRoot "samples/ProtoTest.Demo/ProtoTest.Demo.csproj"
$trace = Join-Path $repositoryRoot "samples/ProtoTest.Demo/bin/$Configuration/net8.0/TestResults/ProtoTest.Demo/prototest-demo.prototrace"
$destinationDirectory = Join-Path $repositoryRoot "viewer/public/demos"
$destination = Join-Path $destinationDirectory "prototest-demo.prototrace"
$previousFailureMode = [Environment]::GetEnvironmentVariable("ProtoTest__Demo__IncludeFailure", "Process")
$runStartedAtUtc = [DateTime]::UtcNow

try {
    $env:ProtoTest__Demo__IncludeFailure = "true"
    & dotnet test $project --configuration $Configuration --no-restore --logger "console;verbosity=minimal"
    $testExitCode = $LASTEXITCODE

    if ($testExitCode -eq 0) {
        throw "The demo run unexpectedly succeeded; its intentional failure was not exercised."
    }
    if (-not (Test-Path -LiteralPath $trace)) {
        throw "The expected ProtoTrace archive was not produced at '$trace'."
    }

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
            $embedded = $manifest.sources.$path
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

    $failedTests = @($testGroups | Where-Object { $_.resource.attributes.testOutcome -eq "failed" })
    # The demo grows with every journey, so the exact totals change: require at least the tests that
    # existed when this gate was written, allow new succeeded or partial tests, and require the only
    # failure to be the one intentional shape mismatch.
    $minimumTestCount = 40
    $intentionalFailure = "TheOrganizationReportsItsPlanAndProjectCount"
    if ($testGroups.Count -lt $minimumTestCount) {
        throw "Expected at least $minimumTestCount test resources in spans.json, but found $($testGroups.Count)."
    }

    $unknownTests = @($testGroups | Where-Object {
        $_.resource.attributes.testOutcome -notin @("succeeded", "partial", "failed")
    })
    if ($unknownTests.Count -gt 0) {
        throw "Expected every test resource to report a succeeded, partial or failed outcome, but $($unknownTests.Count) did not."
    }

    $intentionalFailures = @($failedTests | Where-Object {
        $_.resource.attributes.testMethod -eq $intentionalFailure
    })
    if ($intentionalFailures.Count -ne 1) {
        throw "Expected exactly one intentional failure '$intentionalFailure' in spans.json, but found $($intentionalFailures.Count)."
    }

    $unexpectedFailures = @($failedTests | Where-Object {
        $_.resource.attributes.testMethod -ne $intentionalFailure
    })
    if ($unexpectedFailures.Count -gt 0) {
        $names = @($unexpectedFailures | ForEach-Object { $_.resource.attributes.testMethod }) -join ", "
        throw "Expected no failures other than '$intentionalFailure', but found: $names."
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

    # Evidence is span events now: findings, observations and attachments.
    foreach ($record in @("finding", "observation", "attachment")) {
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

    New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
    Copy-Item -LiteralPath $trace -Destination $destination -Force
    Write-Host "Updated viewer demo trace: $destination"
}
finally {
    [Environment]::SetEnvironmentVariable(
        "ProtoTest__Demo__IncludeFailure",
        $previousFailureMode,
        "Process")
}
