namespace ProtoTest.Feedback.Tests;

using ProtoTest.Diagnosis;

/// <summary>
/// The digest the feedback channels consume: the diagnosis document read through the feedback entry
/// point, byte-identical for the same archive and carrying the numbers the report published.
/// </summary>
[TestFixture]
public sealed class FeedbackDigestTests
{
    [Test]
    public void ReadDigest_ShouldBeByteIdenticalForTheSameArchive()
    {
        var path = FeedbackFixtures.McpFixture("run-failed");

        var first = ProtoFeedback.DigestJson(ProtoFeedback.ReadDigest(path));
        var second = ProtoFeedback.DigestJson(ProtoFeedback.ReadDigest(path));

        Assert.That(second, Is.EqualTo(first));
    }

    [Test]
    public void Digest_ShouldCarryTheDiagnosisNumbersAndTheTraceReference()
    {
        var digest = ProtoFeedback.ReadDigest(FeedbackFixtures.McpFixture("run-failed"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(digest.DigestVersion, Is.EqualTo(ProtoDiagnosisDocument.CurrentDigestVersion));
            Assert.That(digest.TraceFile, Does.EndWith("run-failed.prototrace"));
            Assert.That(digest.Outcomes, Is.EqualTo(new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["failed"] = 1,
                ["succeeded"] = 1
            }));

            var failure = digest.Failures.Single();
            Assert.That(failure.Name, Is.EqualTo("orders match their shape"));
            Assert.That(failure.Rule, Is.EqualTo(ProtoDiagnosisRule.Assertion));

            Assert.That(digest.Coverage, Is.Not.Null);
            Assert.That(digest.Coverage!.Covered, Is.EqualTo(2));
            Assert.That(digest.Coverage.Total, Is.EqualTo(4));
        }
    }

    [Test]
    public void DigestJson_ShouldBeTheOneJsonDocument()
    {
        var json = ProtoFeedback.DigestJson(FeedbackFixtures.FailedDigest());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(json, Does.Contain("\"digestVersion\":\"1\""));
            Assert.That(json, Does.Contain("\"runId\":\"run-1\""));
            Assert.That(json, Does.Contain("\"traceFile\":\"TestResults/run-1.prototrace\""));
            Assert.That(json, Does.Contain("\"coverage\""));
        }
    }
}
