namespace ProtoTest.Core.Tests;

using System.Diagnostics;
using System.Reflection;
using NUnit.Framework;

[TestFixture]
public sealed class ProtoSpanConverterTests
{
    [Test]
    public async Task ApplicationSpanWithIdentityAttribute_ShouldMaterializeATrackedValue()
    {
        const string Source = "ProtoTest.Core.Tests.SpanConverter";
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options =>
        {
            options.OutputPath = TemporaryTracePath();
            options.ActivitySources.Add(Source);
        });
        await using var host = builder.Build();
        await host.StartAsync();

        using var applicationSource = new ActivitySource(Source);
        using (var span = applicationSource.StartActivity("invoice.pay", ActivityKind.Server))
        {
            span?.SetTag("invoice.number", "INV-1");
            span?.SetTag("invoice.total", "125");
        }

        var value = host.Trace.Snapshot().Values!.Single(item => item.Id == "invoice:INV-1");

        Assert.Multiple(() =>
        {
            Assert.That($"{value.Kind}:{value.Id}", Is.EqualTo("value:invoice:INV-1"));
            Assert.That(value.Name, Is.EqualTo("Invoice INV-1"));
            Assert.That(value.Versions.Single().Change, Is.EqualTo("created"));
            Assert.That(value.Versions.Single().State["invoice.total"], Is.EqualTo("125"));
        });
    }

    [Test]
    public async Task CompletedTest_ShouldReleaseTheConvertersTrackedState()
    {
        const string Source = "ProtoTest.Core.Tests.SpanConverter.Release";
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options =>
        {
            options.OutputPath = TemporaryTracePath();
            options.ActivitySources.Add(Source);
        });
        await using var host = builder.Build();
        await host.StartAsync();
        var session = (ProtoTraceSession)host.Trace;

        await host.StartTestAsync("released writer", (MethodInfo)MethodInfo.GetCurrentMethod()!);
        using (var applicationSource = new ActivitySource(Source))
        using (var span = applicationSource.StartActivity("invoice.pay", ActivityKind.Server))
        {
            span?.SetTag("invoice.number", "INV-1");
        }

        Assert.That(session.TrackedWriterCount, Is.EqualTo(1), "the observed value belongs to the test writer");
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        // The converter's state map must not grow with every test a long-lived host runs.
        Assert.That(session.TrackedWriterCount, Is.Zero);
        await host.StopAsync();
    }

    private static string TemporaryTracePath()
        => Path.Combine(Path.GetTempPath(), "ProtoTest.Core.Tests", Guid.NewGuid().ToString("N"), "run.prototrace");
}
