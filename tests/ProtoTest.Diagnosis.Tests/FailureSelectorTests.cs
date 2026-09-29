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

        var time = tests["Northstar.ProtoTest.FailureDrills.ARealWaitDoesNotCloseTheDueWindow"];
        var visibility = tests["Northstar.ProtoTest.FailureDrills.ABareStatusHidesWhatTheApplicationSaid"];
        var state = tests["Northstar.ProtoTest.FailureDrills.AnUnknownProjectIdIsTreatedAsMine"];
        var address = tests["Northstar.ProtoTest.FailureDrills.TheAddressWasHardcodedForOneMachine"];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(time.Failure!.Kind, Is.EqualTo("assert.json.shape"));
            Assert.That(visibility.Failure!.Kind, Is.EqualTo("assert.http.status"));
            Assert.That(state.Failure!.Kind, Is.EqualTo("assert.http.status"));
            Assert.That(address.Failure!.Kind, Is.EqualTo("test.execution"));

            // The shape check was judged on a protocol call, so the context package names that call; the
            // address drill threw before any call was recorded.
            Assert.That(time.CallAncestor(time.Failure!)!.Kind, Is.EqualTo("http.request"));
            Assert.That(address.CallAncestor(address.Failure!), Is.Null);
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
