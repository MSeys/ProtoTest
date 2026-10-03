namespace ProtoTest.Feedback.Tests;

using ProtoTest.Core;
using ProtoTest.Diagnosis;
using ProtoTest.Verification;

/// <summary>
/// The comment body the post sends for a target: the summary card and what it may carry, a shared cause,
/// source links, cell escaping and the row caps.
/// </summary>
[TestFixture]
public sealed class FeedbackCommentBodyTests
{
    private static readonly Uri Card = new("https://api.prototest.dev/evidence/card.svg");

    [Test]
    public void Body_ShouldOpenWithTheSummaryCardAgainstTheBaseBranch()
    {
        var body = ProtoFeedbackComment.Body(FeedbackFixtures.FailedDigest(), new ProtoFeedbackTarget
        {
            TraceLink = "https://example.test/artifact",
            Comparison = Comparison(("orders match their shape", ProtoTestChanges.Broken), ("invoices are paid", ProtoTestChanges.Fixed)),
            Coverage = Coverage(("Shop:Api", "OpenAPI", 29, 31, 29, 32)),
            SummaryCardUrl = Card
        });

        const string query = "broke=1&amp;failed=1&amp;fixed=1&amp;passed=0&amp;total=1&amp;uncovered=0&amp;cov=openapi:29/31:29/32";
        var lines = body.Split(Environment.NewLine);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(lines[0], Is.EqualTo(ProtoFeedbackComment.Marker));
            Assert.That(lines[1], Is.EqualTo("<a href=\"https://example.test/artifact\">"));
            Assert.That(body, Does.Contain($"<source media=\"(prefers-color-scheme: dark)\" srcset=\"{Card}?{query}&amp;theme=dark\">"));
            Assert.That(body, Does.Contain($"<img src=\"{Card}?{query}&amp;theme=light\""));
            Assert.That(body, Does.Contain("alt=\"ProtoTest: 1 broke · 1 failed · 1 fixed · 0 of 1 passed · 0 added without a test\""));
        }
    }

    [Test]
    public void Body_ShouldSendOnlyCountsAndBuiltInCoverageKindsToTheCard()
    {
        var body = ProtoFeedbackComment.Body(FeedbackFixtures.FailedDigest(), new ProtoFeedbackTarget
        {
            Comparison = Comparison(("orders match their shape", ProtoTestChanges.Broken)),
            Coverage = Coverage(
                ("Secret:Billing", "Acme ledger", 1, 2, 1, 3),
                ("Shop:Api", "GraphQL operation", 4, 4, 3, 4),
                ("Shop:Api", "REST traffic", 2, 2, 1, 2),
                ("Shop:Api", "Web", 5, 5, 4, 5)),
            SummaryCardUrl = Card
        });

        var source = body.Split(Environment.NewLine).Single(line => line.Contains("<img ", StringComparison.Ordinal));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(source, Does.Contain("cov=other:1/2:1/3,graphql-operation:4/4:3/4,rest-traffic:2/2:1/2&amp;"), "three rows at most");
            Assert.That(source, Does.Not.Contain("web:"));
            Assert.That(source, Does.Not.Contain("Secret").And.Not.Contain("ledger").And.Not.Contain("orders"));
        }
    }

    [Test]
    public void Body_ShouldLeaveTheCardOutWithoutABaseBranchRunOrAnAddress()
    {
        var withoutComparison = ProtoFeedbackComment.Body(
            FeedbackFixtures.FailedDigest(), new ProtoFeedbackTarget { SummaryCardUrl = Card });
        var withoutAddress = ProtoFeedbackComment.Body(
            FeedbackFixtures.FailedDigest(), new ProtoFeedbackTarget { Comparison = Comparison() });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(withoutComparison, Does.Not.Contain("<picture>"));
            Assert.That(withoutAddress, Does.Not.Contain("<picture>"));
            Assert.That(withoutAddress, Does.Contain("No test changed outcome against the base branch"));
        }
    }

    [Test]
    public void Body_ShouldNameACauseTheFailuresShareOnce()
    {
        var first = FeedbackFixtures.FailedDigest().Failures[0];
        ProtoDiagnosedTest[] failures =
        [
            WithSubject(first with { Name = "a remote start is accepted" }, "POST /api/stations/1c76fb0f-d623-4efc-97c6-0049239492c9/remote-start"),
            WithSubject(first with { Name = "a blocked start is answered" }, "POST /api/stations/8adc7d1e-3a0f-46ac-9821-18b05856cce7/remote-start"),
            WithSubject(first with { Name = "a station is listed", Mismatches = [] }, "GET /api/stations/12")
        ];
        var digest = FeedbackFixtures.FailedDigest() with
        {
            Failures = failures,
            Outcomes = new Dictionary<string, int>(StringComparer.Ordinal) { ["failed"] = 3 }
        };

        var body = ProtoFeedbackComment.Body(digest, new ProtoFeedbackTarget());

        Assert.That(body, Does.Contain(
            "> [!CAUTION]" + Environment.NewLine
            + "> **2 of the 3 failures share one cause:** `$.orderId` on `POST /api/stations/{id}/remote-start` differs from what the tests expect."));
    }

    [Test]
    public void Body_ShouldNotClaimASharedCauseForOneFailure()
    {
        var body = ProtoFeedbackComment.Body(FeedbackFixtures.FailedDigest(), new ProtoFeedbackTarget());

        Assert.That(body, Does.Not.Contain("[!CAUTION]"));
    }

    [Test]
    public void Body_ShouldLinkARelativeSourceLocationUnderTheCommit()
    {
        var target = new ProtoFeedbackTarget { SourceBaseUrl = new Uri("https://github.com/owner/repo/blob/abc123/") };

        var relative = ProtoFeedbackComment.Body(FeedbackFixtures.FailedDigest(sourceFile: @"tests\Orders\Order Tests.cs"), target);
        var rooted = ProtoFeedbackComment.Body(FeedbackFixtures.FailedDigest(sourceFile: "/home/runner/work/repo/tests/OrderTests.cs"), target);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(relative, Does.Contain("<sub>[Order Tests.cs:42](https://github.com/owner/repo/blob/abc123/tests/Orders/Order%20Tests.cs#L42)</sub>"));
            Assert.That(rooted, Does.Contain("<sub>`OrderTests.cs:42`</sub>"));
            Assert.That(rooted, Does.Not.Contain("blob/abc123"));
        }
    }

    [Test]
    public void Body_ShouldKeepATableRowWholeWhateverTheMessageSays()
    {
        var digest = FeedbackFixtures.FailedDigest(errorMessage: "Expected <b>a | b</b> but saw `x`\nsecond line");
        var failure = digest.Failures[0] with { Mismatches = [] };
        digest = digest with { Failures = [failure] };

        var body = ProtoFeedbackComment.Body(digest, new ProtoFeedbackTarget());

        var row = body.Split(Environment.NewLine).Single(line => line.StartsWith("| 🔴", StringComparison.Ordinal));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(row, Does.Contain(@"Expected \<b\>a \| b\</b\> but saw \`x\`<br>"));
            Assert.That(row, Does.Not.Contain("second line"), "the table shows the first line; the details carry the rest");
            Assert.That(body, Does.Contain("second line"));
        }
    }

    [Test]
    public void Body_ShouldKeepTheDetailsFenceClosedAroundBackticks()
    {
        var body = ProtoFeedbackComment.Body(FeedbackFixtures.FailedDigest(errorMessage: "Body was ```json {}```"), new ProtoFeedbackTarget());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(body, Does.Contain("````text"));
            Assert.That(body, Does.Contain("Body was ```json {}```" + Environment.NewLine + "$.orderId"));
        }
    }

    [Test]
    public void Body_ShouldCapTheFailuresAndPointAtTheTrace()
    {
        var first = FeedbackFixtures.FailedDigest().Failures[0];
        var failures = Enumerable.Range(1, 25).Select(index => first with { Name = $"order {index} is shaped" }).ToArray();
        var digest = FeedbackFixtures.FailedDigest() with
        {
            Failures = failures,
            Outcomes = new Dictionary<string, int>(StringComparer.Ordinal) { ["failed"] = 25 }
        };

        var body = ProtoFeedbackComment.Body(digest, new ProtoFeedbackTarget());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(body.Split(Environment.NewLine).Count(line => line.StartsWith("| 🔴", StringComparison.Ordinal)), Is.EqualTo(20));
            Assert.That(body, Does.Contain("| | 5 more | The trace has every failure. |"));
            Assert.That(body, Does.Contain("5 more failing tests are in the trace."));
            Assert.That(body, Does.Not.Contain("order 21 is shaped"));
        }
    }

    [Test]
    public void Body_ShouldTitleATestTheWayTheViewerDoes()
    {
        var test = FeedbackFixtures.FailedDigest().Failures[0] with
        {
            Name = "Shop.Tests.OrderTests.TheOperatorReadsAnOrderOverGraphQLById(42)",
            ClassName = "Shop.Tests.OrderTests",
            MethodName = "TheOperatorReadsAnOrderOverGraphQLById"
        };
        var digest = FeedbackFixtures.FailedDigest() with { Failures = [test] };

        var body = ProtoFeedbackComment.Body(digest, new ProtoFeedbackTarget());

        Assert.That(body, Does.Contain("**The operator reads an order over GraphQL by ID (42)**<br><sub>`OrderTests` · 16 ms</sub>"));
    }

    [Test]
    public void Body_ShouldMarkATestThatAlreadyFailedOnTheBaseBranch()
    {
        var body = ProtoFeedbackComment.Body(FeedbackFixtures.FailedDigest(), new ProtoFeedbackTarget
        {
            Comparison = Comparison(("orders match their shape", ProtoTestChanges.StillFailing))
        });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(body, Does.Contain("| 🟠 | **orders match their shape**<br><sub>`FixtureMethods` · 16 ms · already failing on the base branch</sub>"));
            Assert.That(body, Does.Contain("**1 of 1 test failed**"));
        }
    }

    private static ProtoDiagnosedTest WithSubject(ProtoDiagnosedTest test, string subject)
        => test with { Failure = test.Failure! with { Subject = subject } };

    private static ProtoTraceComparison Comparison(params (string Name, string Change)[] tests)
        => new("base-run", "run-1", tests.Select(test => new ProtoTestComparison(
            test.Name,
            test.Change,
            test.Change == ProtoTestChanges.Broken ? "succeeded" : "failed",
            test.Change == ProtoTestChanges.Fixed ? "succeeded" : "failed",
            null)).ToArray());

    private static ProtoVerificationVerdict Coverage(params (string Target, string Category, int BaseCovered, int BaseTotal, int Covered, int Total)[] rows)
        => new(
            [],
            rows.Select(row => new ProtoVerificationCoverageDelta(
                row.Target,
                row.Category,
                new ProtoCoverageSummary(row.Target, row.Category, row.BaseCovered, row.BaseTotal),
                new ProtoCoverageSummary(row.Target, row.Category, row.Covered, row.Total),
                Regressed: row.Covered < row.BaseCovered ? 1 : 0,
                AddedUncovered: 0)).ToArray(),
            []);

}
