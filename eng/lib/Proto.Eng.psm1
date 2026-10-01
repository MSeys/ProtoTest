# Shared helpers for the commands under eng/commands. Each command imports this module once:
#   Import-Module (Join-Path $PSScriptRoot "../lib/Proto.Eng.psm1") -Force

$script:Repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "../.."))

function Get-ProtoRepository {
    # The repository root, two levels above this module.
    return $script:Repository
}

function Get-ProtoVersion {
    # The <Version> in Directory.Build.props, the one version every package and record carries.
    param([string]$Repository = $script:Repository)

    $props = Get-Content -LiteralPath (Join-Path $Repository "Directory.Build.props") -Raw
    $match = [regex]::Match($props, '<Version>\s*([^<]+?)\s*</Version>')
    if (-not $match.Success) { throw "Directory.Build.props does not declare a <Version>." }
    return $match.Groups[1].Value
}

function Test-ProtoVerbose {
    # Commands print one line per step and keep the full output in a log. -Verbose, PROTO_VERBOSE=1
    # or a GitHub Actions run streams everything instead.
    return $VerbosePreference -eq 'Continue' -or $env:PROTO_VERBOSE -eq '1' -or $env:GITHUB_ACTIONS -eq 'true'
}

function Write-ProtoTail {
    param([string]$Text, [int]$Lines = 40)

    $all = @($Text -split "\r?\n")
    $start = [Math]::Max(0, $all.Count - $Lines)
    $all[$start..($all.Count - 1)] | ForEach-Object { Write-Host $_ }
}

function Invoke-ProtoNative {
    # Runs a native command, keeps its output in a log under artifacts/logs, and prints one line. On a
    # nonzero exit it prints the last lines and the log path and throws; -AllowFailure only reports the exit.
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$FilePath,
        [string[]]$ArgumentList = @(),
        [string]$Log,
        [string]$Failure,
        [switch]$AllowFailure
    )

    if (-not $Log) {
        $Log = Join-Path $script:Repository ("artifacts/logs/" + ($Name -replace '[^A-Za-z0-9._-]', '-') + ".log")
    }
    New-Item -ItemType Directory -Path (Split-Path -Parent $Log) -Force | Out-Null

    $verbose = Test-ProtoVerbose
    $output = & $FilePath @ArgumentList 2>&1 | ForEach-Object {
        $line = "$_"
        if ($verbose) { Write-Host $line }
        $line
    }
    $exitCode = $LASTEXITCODE
    $text = @($output) -join [Environment]::NewLine
    Set-Content -LiteralPath $Log -Value $text -Encoding utf8

    if ($exitCode -ne 0) {
        # With -AllowFailure a nonzero exit can be the expected result; the caller prints the tail when it is not.
        if ($AllowFailure) {
            Write-Host "$Name exited $exitCode (log: $Log)"
        }
        else {
            if (-not $verbose) { Write-ProtoTail $text }
            $message = if ($Failure) { $Failure } else { "$Name failed with exit code $exitCode." }
            throw "$message Log: $Log"
        }
    }
    elseif (-not $verbose) {
        Write-Host "$Name passed (log: $Log)"
    }

    return [pscustomobject]@{ ExitCode = $exitCode; Text = $text; Log = $Log }
}

function Test-ProtoZeroTests {
    # The VSTest text a filter or a discovered project prints when it ran nothing.
    param([string]$Text)

    return $Text -match 'No test is available in' -or $Text -match 'No test matches the given testcase filter'
}

function Invoke-ProtoSampleRun {
    # One `dotnet test` run of the Northstar sample, optionally filtered and with the drills on. The
    # stale trace is deleted first, so the returned Trace is always this run's archive or $null.
    param(
        [string]$Configuration = "Release",
        [string]$Filter,
        [switch]$NoBuild,
        [switch]$Drills,
        [string]$Name = "sample"
    )

    $project = Join-Path $script:Repository "samples/Northstar.ProtoTest/Northstar.ProtoTest.csproj"
    $traceDir = Join-Path $script:Repository "samples/Northstar.ProtoTest/bin/$Configuration/net8.0/TestResults"
    $traceFilter = "prototest-*.prototrace"
    Remove-Item -Path (Join-Path $traceDir $traceFilter) -Force -ErrorAction SilentlyContinue

    $arguments = @("test", $project, "--configuration", $Configuration, "--verbosity", "minimal")
    if ($Filter) { $arguments += @("--filter", $Filter) }
    if ($NoBuild) { $arguments += "--no-build" }

    $previousDrills = $env:ProtoTest__Sample__Drills
    if ($Drills) { $env:ProtoTest__Sample__Drills = "true" }
    try {
        $run = Invoke-ProtoNative -Name "traces/$Name" -FilePath "dotnet" -ArgumentList $arguments -AllowFailure
    }
    finally {
        if ($null -eq $previousDrills) { Remove-Item Env:ProtoTest__Sample__Drills -ErrorAction SilentlyContinue }
        else { $env:ProtoTest__Sample__Drills = $previousDrills }
    }

    if ($Filter -and (Test-ProtoZeroTests $run.Text)) {
        throw "The filter '$Filter' matched zero tests; fix the filter or the test name."
    }

    $summary = [regex]::Match($run.Text, 'Failed:\s*(\d+),\s*Passed:\s*(\d+),\s*Skipped:\s*(\d+),\s*Total:\s*(\d+)')
    $trace = Get-ChildItem -Path $traceDir -Filter $traceFilter -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    return [pscustomobject]@{
        ExitCode = $run.ExitCode
        Text     = $run.Text
        Log      = $run.Log
        Summary  = $summary
        Trace    = if ($trace) { $trace.FullName } else { $null }
    }
}

Export-ModuleMember -Function Get-ProtoRepository, Get-ProtoVersion, Test-ProtoVerbose, Write-ProtoTail,
    Invoke-ProtoNative, Test-ProtoZeroTests, Invoke-ProtoSampleRun
