namespace ProtoTest.Verification.Tests;

using ProtoTest.TestSupport;
using static ProtoTest.TestSupport.RecordedRuns;

/// <summary>
/// <see cref="ProtoVerification.Compare(string, string)"/> over two real runs: the change class of each
/// test and the first operation where its two recordings part.
/// </summary>
[TestFixture]
public sealed class ComparisonTests
{
    [Test]
    public async Task Compare_ShouldClassifyEveryTestAcrossTheTwoRuns()
    {
        using var baseline = new TemporaryTrace("compare-baseline");
        using var current = new TemporaryTrace("compare-current");
        await WriteAsync(
            baseline.Path,
            new RecordedTest("orders are listed", Call("List orders", "GET /orders"), Check("orders shape")),
            new RecordedTest("orders match their shape", Call("List orders", "GET /orders"), FailedCheck("orders shape", new InvalidOperationException("Shape mismatch."))),
            new RecordedTest("invoices are paid", Call("Pay invoice", "POST /invoices")),
            new RecordedTest("the old export works", Call("Export", "GET /export")));
        await WriteAsync(
            current.Path,
            new RecordedTest("orders are listed", FailedCall("List orders", "GET /orders", new TimeoutException("No answer in 2 seconds."))),
            new RecordedTest("orders match their shape", Call("List orders", "GET /orders"), Check("orders shape")),
            new RecordedTest("invoices are paid", Call("Pay invoice", "POST /invoices")),
            new RecordedTest("tariffs are listed", Call("List tariffs", "GET /tariffs")));

        var comparison = ProtoVerification.Compare(baseline.Path, current.Path);
        var changes = comparison.Tests.ToDictionary(test => test.Name, test => test.Change);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(changes["orders are listed"], Is.EqualTo(ProtoTestChanges.Broken));
            Assert.That(changes["orders match their shape"], Is.EqualTo(ProtoTestChanges.Fixed));
            Assert.That(changes["invoices are paid"], Is.EqualTo(ProtoTestChanges.Unchanged));
            Assert.That(changes["tariffs are listed"], Is.EqualTo(ProtoTestChanges.New));
            Assert.That(changes["the old export works"], Is.EqualTo(ProtoTestChanges.Removed));
            Assert.That(comparison.Tests[0].Change, Is.EqualTo(ProtoTestChanges.Broken), "broken tests lead");
            Assert.That(comparison.HasBroken, Is.True);
            Assert.That(comparison.Counts[ProtoTestChanges.Unchanged], Is.EqualTo(1));
        }
    }

    [Test]
    public async Task Compare_ShouldNameTheOperationWhereABrokenTestLeftTheBaseline()
    {
        using var baseline = new TemporaryTrace("compare-baseline");
        using var current = new TemporaryTrace("compare-current");
        await WriteAsync(baseline.Path, new RecordedTest("orders are listed", Call("List orders", "GET /orders"), Check("orders shape")));
        await WriteAsync(
            current.Path,
            new RecordedTest("orders are listed", FailedCall("List orders", "GET /orders", new TimeoutException("No answer in 2 seconds."))));

        var divergence = ProtoVerification.Compare(baseline.Path, current.Path).Tests.Single().Divergence;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(divergence, Is.Not.Null);
            Assert.That(divergence!.Reason, Is.EqualTo(ProtoDivergenceReasons.StatusChanged));
            Assert.That(divergence.Current!.Subject, Is.EqualTo("GET /orders"));
            Assert.That(divergence.Current.ErrorType, Does.Contain("TimeoutException"));
            Assert.That(divergence.Baseline!.Status, Is.EqualTo("succeeded"));
        }
    }

    [Test]
    public async Task Compare_ShouldReportTheOperationAFailingRunNoLongerReached()
    {
        using var baseline = new TemporaryTrace("compare-baseline");
        using var current = new TemporaryTrace("compare-current");
        await WriteAsync(
            baseline.Path,
            new RecordedTest("orders flow", Call("Create order", "POST /orders"), FailedCheck("order shape", new InvalidOperationException("Shape mismatch."))));
        await WriteAsync(
            current.Path,
            new RecordedTest("orders flow", FailedCall("Create order", "POST /orders", new InvalidOperationException("409 Conflict"))));

        var test = ProtoVerification.Compare(baseline.Path, current.Path).Tests.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(test.Change, Is.EqualTo(ProtoTestChanges.StillFailing));
            Assert.That(test.Divergence!.Reason, Is.EqualTo(ProtoDivergenceReasons.StatusChanged));
            Assert.That(test.Divergence.Current!.Name, Is.EqualTo("Create order"),
                "the failure moved to the earlier call, so the call is where the runs part");
        }
    }

    [Test]
    public async Task Compare_ShouldSayAStillFailingTestFailsTheSameWay()
    {
        using var baseline = new TemporaryTrace("compare-baseline");
        using var current = new TemporaryTrace("compare-current");
        await WriteAsync(
            baseline.Path,
            new RecordedTest("orders flow", FailedCall("Create order", "POST /orders", new TimeoutException("No answer after 2 s (run 1)."))));
        await WriteAsync(
            current.Path,
            new RecordedTest("orders flow", FailedCall("Create order", "POST /orders", new TimeoutException("No answer after 2 s (run 2)."))));

        var comparison = ProtoVerification.Compare(baseline.Path, current.Path);
        using var text = new StringWriter();
        ProtoVerificationText.Write(comparison, text);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(comparison.Tests.Single().Divergence, Is.Null, "a different message alone is not a divergence");
            Assert.That(text.ToString(), Does.Contain("fails the same way"));
        }
    }

    [Test]
    public async Task Compare_ShouldNameAChangedErrorType()
    {
        using var baseline = new TemporaryTrace("compare-baseline");
        using var current = new TemporaryTrace("compare-current");
        await WriteAsync(
            baseline.Path,
            new RecordedTest("orders flow", FailedCall("Create order", "POST /orders", new TimeoutException("No answer."))));
        await WriteAsync(
            current.Path,
            new RecordedTest("orders flow", FailedCall("Create order", "POST /orders", new InvalidOperationException("409 Conflict"))));

        var divergence = ProtoVerification.Compare(baseline.Path, current.Path).Tests.Single().Divergence;

        Assert.That(divergence!.Reason, Is.EqualTo(ProtoDivergenceReasons.ErrorChanged));
    }

    [Test]
    public async Task Compare_ShouldPairARepeatedTestNameByOccurrence()
    {
        using var baseline = new TemporaryTrace("compare-baseline");
        using var current = new TemporaryTrace("compare-current");
        await WriteAsync(
            baseline.Path,
            new RecordedTest("row", Call("Read", "GET /a")),
            new RecordedTest("row", FailedCall("Read", "GET /b", new TimeoutException("No answer."))));
        await WriteAsync(
            current.Path,
            new RecordedTest("row", Call("Read", "GET /a")),
            new RecordedTest("row", Call("Read", "GET /b")));

        var changes = ProtoVerification.Compare(baseline.Path, current.Path).Tests.Select(test => test.Change).ToArray();

        Assert.That(changes, Is.EquivalentTo(new[] { ProtoTestChanges.Fixed, ProtoTestChanges.Unchanged }));
    }

    [Test]
    public async Task Compare_ShouldNotCountASkippedTestAsFailing()
    {
        using var baseline = new TemporaryTrace("compare-baseline");
        using var current = new TemporaryTrace("compare-current");
        var skipped = new RecordedTest("a drill that only runs on request") { Skipped = true };
        await WriteAsync(baseline.Path, skipped, new RecordedTest("orders are listed", FailedCall("List orders", "GET /orders", new TimeoutException("No answer."))));
        await WriteAsync(current.Path, skipped, new RecordedTest("orders are listed", Call("List orders", "GET /orders")));

        var changes = ProtoVerification.Compare(baseline.Path, current.Path).Tests.ToDictionary(test => test.Name, test => test.Change);
        var receipt = ProtoVerification.Prove(baseline.Path, [current.Path]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(changes["a drill that only runs on request"], Is.EqualTo(ProtoTestChanges.Unchanged));
            Assert.That(changes["orders are listed"], Is.EqualTo(ProtoTestChanges.Fixed));
            Assert.That(receipt.Tests.Select(test => test.Name), Is.EqualTo(new[] { "orders are listed" }), "a skipped test is not claimed");
            Assert.That(receipt.Proven, Is.True);
        }
    }

    [Test]
    public async Task Compare_ShouldPairStepsWhoseNamesCarryRunValuesAndPreferTheFailedCheck()
    {
        using var baseline = new TemporaryTrace("compare-baseline");
        using var current = new TemporaryTrace("compare-current");
        await WriteAsync(
            baseline.Path,
            new RecordedTest("a project appears on the page", Navigate("http://127.0.0.1:58860/login"), Check("Status should have text \"active\"")));
        await WriteAsync(
            current.Path,
            new RecordedTest(
                "a project appears on the page",
                Navigate("http://127.0.0.1:60278/login"),
                FailedCheck("Status should have text \"active\"", new InvalidOperationException("Was \"ACTIVE\"."))));

        var divergence = ProtoVerification.Compare(baseline.Path, current.Path).Tests.Single().Divergence;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(divergence!.Reason, Is.EqualTo(ProtoDivergenceReasons.StatusChanged), "the navigation pairs although its port changed");
            Assert.That(divergence.Current!.Kind, Is.EqualTo("assert.json.shape"));
        }
    }

    [Test]
    public async Task WriteComparison_ShouldListChangedTestsWithTheirDivergence()
    {
        using var baseline = new TemporaryTrace("compare-baseline");
        using var current = new TemporaryTrace("compare-current");
        await WriteAsync(baseline.Path, new RecordedTest("orders are listed", Call("List orders", "GET /orders")));
        await WriteAsync(
            current.Path,
            new RecordedTest("orders are listed", FailedCall("List orders", "GET /orders", new TimeoutException("No answer in 2 seconds."))));

        using var text = new StringWriter();
        ProtoVerificationText.Write(ProtoVerification.Compare(baseline.Path, current.Path), text);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(text.ToString(), Does.Contain("1 tests · 1 broken"));
            Assert.That(text.ToString(), Does.Contain("BROKEN orders are listed (succeeded -> failed)"));
            Assert.That(text.ToString(), Does.Contain("diverges at: http.request List orders [GET /orders] (status-changed)"));
            Assert.That(text.ToString(), Does.Contain("No answer in 2 seconds."));
        }
    }
}
