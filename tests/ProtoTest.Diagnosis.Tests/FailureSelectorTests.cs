namespace ProtoTest.Diagnosis.Tests;

using ProtoTest.Traces;

/// <summary>
/// The one failure selector: the CLI, the MCP tools and the viewer must pick the same operation for a
/// failing test. The committed demo trace is pinned here and in the viewer's own analysis test.
/// </summary>
[TestFixture]
public sealed class FailureSelectorTests
{
    [Test]
    public void DemoTrace_ShouldSelectTheFailuresTheViewerPins()
    {
        var archive = ProtoTraceArchive.Open(DiagnosisFixtures.RepositoryFile("viewer", "public", "demos", "prototest-demo.prototrace"));
        var tests = archive.Tests.ToDictionary(test => test.Name, StringComparer.Ordinal);

        var webhook = tests["ProtoTest.Demo.DiagnosticsShowcase.AFailedOperationRecordsItsDiagnosticsAndTheRunContinues"];
        var captured = tests["ProtoTest.Demo.DiagnosticsShowcase.ShapeMismatchesAreCapturedWithoutFailingTheRun"];
        var dashboard = tests["ProtoTest.Demo.DiagnosticsShowcase.TheDashboardNeverShowsAnotherTenantsPlan"];
        var organization = tests["ProtoTest.Demo.DiagnosticsShowcase.TheOrganizationReportsItsPlanAndProjectCount"];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(webhook.Failure!.Kind, Is.EqualTo("northstar.webhook.deliver"));
            Assert.That(captured.Failure!.Kind, Is.EqualTo("assert.json.shape"));
            Assert.That(dashboard.Failure!.Kind, Is.EqualTo("assert.web"));
            Assert.That(organization.Failure!.Kind, Is.EqualTo("assert.json.shape"));

            // The shape checks were judged on a protocol call, so the context package names that call.
            Assert.That(tests["ProtoTest.Demo.DiagnosticsShowcase.ShapeMismatchesAreCapturedWithoutFailingTheRun"].CallAncestor(captured.Failure!)!.Kind, Is.EqualTo("http.request"));
            Assert.That(tests["ProtoTest.Demo.DiagnosticsShowcase.TheOrganizationReportsItsPlanAndProjectCount"].CallAncestor(organization.Failure!)!.Kind, Is.EqualTo("http.request"));
            Assert.That(dashboard.CallAncestor(dashboard.Failure!), Is.Null);
        }
    }

    [Test]
    public void McpFixtures_ShouldSelectTheFailureAndTheGreenRun()
    {
        var failed = ProtoTraceArchive.Open(DiagnosisFixtures.McpFixture("run-failed"));
        var passed = ProtoTraceArchive.Open(DiagnosisFixtures.McpFixture("run-passed"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(failed.Tests.Single(test => !test.Succeeded).Failure!.Kind, Is.EqualTo("assert.json.shape"));
            Assert.That(passed.Tests.Single().Failure, Is.Null);
        }
    }

    [Test]
    public async Task AssertionFixture_ShouldSelectTheCheckNotThePhaseSpan()
    {
        await DiagnosisFixtures.WithTraceAsync(path =>
        {
            var test = ProtoTraceArchive.Open(path).Tests.Single();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(test.Failure!.Kind, Is.EqualTo("assert.json.shape"));
                Assert.That(test.Ancestors(test.Failure!), Has.Count.EqualTo(2));
                Assert.That(test.Ancestors(test.Failure!)[0].Kind, Is.EqualTo("http.request"));
            }

            return Task.CompletedTask;
        }, path => DiagnosisFixtures.WriteAssertionFailureAsync(path));
    }

    [Test]
    public async Task CancelledChild_ShouldNotOutrankTheFailedOperation()
    {
        await DiagnosisFixtures.WithTraceAsync(path =>
        {
            var test = ProtoTraceArchive.Open(path).Tests.Single();
            var cancelled = test.Operations.Single(operation => operation.Status == "cancelled");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(test.Failure!.Failed, Is.True, "a cancelled child must not hide the failure above it");
                Assert.That(test.Failure!.SpanId, Is.Not.EqualTo(cancelled.SpanId));
                Assert.That(cancelled.HasError, Is.True, "the cancelled child recorded the cancellation as an error");
            }

            return Task.CompletedTask;
        }, path => DiagnosisFixtures.WriteCancelledChildAsync(path));
    }

    [Test]
    public async Task CallAncestor_ShouldFindTheNestedProtocolCall()
    {
        await DiagnosisFixtures.WithTraceAsync(path =>
        {
            var test = ProtoTraceArchive.Open(path).Tests.Single();
            var call = test.CallAncestor(test.Failure!);

            Assert.That(call!.Kind, Is.EqualTo("http.request"));
            return Task.CompletedTask;
        }, path => DiagnosisFixtures.WriteOperationErrorAsync(path));
    }
}
