namespace ProtoTest.RunnerContract.Tests;

/// <summary>
/// The runner boundary outside cleanup: a setup failure, a parameterized test's rows and, on xUnit v2, a
/// ProtoTest test outside the collection that starts the host. Each run is a fresh process.
/// </summary>
[TestFixture]
public sealed class BoundaryContractTests
{
    public static IEnumerable<Runner> Runners() => Enum.GetValues<Runner>();

    [TestCaseSource(nameof(Runners))]
    public async Task SetupFailure_ShouldSurfaceTheOriginalErrorAndRecordOneFailedTrace(Runner runner)
    {
        // Act
        var run = await FixtureSuites.RunAsync(runner, "CleanupContract", new Scenario(SetupFails: true));

        // Assert
        var describe = CleanupContractTests.Describe(run);
        Assert.That(run.Results, Has.Count.EqualTo(1), describe);
        Assert.That(run.Trace, Is.Not.Null, describe);
        Assert.That(run.Trace!.Tests, Has.Count.EqualTo(1), describe);
        var traced = run.Trace.Tests[0];
        Assert.Multiple(() =>
        {
            Assert.That(run.ExitCode, Is.EqualTo(CleanupContractTests.FailedExitCode(runner)), describe);
            Assert.That(run.Results[0].Outcome, Is.EqualTo("Failed"), describe);
            Assert.That(run.Results[0].Message, Does.Contain("CONTRACT-SETUP"), describe);
            Assert.That(run.Markers, Is.Empty, "the body must not run after its setup failed. " + describe);
            Assert.That(traced.Outcome, Is.EqualTo("failed"), describe);
            Assert.That(traced.Failure?.ErrorMessage, Does.Contain("CONTRACT-SETUP"), describe);
        });
    }

    [TestCaseSource(nameof(Runners))]
    public async Task ParameterizedTest_ShouldReportAndTraceEachRow(Runner runner)
    {
        // Act
        var run = await FixtureSuites.RunAsync(runner, "RowsContract", new Scenario());

        // Assert
        var describe = CleanupContractTests.Describe(run);
        Assert.That(run.Trace, Is.Not.Null, describe);
        Assert.Multiple(() =>
        {
            Assert.That(run.ExitCode, Is.Zero, describe);
            Assert.That(run.Results, Has.Count.EqualTo(2), describe);
            Assert.That(run.Results.Select(result => result.Outcome), Is.All.EqualTo("Passed"), describe);
            Assert.That(run.Trace!.Tests, Has.Count.EqualTo(2), describe);
            Assert.That(run.Trace.Tests.Select(test => test.Outcome), Is.All.EqualTo("succeeded"), describe);
            Assert.That(run.Trace.Tests.Select(test => test.Name), Is.Unique, describe);
            Assert.That(run.Markers, Is.EquivalentTo(new[] { "row-1", "row-2" }), describe);
        });
    }

    [Test]
    public async Task MissingXunitCollectionFixture_ShouldFailEachProtoTestTest()
    {
        // Act
        var run = await FixtureSuites.RunAsync(Runner.Xunit, "MissingFixtureContract", new Scenario());

        // Assert: no collection started the host, so there is no trace, and no test may pass.
        var describe = CleanupContractTests.Describe(run);
        Assert.Multiple(() =>
        {
            Assert.That(run.ExitCode, Is.EqualTo(1), describe);
            Assert.That(run.Results, Has.Count.EqualTo(2), describe);
            Assert.That(run.Results.Select(result => result.Outcome), Is.All.EqualTo("Failed"), describe);
            Assert.That(
                run.Results.Select(result => result.Message),
                Is.All.Contains("ProtoHost is not initialized").And.All.Contains("collection fixture inherits from ProtoTestAssembly"),
                describe);
            Assert.That(run.Trace, Is.Null, describe);
        });
    }
}
