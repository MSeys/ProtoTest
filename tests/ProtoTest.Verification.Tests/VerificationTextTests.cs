namespace ProtoTest.Verification.Tests;

using static VerificationReports;

/// <summary>
/// The text rendering of a verdict: the line a CI log reads, the findings, the coverage deltas and
/// the specification checks.
/// </summary>
[TestFixture]
public sealed class VerificationTextTests
{
    [Test]
    public void Write_ShouldRenderTheFailedVerdictWithItsDeltasAndSpecChecks()
    {
        var baseline = Report(Unit("Api", "OpenAPI", "/orders", covered: true));
        var current = Report(
            Unit("Api", "OpenAPI", "/orders", covered: false),
            Spec("Api", "openapi.json", "hash"));
        var verdict = ProtoVerification.Verify(baseline, current);
        using var writer = new StringWriter();

        ProtoVerificationText.Write(verdict, writer);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                writer.ToString(),
                Does.StartWith("ProtoTest verification failed: 1 failing, 0 warning(s), 0 info" + Environment.NewLine));
            Assert.That(writer.ToString(), Does.Contain("  fail regressed: Target 'Api' unit '/orders' in category 'OpenAPI'"));
            Assert.That(writer.ToString(), Does.Contain("coverage deltas:"));
            Assert.That(writer.ToString(), Does.Contain("  Api · OpenAPI: 1/1 covered -> 0/1 covered (-100 points, 1 regressed, 0 added uncovered)"));
            Assert.That(writer.ToString(), Does.Contain("spec checks:"));
            Assert.That(writer.ToString(), Does.Contain("  Api · OpenAPI: recorded"));
        }
    }

    [Test]
    public void Write_ShouldRenderAPassingVerdictWithoutFindings()
    {
        var report = Report(Unit("Api", "OpenAPI", "/orders", covered: true));
        var verdict = ProtoVerification.Verify(report, report);
        using var writer = new StringWriter();

        ProtoVerificationText.Write(verdict, writer);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(verdict.Failed, Is.False);
            Assert.That(
                writer.ToString(),
                Does.StartWith("ProtoTest verification passed: 0 failing, 0 warning(s), 0 info"));
            Assert.That(writer.ToString(), Does.Not.Contain("fail "));
        }
    }

    [Test]
    public void Write_ShouldRenderAFailedRunGateFinding()
    {
        var report = Report(FailedGate("coverage gate", "Coverage regressed below the agreed floor."));
        var verdict = ProtoVerification.Verify(Report(), report);
        using var writer = new StringWriter();

        ProtoVerificationText.Write(verdict, writer);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(verdict.Failed, Is.True);
            Assert.That(
                writer.ToString(),
                Does.Contain("  fail gate-failed: Run gate 'coverage gate' failed: Coverage regressed below the agreed floor."));
        }
    }
}
