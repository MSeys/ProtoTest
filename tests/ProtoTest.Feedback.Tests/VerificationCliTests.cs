namespace ProtoTest.Feedback.Tests;

using ProtoTest.Cli;

/// <summary>
/// The `prototest verify` verb: the verdict text, the exit code and the one annotation per failing
/// finding.
/// </summary>
[TestFixture]
public sealed class VerificationCliTests
{
    [Test]
    public void Verify_ShouldFailWithOneAnnotationPerFailingFinding()
    {
        var directory = FeedbackFixtures.NewTempDirectory("verify-cli");
        try
        {
            var (baseline, current) = FeedbackFixtures.RegressedReports(directory);
            using var output = new StringWriter();
            using var error = new StringWriter();

            var exit = CliHost.Run(["verify", baseline, current], output, error);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(exit, Is.EqualTo(1));
                Assert.That(output.ToString(), Does.StartWith("::error::regressed: "));
                Assert.That(output.ToString(), Does.Contain("was covered in the baseline and is uncovered now"));
                Assert.That(output.ToString(), Does.Contain("ProtoTest verification failed: 1 failing, 0 warning(s), 0 info"));
                Assert.That(
                    output.ToString(),
                    Does.Contain("Northstar:Api · OpenAPI: 1/1 covered -> 0/1 covered (-100 points, 1 regressed, 0 added uncovered)"));
                Assert.That(output.ToString().Split(Environment.NewLine).Count(line => line.StartsWith("::error", StringComparison.Ordinal)), Is.EqualTo(1));
                Assert.That(error.ToString(), Is.Empty);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void Verify_ShouldPassTwoEqualReportsWithoutAnnotations()
    {
        var directory = FeedbackFixtures.NewTempDirectory("verify-cli");
        try
        {
            var baseline = FeedbackFixtures.WriteReport(
                Path.Combine(directory, "baseline.json"),
                FeedbackFixtures.Unit("Northstar:Api", "OpenAPI", "GET /api/v1/orders", covered: true));
            var current = FeedbackFixtures.WriteReport(
                Path.Combine(directory, "current.json"),
                FeedbackFixtures.Unit("Northstar:Api", "OpenAPI", "GET /api/v1/orders", covered: true));
            using var output = new StringWriter();
            using var error = new StringWriter();

            var exit = CliHost.Run(["verify", baseline, current], output, error);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(exit, Is.EqualTo(0));
                Assert.That(output.ToString(), Does.Contain("ProtoTest verification passed: 0 failing"));
                Assert.That(output.ToString(), Does.Not.Contain("::error"));
                Assert.That(error.ToString(), Is.Empty);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void Verify_ShouldNameAMissingReport()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exit = CliHost.Run(["verify", "baseline.json", "current.json"], output, error);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exit, Is.EqualTo(1));
            Assert.That(error.ToString(), Does.Contain("baseline.json"));
        }
    }

    [Test]
    public void Verify_ShouldNameAFileThatIsNotAReport()
    {
        var directory = FeedbackFixtures.NewTempDirectory("verify-cli");
        try
        {
            var baseline = FeedbackFixtures.RegressedReports(directory).Baseline;
            var path = Path.Combine(directory, "current.json");
            File.WriteAllText(path, "[]");
            using var output = new StringWriter();
            using var error = new StringWriter();

            var exit = CliHost.Run(["verify", baseline, path], output, error);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(exit, Is.EqualTo(1));
                Assert.That(error.ToString(), Does.Contain("Could not verify"));
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void Verify_ShouldPrintUsageWithoutBothReports()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exit = CliHost.Run(["verify", "baseline.json"], output, error);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exit, Is.EqualTo(1));
            Assert.That(error.ToString(), Does.Contain("prototest verify <baseline> <current>"));
        }
    }
}
