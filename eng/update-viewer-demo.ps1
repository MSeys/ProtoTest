[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repositoryRoot "samples/ProtoTest.Demo/ProtoTest.Demo.csproj"
$trace = Join-Path $repositoryRoot "samples/ProtoTest.Demo/bin/$Configuration/net8.0/TestResults/ProtoTest.Demo/control-plane.prototrace"
$destinationDirectory = Join-Path $repositoryRoot "viewer/public/demos"
$destination = Join-Path $destinationDirectory "shape-mismatch.prototrace"
$previousFailureMode = [Environment]::GetEnvironmentVariable("PROTOTEST_DEMO_INCLUDE_FAILURE", "Process")
$runStartedAtUtc = [DateTime]::UtcNow

try {
    $env:PROTOTEST_DEMO_INCLUDE_FAILURE = "1"
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

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($trace)
    try {
        $runEntry = $archive.GetEntry("run.json")
        if ($null -eq $runEntry) { throw "The generated archive has no run.json entry." }
        $reader = [System.IO.StreamReader]::new($runEntry.Open())
        try { $run = $reader.ReadToEnd() | ConvertFrom-Json -Depth 100 }
        finally { $reader.Dispose() }
    }
    finally {
        $archive.Dispose()
    }

    $tests = @($run.tests)
    $failedTests = @($tests | Where-Object outcome -eq "Failed")
    $succeededTests = @($tests | Where-Object outcome -eq "Succeeded")
    $partialTests = @($tests | Where-Object outcome -eq "Partial")
    if ($tests.Count -ne 12 -or $succeededTests.Count -ne 9 -or $partialTests.Count -ne 2 -or $failedTests.Count -ne 1 -or
        $failedTests[0].methodName -ne "IntentionalFailureShowsFailedTestShapeMismatchAndTeardown") {
        throw "Expected 9 successful tests, 2 partial diagnostic tests and only the intentional shape-mismatch failure in the viewer trace."
    }

    New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
    Copy-Item -LiteralPath $trace -Destination $destination -Force
    Write-Host "Updated viewer demo trace: $destination"
}
finally {
    [Environment]::SetEnvironmentVariable(
        "PROTOTEST_DEMO_INCLUDE_FAILURE",
        $previousFailureMode,
        "Process")
}
