namespace ProtoTest.Verification.Tests;

using ProtoTest.Core;
using static VerificationReports;

[TestFixture]
public sealed class SpecIntegrityTests
{
    [Test]
    public void Verify_ShouldVerifyACandidateFileWithTheRecordedContent()
    {
        var directory = CreateDirectory();
        var content = """{ "openapi": "3.0.1", "info": { "title": "T", "version": "1" }, "paths": {} }""";
        var specPath = Path.Combine(directory, "openapi.json");
        File.WriteAllText(specPath, content);
        try
        {
            var hash = ProtoSpecIdentity.Hash(content);
            var current = Report(Spec("Api", specPath, hash));

            var verdict = ProtoVerification.Verify(
                Report(), current, [new ProtoSpecCandidate("Api", specPath)]);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(verdict.Findings, Is.Empty);
                Assert.That(verdict.CoverageDeltas, Is.Empty, "A specification identity item is an aggregate, not a coverage unit.");
                var check = verdict.SpecChecks.Single();
                Assert.That(check.Status, Is.EqualTo(ProtoSpecCheckStatuses.Verified));
                Assert.That(check.Source, Is.EqualTo(specPath));
                Assert.That(check.Hash, Is.EqualTo(hash));
                Assert.That(check.CandidateHash, Is.EqualTo(hash));
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Test]
    public void Verify_ShouldReportAChangedCandidateFile()
    {
        var directory = CreateDirectory();
        var recordedPath = Path.Combine(directory, "recorded.json");
        var candidatePath = Path.Combine(directory, "candidate.json");
        File.WriteAllText(recordedPath, """{ "openapi": "3.0.1", "paths": { "/a": {} } }""");
        File.WriteAllText(candidatePath, """{ "openapi": "3.0.1", "paths": { "/b": {} } }""");
        try
        {
            var current = Report(Spec("Api", recordedPath, ProtoSpecIdentity.Hash(File.ReadAllText(recordedPath))));

            var verdict = ProtoVerification.Verify(
                Report(), current, [new ProtoSpecCandidate("Api", candidatePath)]);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(verdict.Failed, Is.True);
                var finding = verdict.Findings.Single();
                Assert.That(finding.Class, Is.EqualTo(ProtoVerificationFindingClasses.StaleSpec));
                Assert.That(finding.Severity, Is.EqualTo(ProtoVerificationSeverities.Fail));
                Assert.That(finding.BaselineValue, Is.EqualTo(ProtoSpecIdentity.Hash(File.ReadAllText(recordedPath))));
                Assert.That(finding.CurrentValue, Is.EqualTo(ProtoSpecIdentity.Hash(File.ReadAllText(candidatePath))));

                var check = verdict.SpecChecks.Single();
                Assert.That(check.Status, Is.EqualTo(ProtoSpecCheckStatuses.Changed));
                Assert.That(check.CandidateHash,
                    Is.EqualTo(ProtoSpecIdentity.Hash(File.ReadAllText(candidatePath))));
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Test]
    public void Verify_ShouldRecordARemoteSourceWithoutFetching()
    {
        var current = Report(Spec("Api", "https://example.test/openapi.json", "hash"));

        var verdict = ProtoVerification.Verify(Report(), current);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(verdict.Findings, Is.Empty);
            var check = verdict.SpecChecks.Single();
            Assert.That(check.Status, Is.EqualTo(ProtoSpecCheckStatuses.Recorded));
            Assert.That(check.Message, Does.Contain("not re-verified"));
        }
    }

    [Test]
    public void Verify_ShouldRecordAFileWithoutACandidate()
    {
        var current = Report(Spec("Api", "openapi.json", "hash"));

        var verdict = ProtoVerification.Verify(Report(), current);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(verdict.Findings, Is.Empty);
            Assert.That(verdict.SpecChecks.Single().Status, Is.EqualTo(ProtoSpecCheckStatuses.Recorded));
            Assert.That(verdict.SpecChecks.Single().Message, Does.Contain("no candidate file was given"));
        }
    }

    [Test]
    public void Verify_ShouldReportASpecificationThatChangedBetweenRuns()
    {
        var baseline = Report(Spec("Api", "openapi.json", "hash-a"));
        var current = Report(Spec("Api", "openapi.json", "hash-b"));

        var verdict = ProtoVerification.Verify(baseline, current);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(verdict.Failed, Is.False, "A specification change is a fact the gate reports, not a failure.");
            var finding = verdict.Findings.Single();
            Assert.That(finding.Class, Is.EqualTo(ProtoVerificationFindingClasses.StaleSpec));
            Assert.That(finding.Severity, Is.EqualTo(ProtoVerificationSeverities.Info));
            Assert.That(finding.BaselineValue, Is.EqualTo("hash-a"));
            Assert.That(finding.CurrentValue, Is.EqualTo("hash-b"));
            var check = verdict.SpecChecks.Single();
            Assert.That(check.BaselineHash, Is.EqualTo("hash-a"));
            Assert.That(check.Hash, Is.EqualTo("hash-b"));
        }
    }

    [Test]
    public void Verify_ShouldReportAMissingCurrentSpecification()
    {
        var baseline = Report(Spec("Api", "openapi.json", "hash-a"));

        var verdict = ProtoVerification.Verify(baseline, Report());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(verdict.Failed, Is.False);
            var check = verdict.SpecChecks.Single();
            Assert.That(check.Status, Is.EqualTo(ProtoSpecCheckStatuses.Missing));
            Assert.That(check.BaselineHash, Is.EqualTo("hash-a"));
            var finding = verdict.Findings.Single();
            Assert.That(finding.Class, Is.EqualTo(ProtoVerificationFindingClasses.StaleSpec));
            Assert.That(finding.Severity, Is.EqualTo(ProtoVerificationSeverities.Info));
            Assert.That(finding.Message, Does.Contain("not verified"));
        }
    }

    [Test]
    public void Verify_ShouldRejectACandidateForAnUnknownTarget()
    {
        var current = Report(Spec("Api", "openapi.json", "hash"));

        var exception = Assert.Throws<ArgumentException>(() => ProtoVerification.Verify(
            Report(), current, [new ProtoSpecCandidate("Other", "openapi.json")]));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Does.Contain("'Other'"));
            Assert.That(exception.Message, Does.Contain("'Api'"), "The error names the targets the report records.");
        }
    }

    [Test]
    public void Verify_ShouldRejectDuplicateCandidates()
    {
        var current = Report(Spec("Api", "openapi.json", "hash"));

        var exception = Assert.Throws<ArgumentException>(() => ProtoVerification.Verify(
            Report(), current,
            [new ProtoSpecCandidate("Api", "a.json"), new ProtoSpecCandidate("api", "b.json")]));

        Assert.That(exception!.Message, Does.Contain("'Api'"));
    }

    [Test]
    public void Verify_ShouldRejectAMissingCandidateFile()
    {
        var directory = CreateDirectory();
        try
        {
            var current = Report(Spec("Api", "openapi.json", "hash"));
            var missing = Path.Combine(directory, "missing.json");

            var exception = Assert.Throws<InvalidOperationException>(() => ProtoVerification.Verify(
                Report(), current, [new ProtoSpecCandidate("Api", missing)]));

            Assert.That(exception!.Message, Does.Contain("missing.json"));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static string CreateDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ProtoTest.Verification.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
