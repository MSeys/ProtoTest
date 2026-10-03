namespace ProtoTest.Verification.Tests;

using ProtoTest.Core;
using ProtoTest.Reporting;
using static VerificationReports;

[TestFixture]
public sealed class CoverageDeltaTests
{
    [Test]
    public void Verify_ShouldFailOnACoveredUnitThatBecameUncovered()
    {
        var baseline = Report(
            Unit("Api", "OpenAPI", "GET /a", covered: true),
            Unit("Api", "OpenAPI", "GET /b", covered: true));
        var current = Report(
            Unit("Api", "OpenAPI", "GET /a", covered: false),
            Unit("Api", "OpenAPI", "GET /b", covered: true));

        var verdict = ProtoVerification.Verify(baseline, current);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(verdict.Failed, Is.True);
            var finding = verdict.Findings.Single();
            Assert.That(finding.Class, Is.EqualTo(ProtoVerificationFindingClasses.Regressed));
            Assert.That(finding.Severity, Is.EqualTo(ProtoVerificationSeverities.Fail));
            Assert.That(finding.TargetName, Is.EqualTo("Api"));
            Assert.That(finding.Category, Is.EqualTo("OpenAPI"));
            Assert.That(finding.Identifier, Is.EqualTo("GET /a"));
            Assert.That(finding.BaselineValue, Is.EqualTo("covered"));
            Assert.That(finding.CurrentValue, Is.EqualTo("uncovered"));

            var delta = verdict.CoverageDeltas.Single();
            Assert.That(delta.Baseline.Percentage, Is.EqualTo(100));
            Assert.That(delta.Current.Percentage, Is.EqualTo(50));
            Assert.That(delta.PercentageDelta, Is.EqualTo(-50));
            Assert.That(delta.Regressed, Is.EqualTo(1));
            Assert.That(delta.AddedUncovered, Is.Zero);
        }
    }

    [Test]
    public void Verify_ShouldKeyANestedUnitByItsPathUnderItsParents()
    {
        // OpenAPI names responses and properties per endpoint, so the same "200" and "$.id" appear under every endpoint.
        static ProtoReportItem Endpoint(string identifier, bool propertyCovered)
            => Unit("Api", "OpenAPI", identifier, covered: true) with
            {
                Children = [Unit("Api", "OpenAPI", "200", covered: true) with { Children = [Unit("Api", "OpenAPI", "$.id", propertyCovered)] }]
            };

        var baseline = Report(Endpoint("GET /a", propertyCovered: true), Endpoint("GET /b", propertyCovered: true));
        var current = Report(Endpoint("GET /a", propertyCovered: true), Endpoint("GET /b", propertyCovered: false));

        var verdict = ProtoVerification.Verify(baseline, current);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(verdict.Failed, Is.True);
            var finding = verdict.Findings.Single();
            Assert.That(finding.Class, Is.EqualTo(ProtoVerificationFindingClasses.Regressed));
            Assert.That(finding.Identifier, Is.EqualTo("GET /b › 200 › $.id"));
            Assert.That(verdict.CoverageDeltas.Single().Regressed, Is.EqualTo(1));
        }
    }

    [Test]
    public void Verify_ShouldKeepAnIdentifierItsCollectorAlreadyQualified()
    {
        // GraphQL schema coverage names a field "Type.field" under its type, so the parent is not repeated.
        static ProtoReportItem Type(bool argumentCovered)
            => new("Api", "GraphQL", "Query", Kind: ProtoReportItemKinds.Coverage, Status: ProtoReportStatus.Neutral, IsCovered: null)
            {
                Children = [Unit("Api", "GraphQL", "Query.users", covered: true) with { Children = [Unit("Api", "GraphQL", "Query.users(first)", argumentCovered)] }]
            };

        var verdict = ProtoVerification.Verify(Report(Type(argumentCovered: true)), Report(Type(argumentCovered: false)));

        Assert.That(verdict.Findings.Single().Identifier, Is.EqualTo("Query.users(first)"));
    }

    [Test]
    public void Verify_ShouldWarnOnANewUncoveredUnit()
    {
        var baseline = Report(Unit("Api", "OpenAPI", "GET /a", covered: true));
        var current = Report(
            Unit("Api", "OpenAPI", "GET /a", covered: true),
            Unit("Api", "OpenAPI", "GET /b", covered: false));

        var verdict = ProtoVerification.Verify(baseline, current);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(verdict.Failed, Is.False);
            var finding = verdict.Findings.Single();
            Assert.That(finding.Class, Is.EqualTo(ProtoVerificationFindingClasses.AddedUncovered));
            Assert.That(finding.Severity, Is.EqualTo(ProtoVerificationSeverities.Warn));
            Assert.That(finding.Identifier, Is.EqualTo("GET /b"));
            Assert.That(verdict.CoverageDeltas.Single().AddedUncovered, Is.EqualTo(1));
        }
    }

    [Test]
    public void Verify_ShouldIgnoreUnchangedAndImprovedUnits()
    {
        var baseline = Report(
            Unit("Api", "OpenAPI", "GET /a", covered: true),
            Unit("Api", "OpenAPI", "GET /c", covered: false),
            Unit("Api", "OpenAPI", "GET /d", covered: false));
        var current = Report(
            Unit("Api", "OpenAPI", "GET /a", covered: true),
            Unit("Api", "OpenAPI", "GET /c", covered: true),
            Unit("Api", "OpenAPI", "GET /d", covered: false),
            Unit("Api", "OpenAPI", "GET /e", covered: true));

        var verdict = ProtoVerification.Verify(baseline, current);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(verdict.Findings, Is.Empty,
                "An unchanged uncovered unit, an improved unit and a new covered unit are not findings.");
            Assert.That(verdict.Failed, Is.False);
            Assert.That(verdict.CoverageDeltas.Single().PercentageDelta, Is.EqualTo(41.67));
        }
    }

    [Test]
    public void Verify_ShouldNotFailOnAnEmptyBaseline()
    {
        var current = Report(
            Unit("Api", "OpenAPI", "GET /a", covered: false),
            Unit("Api", "OpenAPI", "GET /b", covered: true));

        var verdict = ProtoVerification.Verify(Report(), current);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(verdict.Failed, Is.False, "Every unit of an empty baseline is new, and new uncovered warns.");
            Assert.That(verdict.Findings.Single().Class, Is.EqualTo(ProtoVerificationFindingClasses.AddedUncovered));
            var delta = verdict.CoverageDeltas.Single();
            Assert.That(delta.Baseline.Total, Is.Zero);
            Assert.That(delta.Current.Total, Is.EqualTo(2));
            Assert.That(delta.Current.Covered, Is.EqualTo(1));
        }
    }

    [Test]
    public void Verify_ShouldApplyTheConfiguredSeverities()
    {
        var baseline = Report(Unit("Api", "OpenAPI", "GET /a", covered: true));
        var current = Report(
            Unit("Api", "OpenAPI", "GET /a", covered: false),
            FailedGate("Coverage", "covered share below the threshold"));
        var options = new ProtoVerificationOptions
        {
            RegressedSeverity = ProtoVerificationSeverities.Warn,
            GateFailureSeverity = ProtoVerificationSeverities.Info
        };

        var verdict = ProtoVerification.Verify(baseline, current, options: options);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(verdict.Failed, Is.False);
            Assert.That(verdict.Findings, Has.Count.EqualTo(2));
            Assert.That(verdict.Findings[0].Class, Is.EqualTo(ProtoVerificationFindingClasses.Regressed));
            Assert.That(verdict.Findings[0].Severity, Is.EqualTo(ProtoVerificationSeverities.Warn));
            Assert.That(verdict.Findings[1].Class, Is.EqualTo(ProtoVerificationFindingClasses.GateFailed));
            Assert.That(verdict.Findings[1].Severity, Is.EqualTo(ProtoVerificationSeverities.Info));
        }
    }

    [Test]
    public void Verify_ShouldRejectAnUnknownSeverity()
    {
        var options = new ProtoVerificationOptions { AddedUncoveredSeverity = "error" };

        var exception = Assert.Throws<ArgumentException>(
            () => ProtoVerification.Verify(Report(), Report(), options: options));

        Assert.That(exception!.Message, Does.Contain("fail, warn, info"));
    }

    [Test]
    public void Verify_ShouldSurfaceAFailedRunGate()
    {
        var current = Report(FailedGate("Coverage", "covered share below the threshold"));

        var verdict = ProtoVerification.Verify(Report(), current);

        using (Assert.EnterMultipleScope())
        {
            var finding = verdict.Findings.Single();
            Assert.That(finding.Class, Is.EqualTo(ProtoVerificationFindingClasses.GateFailed));
            Assert.That(finding.Severity, Is.EqualTo(ProtoVerificationSeverities.Fail));
            Assert.That(finding.TargetName, Is.EqualTo("Run gates"));
            Assert.That(finding.Identifier, Is.EqualTo("Coverage"));
            Assert.That(finding.Message, Does.Contain("covered share below the threshold"));
            Assert.That(verdict.Failed, Is.True);
        }
    }

    [Test]
    public void Verify_ShouldIgnorePassedGatesAndTrafficItems()
    {
        var current = Report(
            new ProtoReportItem("Run gates", "Gate", "Coverage", Kind: ProtoReportItemKinds.Gate,
                Status: ProtoReportStatus.Success, Count: 1),
            new ProtoReportItem("Api", "REST traffic", "$.name", Kind: ProtoReportItemKinds.Traffic, IsCovered: false));

        var verdict = ProtoVerification.Verify(Report(), current);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(verdict.Findings, Is.Empty);
            Assert.That(verdict.CoverageDeltas, Is.Empty, "Traffic items carry no coverage verdict and stay out of the arithmetic.");
        }
    }

    [Test]
    public void Verify_ShouldSortFindingsDeterministically()
    {
        var baseline = Report(
            Unit("Api", "OpenAPI", "GET /a", covered: true),
            Unit("Api", "OpenAPI", "GET /b", covered: true));
        var current = Report(
            Unit("Api", "OpenAPI", "GET /a", covered: false),
            Unit("Api", "OpenAPI", "GET /b", covered: false),
            FailedGate("Coverage", "failed"));

        var first = ProtoVerification.Verify(baseline, current);
        var second = ProtoVerification.Verify(baseline, current);

        Assert.That(first.Findings.Select(finding => (finding.Class, finding.Identifier)),
            Is.EqualTo(second.Findings.Select(finding => (finding.Class, finding.Identifier))));
        Assert.That(first.Findings.Select(finding => finding.Class),
            Is.EqualTo(new[]
            {
                ProtoVerificationFindingClasses.Regressed,
                ProtoVerificationFindingClasses.Regressed,
                ProtoVerificationFindingClasses.GateFailed
            }));
    }
}
