namespace ProtoTest.Diagnosis.Tests;

using ProtoTest.Traces;

/// <summary>
/// The context package one agent receives for a failure: the ancestors and the call, the section
/// previews, the embedded source snippet, artifacts and their content on request, the state the
/// operation changed and the report rows it touched.
/// </summary>
[TestFixture]
public sealed class DiagnosisContextTests
{
    [Test]
    public async Task Context_ShouldCarryTheAncestorsSectionsSourceAndState()
    {
        await DiagnosisFixtures.WithTraceAsync(path =>
        {
            var archive = ProtoTraceArchive.Open(path);
            var document = ProtoDiagnosis.Read(archive);
            var failure = document.Failures.Single();
            var context = ProtoDiagnosis.ReadContext(archive, failure.TestId);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(context.RunId, Is.EqualTo(document.RunId));
                Assert.That(context.Rule, Is.EqualTo(ProtoDiagnosisRule.Assertion));
                Assert.That(context.Failure!.Kind, Is.EqualTo("assert.json.shape"));
                Assert.That(context.Failure.Attributes, Is.Not.Null);
                Assert.That(context.Failure.Attributes!["shape.mismatch_count"], Is.EqualTo("1"));
                Assert.That(context.Call!.Kind, Is.EqualTo("http.request"));
                Assert.That(
                    context.Ancestors.Select(ancestor => ancestor.Kind),
                    Is.EqualTo(new[] { "http.request", "test.execution" }));
                Assert.That(context.AncestorsTruncated, Is.False);

                Assert.That(context.Sections, Has.Count.EqualTo(1));
                Assert.That(context.Sections[0].Kind, Is.EqualTo("checks"));
                Assert.That(context.Sections[0].Items[0].Tone, Is.EqualTo("error"));

                Assert.That(context.Source, Is.Not.Null);
                Assert.That(context.Source!.File, Does.EndWith("DiagnosisFixtures.cs"));
                Assert.That(context.Source.StartLine, Is.LessThanOrEqualTo(context.Source.Line));
                Assert.That(context.Source.EndLine, Is.GreaterThanOrEqualTo(context.Source.Line));
                Assert.That(context.Source.Lines.Select(line => line.Number), Is.Ordered.Ascending);
                Assert.That(context.SourceAbsentReason, Is.Null);

                Assert.That(context.State, Has.Count.EqualTo(1));
                Assert.That(context.State[0].Id, Is.EqualTo("order:order-1"));
                Assert.That(context.State[0].Changes, Has.Count.EqualTo(1));
                Assert.That(context.State[0].Changes[0].Change, Is.EqualTo("changed"));
                Assert.That(context.StateAbsentReason, Is.Null);

                // The response artifact was attached under the failing operation; the payload was added
                // at the test level after it closed, so it has no operation link.
                var response = context.Artifacts.Single(artifact => artifact.Name.EndsWith("response.json"));
                Assert.That(response.OperationId, Is.EqualTo(context.Failure.SpanId));
                Assert.That(response.Content, Is.Null);
                Assert.That(context.Artifacts.Single(artifact => artifact.Name.EndsWith("payload.txt")).OperationId, Is.Null);
            }

            return Task.CompletedTask;
        }, path => DiagnosisFixtures.WriteAssertionFailureAsync(path));
    }

    [Test]
    public async Task Context_ShouldReadArtifactContentOnlyOnRequest()
    {
        await DiagnosisFixtures.WithTraceAsync(path =>
        {
            var archive = ProtoTraceArchive.Open(path);
            var failure = ProtoDiagnosis.Read(archive).Failures.Single();
            var context = ProtoDiagnosis.ReadContext(archive, failure.TestId, includeArtifactContent: true);

            var response = context.Artifacts.Single(artifact => artifact.Name.EndsWith("response.json"));
            var payload = context.Artifacts.Single(artifact => artifact.Name.EndsWith("payload.txt"));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(response.Content, Is.EqualTo("""{"orderId":42}"""));
                Assert.That(response.ContentTruncated, Is.False);
                Assert.That(payload.Content, Is.Not.Null);
                Assert.That(payload.Content!.Length, Is.EqualTo(ProtoDiagnosis.MaxArtifactPreviewBytes));
                Assert.That(payload.ContentTruncated, Is.True);
            }

            return Task.CompletedTask;
        }, path => DiagnosisFixtures.WriteAssertionFailureAsync(path));
    }

    [Test]
    public async Task Context_ShouldMatchTheCoverageRowTheOperationTouched()
    {
        await DiagnosisFixtures.WithTraceAsync(path =>
        {
            var archive = ProtoTraceArchive.Open(path);
            var failure = ProtoDiagnosis.Read(archive).Failures.Single();
            var context = ProtoDiagnosis.ReadContext(archive, failure.TestId);
            var row = context.Report.CoverageRows.Single();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(context.Report.Coverage, Is.Not.Null);
                Assert.That(context.Report.AbsentReason, Is.Null);
                Assert.That(row.Identifier, Is.EqualTo(DiagnosisFixtures.RequestIdentifier));
                Assert.That(row.Target, Is.EqualTo("Northstar:Api"));
                Assert.That(row.IsCovered, Is.True);
                Assert.That(row.Count, Is.EqualTo(2));
                Assert.That(context.Report.CoverageRowsTruncated, Is.False);
            }

            return Task.CompletedTask;
        }, path => DiagnosisFixtures.WriteAssertionFailureAsync(path, withReport: true));
    }

    [Test]
    public async Task Context_ShouldCarryTheEntityStateAnOperationErrorActedOn()
    {
        await DiagnosisFixtures.WithTraceAsync(path =>
        {
            var archive = ProtoTraceArchive.Open(path);
            var failure = ProtoDiagnosis.Read(archive).Failures.Single();
            var context = ProtoDiagnosis.ReadContext(archive, failure.TestId);
            var client = context.State.Single(item => item.Id == "client:Rest:Orders");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(client.Kind, Is.EqualTo("client"));
                Assert.That(client.State["resource.state"], Is.EqualTo("registered"));
                Assert.That(context.Report.AbsentReason, Does.Contain("No JSON report artifact"));
            }

            return Task.CompletedTask;
        }, path => DiagnosisFixtures.WriteOperationErrorAsync(path));
    }
}
