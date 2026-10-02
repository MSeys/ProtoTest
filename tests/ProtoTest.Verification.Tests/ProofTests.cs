namespace ProtoTest.Verification.Tests;

using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;
using ProtoTest.Reporting;
using ProtoTest.TestSupport;
using static ProtoTest.TestSupport.RecordedRuns;

/// <summary>
/// <see cref="ProtoVerification.Prove(string, IReadOnlyList{string}, IReadOnlyCollection{string}?)"/>
/// over real runs: the receipt is proven only when every condition holds, and each unmet one is named.
/// </summary>
[TestFixture]
public sealed class ProofTests
{
    private static readonly RecordedTest FailingOrders =
        new("orders are listed", FailedCall("List orders", "GET /orders", new TimeoutException("No answer.")));

    private static readonly RecordedTest PassingOrders = new("orders are listed", Call("List orders", "GET /orders"));

    private static readonly RecordedTest PassingInvoices = new("invoices are paid", Call("Pay invoice", "POST /invoices"));

    [Test]
    public async Task Prove_ShouldProveATestThatFailedAndNowSucceeds()
    {
        using var baseline = new TemporaryTrace("prove-baseline");
        using var current = new TemporaryTrace("prove-current");
        await WriteAsync(baseline.Path, FailingOrders, PassingInvoices);
        await WriteAsync(current.Path, PassingOrders, PassingInvoices);

        var receipt = ProtoVerification.Prove(baseline.Path, [current.Path]);
        var proof = receipt.Tests.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(receipt.Proven, Is.True);
            Assert.That(proof.Name, Is.EqualTo("orders are listed"), "with no names, the baseline's failing tests are claimed");
            Assert.That(proof.Reasons, Is.Empty);
            Assert.That(proof.Divergence!.Current!.Subject, Is.EqualTo("GET /orders"));
            Assert.That(receipt.ReportVerdict, Is.Null);
            Assert.That(receipt.ReportNote, Does.Contain("No JSON report is embedded"));
        }
    }

    [Test]
    public async Task Prove_ShouldRequireEveryCurrentRunToSucceed()
    {
        using var baseline = new TemporaryTrace("prove-baseline");
        using var first = new TemporaryTrace("prove-first");
        using var second = new TemporaryTrace("prove-second");
        await WriteAsync(baseline.Path, FailingOrders);
        await WriteAsync(first.Path, PassingOrders);
        await WriteAsync(second.Path, FailingOrders);

        var receipt = ProtoVerification.Prove(baseline.Path, [first.Path, second.Path]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(receipt.Proven, Is.False);
            Assert.That(receipt.Tests.Single().Reasons.Single().Code, Is.EqualTo(ProtoFixReasons.StillFailing));
            Assert.That(receipt.CurrentRunIds, Has.Count.EqualTo(2));
        }
    }

    [Test]
    public async Task Prove_ShouldRefuseATestTheBaselineDidNotShowFailing()
    {
        using var baseline = new TemporaryTrace("prove-baseline");
        using var current = new TemporaryTrace("prove-current");
        await WriteAsync(baseline.Path, PassingOrders);
        await WriteAsync(current.Path, PassingOrders);

        var receipt = ProtoVerification.Prove(baseline.Path, [current.Path], ["orders are listed", "tariffs are listed"]);
        var codes = receipt.Tests.SelectMany(test => test.Reasons).Select(reason => reason.Code).ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(receipt.Proven, Is.False);
            Assert.That(codes, Does.Contain(ProtoFixReasons.NotFailingInBaseline));
            Assert.That(codes, Does.Contain(ProtoFixReasons.NotInBaseline));
            Assert.That(codes, Does.Contain(ProtoFixReasons.NotInCurrent));
        }
    }

    [Test]
    public async Task Prove_ShouldNotProveAFixThatBrokeAnotherTest()
    {
        using var baseline = new TemporaryTrace("prove-baseline");
        using var current = new TemporaryTrace("prove-current");
        await WriteAsync(baseline.Path, FailingOrders, PassingInvoices);
        await WriteAsync(
            current.Path,
            PassingOrders,
            new RecordedTest("invoices are paid", FailedCall("Pay invoice", "POST /invoices", new InvalidOperationException("402"))));

        var receipt = ProtoVerification.Prove(baseline.Path, [current.Path]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(receipt.Tests.Single().Proven, Is.True);
            Assert.That(receipt.BrokenTests, Is.EqualTo(new[] { "invoices are paid" }));
            Assert.That(receipt.Proven, Is.False);
        }
    }

    [Test]
    public async Task Prove_ShouldNotProveAFixThatLostCoverage()
    {
        using var baseline = new TemporaryTrace("prove-baseline");
        using var current = new TemporaryTrace("prove-current");
        await WriteAsync(baseline.Path, WithReport(baseline.Path, covered: true), FailingOrders);
        await WriteAsync(current.Path, WithReport(current.Path, covered: false), PassingOrders);

        var receipt = ProtoVerification.Prove(baseline.Path, [current.Path]);
        using var text = new StringWriter();
        ProtoVerificationText.Write(receipt, text);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(receipt.Tests.Single().Proven, Is.True);
            Assert.That(receipt.ReportVerdict!.Failed, Is.True);
            Assert.That(receipt.Proven, Is.False);
            Assert.That(text.ToString(), Does.Contain("ProtoTest fix not proven"));
            Assert.That(text.ToString(), Does.Contain("REPORT regressed"));
        }
    }

    [Test]
    public void Prove_ShouldRequireARunAfterTheFix()
        => Assert.That(
            () => ProtoVerification.Prove("baseline.prototrace", Array.Empty<string>()),
            Throws.InstanceOf<Exception>());

    private static Action<ProtoHostBuilder> WithReport(string tracePath, bool covered)
        => builder =>
        {
            builder.AddSink(new JsonReportSink { OutputPath = $"{Path.ChangeExtension(tracePath, null)}.report.json" });
            builder.ConfigureServices(services => services.AddSingleton<IProtoReportSource>(new OneUnitSource(covered)));
        };

    private sealed class OneUnitSource(bool covered) : IProtoReportSource
    {
        public IEnumerable<ProtoReportItem> GetReportItems()
            =>
            [
                new(
                    "Shop:Api",
                    "OpenAPI",
                    "GET /orders",
                    Kind: ProtoReportItemKinds.Coverage,
                    Status: covered ? ProtoReportStatus.Success : ProtoReportStatus.Neutral,
                    Count: covered ? 1 : 0,
                    IsCovered: covered)
            ];
    }
}
