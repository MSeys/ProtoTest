param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"
$repository = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repository "samples/ProtoTest.Demo/ProtoTest.Demo.csproj"
$trace = Join-Path $repository "samples/ProtoTest.Demo/bin/$Configuration/net8.0/TestResults/ProtoTest.Demo/prototest-demo.prototrace"
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

    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "Trace generation failed for $($recipe.Name)." }
    if (-not (Test-Path -LiteralPath $trace)) { throw "ProtoTest did not write the expected trace: $trace" }

    Copy-Item -LiteralPath $trace -Destination (Join-Path $destination "$($recipe.Name).prototrace") -Force
    Write-Host "Generated $($recipe.Name).prototrace"
}
