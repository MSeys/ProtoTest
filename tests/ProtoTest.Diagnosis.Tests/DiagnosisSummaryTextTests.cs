namespace ProtoTest.Diagnosis.Tests;

using ProtoTest.Cli;

/// <summary>
/// The plain-text rendering and the CLI that prints it: one diagnosis model, rendered twice, so a CI
/// log says the same thing the JSON document does.
/// </summary>
[TestFixture]
public sealed class DiagnosisSummaryTextTests
{
    [Test]
    public async Task Summary_ShouldPrintTheCauseAndTheMismatches()
    {
        await DiagnosisFixtures.WithTraceAsync(path =>
        {
            using var writer = new StringWriter();
            ProtoTraceSummaryText.Write(ProtoDiagnosis.Read(path), writer);
            var summary = writer.ToString();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(summary, Does.Contain("ProtoTest trace 2.0"));
                Assert.That(summary, Does.Contain("1 tests · 1 failed"));
                Assert.That(summary, Does.Contain("FAILED orders match their shape"));
                Assert.That(summary, Does.Contain("cause: assertion (1 mismatch)"));
                Assert.That(summary, Does.Contain("mismatch: $.orderId: expected 7, actual 42"));
                Assert.That(summary, Does.Contain("at tests/ProtoTest.Diagnosis.Tests/DiagnosisFixtures.cs"));
                Assert.That(summary, Does.Contain("assert.json.shape"));
            }

            return Task.CompletedTask;
        }, path => DiagnosisFixtures.WriteAssertionFailureAsync(path));
    }

    [Test]
    public async Task Summary_ShouldPrintTheFailedGate()
    {
        await DiagnosisFixtures.WithTraceAsync(path =>
        {
            using var writer = new StringWriter();
            ProtoTraceSummaryText.Write(ProtoDiagnosis.Read(path), writer);
            var summary = writer.ToString();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(summary, Does.Contain("RUN GATE FAILED coverage gate: Coverage regressed"));
                Assert.That(summary, Does.Contain(DiagnosisFixtures.RequestIdentifier));
                Assert.That(summary, Does.Not.Contain("All green."));
            }

            return Task.CompletedTask;
        }, path => DiagnosisFixtures.WriteGateFailureAsync(path));
    }

    [Test]
    public async Task Summary_ShouldUsePlainHyphens()
    {
        await DiagnosisFixtures.WithTraceAsync(path =>
        {
            using var writer = new StringWriter();
            ProtoTraceSummaryText.Write(ProtoDiagnosis.Read(path), writer);
            var summary = writer.ToString();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(summary, Does.Contain("Z - "), "the recorded time range reads as a plain hyphen");
                Assert.That(summary, Does.Not.Contain("\u2013").And.Not.Contain("\u2014"), "the CLI's own output carries no em or en dash");
            }

            return Task.CompletedTask;
        }, path => DiagnosisFixtures.WriteAssertionFailureAsync(path));
    }

    [Test]
    public void Summary_ShouldSayAllGreenForAGreenRun()
    {
        var document = ProtoDiagnosis.Read(DiagnosisFixtures.McpFixture("run-passed"));

        using var writer = new StringWriter();
        ProtoTraceSummaryText.Write(document, writer);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(writer.ToString(), Does.Contain("1 succeeded"));
            Assert.That(writer.ToString(), Does.Contain("All green."));
        }
    }

    [Test]
    public async Task Cli_ShouldPrintTheSameSummary()
    {
        await DiagnosisFixtures.WithTraceAsync(path =>
        {
            using var output = new StringWriter();
            using var error = new StringWriter();
            var exit = CliHost.Run(["summary", path], output, error);

            using var rendered = new StringWriter();
            ProtoTraceSummaryText.Write(ProtoDiagnosis.Read(path), rendered);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(exit, Is.EqualTo(0));
                Assert.That(output.ToString(), Is.EqualTo(rendered.ToString()));
                Assert.That(error.ToString(), Is.Empty);
            }

            return Task.CompletedTask;
        }, path => DiagnosisFixtures.WriteAssertionFailureAsync(path));
    }

    [Test]
    public async Task Cli_ShouldPrintTheOperationIdentityOnceWhenTheNameMatchesTheKind()
    {
        await DiagnosisFixtures.WithTraceAsync(path =>
        {
            using var output = new StringWriter();
            using var error = new StringWriter();
            var exit = CliHost.Run(["summary", path], output, error);
            var summary = output.ToString();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(exit, Is.EqualTo(0));
                Assert.That(summary, Does.Contain("  assert.json.shape · failed"));
                Assert.That(summary, Does.Not.Contain("assert.json.shape assert.json.shape"));
                Assert.That(error.ToString(), Is.Empty);
            }

            return Task.CompletedTask;
        }, path => DiagnosisFixtures.WriteAssertionFailureAsync(path));
    }

    [Test]
    public async Task Cli_ShouldPrintTheKindAndNameWhenTheyDiffer()
    {
        await DiagnosisFixtures.WithTraceAsync(path =>
        {
            using var output = new StringWriter();
            using var error = new StringWriter();
            var exit = CliHost.Run(["summary", path], output, error);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(exit, Is.EqualTo(0));
                Assert.That(output.ToString(), Does.Contain("  http.request REST · GET orders · failed"));
                Assert.That(error.ToString(), Is.Empty);
            }

            return Task.CompletedTask;
        }, path => DiagnosisFixtures.WriteOperationErrorAsync(path));
    }
}
