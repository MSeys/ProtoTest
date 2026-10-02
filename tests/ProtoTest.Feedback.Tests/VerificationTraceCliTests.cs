namespace ProtoTest.Feedback.Tests;

using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Cli;
using ProtoTest.Core;
using ProtoTest.Reporting;
using ProtoTest.TestSupport;
using static ProtoTest.TestSupport.RecordedRuns;

/// <summary>`prototest verify` over two traces: the reports the runs embedded decide the verdict.</summary>
[TestFixture]
public sealed class VerificationTraceCliTests
{
    [Test]
    public async Task Verify_ShouldReadTheReportsTheTracesEmbedded()
    {
        using var baseline = new TemporaryTrace("verify-trace-baseline");
        using var current = new TemporaryTrace("verify-trace-current");
        var test = new RecordedTest("orders are listed", Call("List orders", "GET /orders"));
        await WriteAsync(baseline.Path, WithUnit(baseline.Path, covered: true), test);
        await WriteAsync(current.Path, WithUnit(current.Path, covered: false), test);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exit = CliHost.Run(["verify", baseline.Path, current.Path], output, error);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exit, Is.EqualTo(1));
            Assert.That(output.ToString(), Does.StartWith("::error::regressed: "));
            Assert.That(error.ToString(), Is.Empty);
        }
    }

    [Test]
    public async Task Verify_ShouldNameATraceWithoutAnEmbeddedReport()
    {
        using var trace = new TemporaryTrace("verify-trace-bare");
        await WriteAsync(trace.Path, new RecordedTest("orders are listed", Call("List orders", "GET /orders")));
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exit = CliHost.Run(["verify", trace.Path, trace.Path], output, error);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exit, Is.EqualTo(1));
            Assert.That(error.ToString(), Does.Contain("No JSON report is embedded"));
            Assert.That(error.ToString(), Does.Contain("JsonReportSink"));
        }
    }

    private static Action<ProtoHostBuilder> WithUnit(string tracePath, bool covered)
        => builder =>
        {
            builder.AddSink(new JsonReportSink { OutputPath = $"{Path.ChangeExtension(tracePath, null)}.report.json" });
            builder.ConfigureServices(services => services.AddSingleton<IProtoReportSource>(new Source(covered)));
        };

    private sealed class Source(bool covered) : IProtoReportSource
    {
        public IEnumerable<ProtoReportItem> GetReportItems()
            => [new("Shop:Api", "OpenAPI", "GET /orders", ProtoReportItemKinds.Coverage, covered ? ProtoReportStatus.Success : ProtoReportStatus.Neutral, covered ? 1 : 0, covered)];
    }
}
