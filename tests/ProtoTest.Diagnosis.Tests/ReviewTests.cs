namespace ProtoTest.Diagnosis.Tests;

using ProtoTest.TestSupport;
using static ProtoTest.TestSupport.RecordedRuns;

/// <summary>
/// <see cref="ProtoDiagnosis.Review(string, IReadOnlyCollection{string}?)"/> over real runs: each rule
/// fires on the evidence it names, a test that checks what it calls is clean, and every finding carries
/// the next step.
/// </summary>
[TestFixture]
public sealed class ReviewTests
{
    [Test]
    public async Task Review_ShouldFlagATestBodyWithNoCheck()
    {
        using var trace = new TemporaryTrace("review-no-check");
        await WriteAsync(trace.Path, new RecordedTest("orders are listed", Call("List orders", "GET /orders")));

        var test = ProtoDiagnosis.Review(trace.Path).Tests.Single();
        var finding = test.Findings.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(finding.Rule, Is.EqualTo(ProtoReviewRules.NoCheck));
            Assert.That(finding.Subject, Is.EqualTo("GET /orders"));
            Assert.That(finding.Next, Does.Contain("Should.HaveStatus"));
            Assert.That(test.Checks, Is.Zero);
            Assert.That(test.Calls, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task Review_ShouldFlagACallNoCheckLookedAt()
    {
        using var trace = new TemporaryTrace("review-unchecked");
        await WriteAsync(
            trace.Path,
            new RecordedTest(
                "an order is created and listed",
                Call("Create order", "POST /orders"),
                Call("List orders", "GET /orders"),
                Check("orders shape")));

        var finding = ProtoDiagnosis.Review(trace.Path).Tests.Single().Findings.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(finding.Rule, Is.EqualTo(ProtoReviewRules.UncheckedCall));
            Assert.That(finding.Subject, Is.EqualTo("POST /orders"), "the check after the second call covers only that call");
            Assert.That(finding.Next, Does.Contain("move it into setup"));
        }
    }

    [Test]
    public async Task Review_ShouldFlagTimeTheBodyDidNotRecord()
    {
        using var trace = new TemporaryTrace("review-gap");
        await WriteAsync(
            trace.Path,
            new RecordedTest(
                "an invoice is paid",
                Sleep(TimeSpan.FromMilliseconds(400)),
                Call("Read invoice", "GET /invoices/1"),
                Check("invoice shape")));

        var finding = ProtoDiagnosis.Review(trace.Path).Tests.Single().Findings.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(finding.Rule, Is.EqualTo(ProtoReviewRules.UntracedGap));
            Assert.That(finding.Message, Does.Contain("before 'GET /invoices/1'"));
            Assert.That(finding.Next, Does.Contain("ProtoPolling"));
        }
    }

    [Test]
    public async Task Review_ShouldCallATestThatChecksEveryCallClean()
    {
        using var trace = new TemporaryTrace("review-clean");
        await WriteAsync(
            trace.Path,
            new RecordedTest(
                "an order is created and listed",
                Call("Create order", "POST /orders"),
                Check("created"),
                Call("List orders", "GET /orders"),
                Check("orders shape")),
            new RecordedTest("orders are listed", Call("List orders", "GET /orders")));

        var review = ProtoDiagnosis.Review(trace.Path, ["an order is created and listed"]);
        using var text = new StringWriter();
        ProtoReviewText.Write(review, text);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(review.Tests, Has.Count.EqualTo(1), "a named review reads only that test");
            Assert.That(review.Tests[0].Clean, Is.True);
            Assert.That(review.Tests[0].Checks, Is.EqualTo(2));
            Assert.That(text.ToString(), Does.Contain("1 tests · 1 clean · no findings"));
        }
    }

    [Test]
    public async Task WriteReview_ShouldListFindingsWithTheirNextStep()
    {
        using var trace = new TemporaryTrace("review-text");
        await WriteAsync(
            trace.Path,
            new RecordedTest("orders are listed", Call("List orders", "GET /orders")),
            new RecordedTest("invoices are paid", Call("Pay invoice", "POST /invoices"), Check("paid")));

        using var text = new StringWriter();
        ProtoReviewText.Write(ProtoDiagnosis.Review(trace.Path), text);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(text.ToString(), Does.Contain("2 tests · 1 clean · 1 no-check"));
            Assert.That(text.ToString(), Does.Contain("orders are listed (succeeded, 0 checks, 1 calls)"));
            Assert.That(text.ToString(), Does.Contain("    next: Assert on what the test is about"));
            Assert.That(text.ToString(), Does.Not.Contain("invoices are paid ("), "clean tests are counted, not listed");
        }
    }
}
