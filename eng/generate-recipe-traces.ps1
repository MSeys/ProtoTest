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
$destination = Join-Path $repository "viewer/public/demos/recipes"

$recipes = @(
    @{ Name = "rest-graphql"; Filter = "FullyQualifiedName~PlatformJourney.RestWritesAreVisibleThroughGraphQL" },
    @{ Name = "rest-database"; Filter = "FullyQualifiedName~DomainAccessJourney.AProjectCreatedThroughRestIsCommittedToTheDatabase" },
    @{ Name = "workbook"; Filter = "FullyQualifiedName~SheetsJourney.TheMonthlyReport_ShouldMatchItsModel" }
)

New-Item -ItemType Directory -Force -Path $destination | Out-Null

foreach ($recipe in $recipes) {
    $arguments = @(
        "test", $project,
        "--configuration", $Configuration,
        "--filter", $recipe.Filter,
        "--verbosity", "minimal"
    )
    if ($NoBuild) { $arguments += "--no-build" }

    # Both files come from this run: the destination is deleted first so a stale committed trace can
    # never pass the copy, and the source is deleted so a run that writes no trace fails instead of
    # reusing one. A filter that matches no test fails loudly (the guard eng/test.ps1 uses).
    $destinationFile = Join-Path $destination "$($recipe.Name).prototrace"
    Remove-Item -LiteralPath $destinationFile -Force -ErrorAction SilentlyContinue
    Remove-Item -Path (Join-Path $traceDir $traceFilter) -Force -ErrorAction SilentlyContinue

    $output = & dotnet @arguments 2>&1
    $output | ForEach-Object { Write-Host $_ }
    if ($LASTEXITCODE -ne 0) { throw "Trace generation failed for $($recipe.Name)." }

    $text = $output -join [Environment]::NewLine
    if ($text -match 'No test is available in' -or $text -match 'No test matches the given testcase filter') {
        throw "The recipe filter '$($recipe.Filter)' matched zero tests; fix the filter or the test name."
    }
    $traceFiles = @(Get-ChildItem -Path $traceDir -Filter $traceFilter -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTimeUtc -Descending)
    if ($traceFiles.Count -eq 0) { throw "ProtoTest did not write the expected trace: $(Join-Path $traceDir $traceFilter)" }
    $trace = $traceFiles[0].FullName

    Copy-Item -LiteralPath $trace -Destination $destinationFile -Force
    Write-Host "Generated $($recipe.Name).prototrace"
}
