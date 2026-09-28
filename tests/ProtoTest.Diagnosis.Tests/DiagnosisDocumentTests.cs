namespace ProtoTest.Diagnosis.Tests;

using ProtoTest.Traces;

/// <summary>
/// The diagnosis document over real traces: each "explained" rule, the report it reads, the
/// determinism contract and the hard caps.
/// </summary>
[TestFixture]
public sealed class DiagnosisDocumentTests
{
    [Test]
    public async Task AssertionFailure_ShouldExplainWithTheRecordedMismatches()
    {
        await DiagnosisFixtures.WithTraceAsync(path =>
        {
            var document = ProtoDiagnosis.Read(path);
            var failure = document.Failures.Single();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(document.DigestVersion, Is.EqualTo("1"));
                Assert.That(document.TraceFormatVersion, Is.EqualTo("2.0"));
                Assert.That(document.TraceFile, Does.EndWith(".prototrace"));
                Assert.That(document.Outcomes["failed"], Is.EqualTo(1));
                Assert.That(document.Environment["runtime"], Does.Contain(".NET"));
                Assert.That(document.Environment.Keys, Contains.Item("os"));

                Assert.That(failure.Rule, Is.EqualTo(ProtoDiagnosisRule.Assertion));
                Assert.That(failure.Failure!.Kind, Is.EqualTo("assert.json.shape"));
                Assert.That(failure.Failure.ErrorType, Is.EqualTo("System.InvalidOperationException"));
                Assert.That(failure.Failure.SourceFile, Does.EndWith("DiagnosisFixtures.cs"));
                Assert.That(failure.Failure.SourceLine, Is.GreaterThan(0));
                Assert.That(failure.Failure.Subject, Is.Null);
                Assert.That(failure.UnexplainedReason, Is.Null);
                Assert.That(failure.Mismatches, Has.Count.EqualTo(1));
                Assert.That(failure.Mismatches[0].Path, Is.EqualTo("$.orderId"));
                Assert.That(failure.Mismatches[0].Expected!.Value.GetInt32(), Is.EqualTo(7));
                Assert.That(failure.Mismatches[0].Actual!.Value.GetInt32(), Is.EqualTo(42));
                Assert.That(failure.Artifacts, Has.Count.EqualTo(2));
                Assert.That(document.Coverage, Is.Null);
                Assert.That(document.CoverageAbsentReason, Does.Contain("No JSON report artifact"));
            }

            return Task.CompletedTask;
        }, path => DiagnosisFixtures.WriteAssertionFailureAsync(path));
    }

    [Test]
    public async Task OperationError_ShouldExplainWithTheOperationError()
    {
        await DiagnosisFixtures.WithTraceAsync(path =>
        {
            var document = ProtoDiagnosis.Read(path);
            var failure = document.Failures.Single();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(failure.Rule, Is.EqualTo(ProtoDiagnosisRule.OperationError));
                Assert.That(failure.Failure!.Kind, Is.EqualTo("http.request"));
                Assert.That(failure.Failure.Subject, Is.EqualTo(DiagnosisFixtures.RequestIdentifier));
                Assert.That(failure.Failure.ErrorType, Is.EqualTo("System.TimeoutException"));
                Assert.That(failure.Mismatches, Is.Empty);
            }

            return Task.CompletedTask;
        }, path => DiagnosisFixtures.WriteOperationErrorAsync(path));
    }

    [Test]
    public async Task RunnerFailure_ShouldExplainWithoutAnOperationError()
    {
        await DiagnosisFixtures.WithTraceAsync(path =>
        {
            var document = ProtoDiagnosis.Read(path);
            var failure = document.Failures.Single();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(failure.Rule, Is.EqualTo(ProtoDiagnosisRule.RunnerFailure));
                Assert.That(failure.Failure!.Kind, Is.EqualTo("test.execution"));
                Assert.That(failure.Failure.ErrorType, Is.EqualTo("NUnit.Failed"));
                Assert.That(failure.Failure.ErrorMessage, Does.Contain("without an exception"));
                Assert.That(failure.UnexplainedReason, Is.Null);
            }

            return Task.CompletedTask;
        }, path => DiagnosisFixtures.WriteRunnerFailureAsync(path));
    }

    [Test]
    public async Task Finding_ShouldExplainAPartialTest()
    {
        await DiagnosisFixtures.WithTraceAsync(path =>
        {
            var document = ProtoDiagnosis.Read(path);
            var failure = document.Failures.Single();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(failure.Outcome, Is.EqualTo("partial"));
                Assert.That(failure.Failure, Is.Null);
                Assert.That(failure.Rule, Is.EqualTo(ProtoDiagnosisRule.Finding));
                Assert.That(failure.Findings, Has.Count.EqualTo(1));
                Assert.That(failure.Findings[0].Message, Does.Contain("Teardown failed"));
                Assert.That(failure.Findings[0].Status, Is.EqualTo("error"));
                Assert.That(failure.Findings[0].Category, Is.EqualTo("Teardown"));
                Assert.That(document.Findings, Has.Count.EqualTo(1));
                Assert.That(document.Findings[0].TestId, Is.EqualTo(failure.TestId));
            }

            return Task.CompletedTask;
        }, path => DiagnosisFixtures.WriteFindingAsync(path));
    }

    [Test]
    public async Task GateFailure_ShouldBeTheRunVerdictWithoutAReport()
    {
        await DiagnosisFixtures.WithTraceAsync(path =>
        {
            var document = ProtoDiagnosis.Read(path);
            var gate = document.Gates.Single();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(document.Failures, Is.Empty);
                Assert.That(gate.Name, Is.EqualTo("coverage gate"));
                Assert.That(gate.Verdict, Is.EqualTo("failed"));
                Assert.That(gate.Message, Is.EqualTo("Coverage regressed below the agreed floor."));
                Assert.That(gate.Details, Has.Count.EqualTo(1));
                Assert.That(gate.Details[0], Does.Contain(DiagnosisFixtures.RequestIdentifier));
            }

            return Task.CompletedTask;
        }, path => DiagnosisFixtures.WriteGateFailureAsync(path));
    }

    [Test]
    public async Task GateFailure_ShouldReadTheGateFromTheReport()
    {
        await DiagnosisFixtures.WithTraceAsync(path =>
        {
            var document = ProtoDiagnosis.Read(path);
            var gate = document.Gates.Single();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(document.Coverage, Is.Not.Null);
                Assert.That(gate.Name, Is.EqualTo("coverage gate"));
                Assert.That(gate.Verdict, Is.EqualTo("failed"));
                Assert.That(gate.Details, Has.Count.EqualTo(2));
            }

            return Task.CompletedTask;
        }, path => DiagnosisFixtures.WriteGateFailureAsync(path, withReport: true));
    }

    [Test]
    public async Task ReportCoverage_ShouldComeFromTheEmbeddedReport()
    {
        await DiagnosisFixtures.WithTraceAsync(path =>
        {
            var document = ProtoDiagnosis.Read(path);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(document.Coverage, Is.Not.Null);
                Assert.That(document.Coverage!.Total, Is.EqualTo(2));
                Assert.That(document.Coverage.Covered, Is.EqualTo(1));
                Assert.That(document.Coverage.Uncovered, Is.EqualTo(1));
                Assert.That(document.Coverage.Percentage, Is.EqualTo(50));
                Assert.That(document.Coverage.ReportArtifact, Does.EndWith(".report.json"));
                Assert.That(document.CoverageAbsentReason, Is.Null);
            }

            return Task.CompletedTask;
        }, path => DiagnosisFixtures.WriteAssertionFailureAsync(path, withReport: true));
    }

    [Test]
    public async Task Digest_ShouldBeDeterministic()
    {
        await DiagnosisFixtures.WithTraceAsync(path =>
        {
            var first = ProtoDiagnosisJson.ToJson(ProtoDiagnosis.Read(path));
            var second = ProtoDiagnosisJson.ToJson(ProtoDiagnosis.Read(path));

            Assert.That(second, Is.EqualTo(first));
            return Task.CompletedTask;
        }, path => DiagnosisFixtures.WriteAssertionFailureAsync(path));
    }

    [Test]
    public async Task Caps_ShouldBoundTheDocumentAndTheContext()
    {
        await DiagnosisFixtures.WithTraceAsync(path =>
        {
            var archive = ProtoTraceArchive.Open(path);
            var document = ProtoDiagnosis.Read(archive);
            var failure = document.Failures.Single();
            var context = ProtoDiagnosis.ReadContext(archive, failure.TestId);
            var documentJson = ProtoDiagnosisJson.ToJson(document);
            var contextJson = ProtoDiagnosisJson.ToJson(context);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(failure.Mismatches, Has.Count.EqualTo(ProtoDiagnosis.MaxMismatches));
                Assert.That(failure.MismatchesTruncated, Is.True);
                Assert.That(failure.Findings, Has.Count.EqualTo(ProtoDiagnosis.MaxFindings));
                Assert.That(failure.FindingsTruncated, Is.True);
                Assert.That(failure.Artifacts, Has.Count.EqualTo(ProtoDiagnosis.MaxArtifacts));
                Assert.That(failure.ArtifactsTruncated, Is.True);
                Assert.That(failure.Failure!.ErrorMessage!.Length, Is.LessThan(ProtoDiagnosis.MaxErrorMessageCharacters + 100));
                Assert.That(failure.Failure.ErrorMessage, Does.Contain("truncated"));

                Assert.That(context.Sections, Has.Count.EqualTo(1));
                Assert.That(context.Sections[0].ContentTruncated, Is.True);
                Assert.That(context.Sections[0].Content!.Length, Is.LessThan(ProtoDiagnosis.MaxPreviewCharacters + 100));
                Assert.That(context.State, Has.Count.EqualTo(ProtoDiagnosis.MaxStateItems));
                Assert.That(context.StateTruncated, Is.True);
                Assert.That(context.Failure!.Attributes!["shape.mismatches"]!.Length, Is.LessThan(ProtoDiagnosis.MaxPreviewCharacters + 100));

                Assert.That(documentJson.Length, Is.LessThan(32 * 1024));
                Assert.That(contextJson.Length, Is.LessThan(32 * 1024));
            }

            return Task.CompletedTask;
        }, path => DiagnosisFixtures.WriteNoisyAsync(path, withReport: true));
    }
}
