namespace ProtoTest.OpenTelemetry.Tests;

using System.Diagnostics;
using System.Reflection;
using global::OpenTelemetry;
using global::OpenTelemetry.Trace;
using ProtoTest.Core;
using ProtoTest.OpenTelemetry;

// Activity listeners are process-wide, so these tests must not observe each other's spans.
[TestFixture]
[NonParallelizable]
public sealed class OpenTelemetryInstrumentationTests
{
    [Test]
    public async Task Instrumentation_ShouldExportProtoTraceOperationsAsSpans()
    {
        var exported = new List<Activity>();
        using (var provider = Sdk.CreateTracerProviderBuilder()
                   .AddProtoTestInstrumentation()
                   .AddInMemoryExporter(exported)
                   .Build())
        {
            var host = new ProtoHostBuilder().Build();
            await using var ownedHost = host;
            await host.StartAsync();
            var context = await host.StartTestAsync("exported test", TestMethod());
            context.Trace.WriteEvent("orders.requested", "Order requested", "ProtoTest.OpenTelemetry.Tests");

            using (var operation = context.Trace.StartOperation(
                       "orders.create",
                       "Create order",
                       "ProtoTest.OpenTelemetry.Tests",
                       attributes: new Dictionary<string, string?> { ["order.product"] = "notebook" }))
            {
                context.Trace.WriteEvent(
                    "orders.validated",
                    "Order validated",
                    "ProtoTest.OpenTelemetry.Tests",
                    outcome: ProtoTraceOutcome.Succeeded);
                operation.Succeed();
            }

            await host.CompleteTestAsync(ProtoTestResult.Passed);
            await host.StopAsync();
            provider.ForceFlush();
        }

        var create = exported.Single(activity => Tag(activity, "prototest.entry.kind") == "orders.create");
        var validated = create.Events.Single(item => item.Name == "Order validated");
        var execution = exported.Single(activity => Tag(activity, "prototest.entry.kind") == "test.execution");
        Assert.Multiple(() =>
        {
            Assert.That(create.Source.Name, Is.EqualTo(ProtoTestDiagnostics.ActivitySourceName));
            Assert.That(create.DisplayName, Is.EqualTo("Create order"));
            Assert.That(create.Kind, Is.EqualTo(ActivityKind.Internal));
            Assert.That(create.Status, Is.EqualTo(ActivityStatusCode.Ok));
            Assert.That(Tag(create, "prototest.test.id"), Is.Not.Empty);
            Assert.That(Tag(create, "prototest.source"), Is.EqualTo("ProtoTest.OpenTelemetry.Tests"));
            Assert.That(Tag(create, "prototest.phase"), Is.EqualTo("execution"));
            Assert.That(Tag(create, "prototest.outcome"), Is.EqualTo("succeeded"));
            Assert.That(Tag(create, "order.product"), Is.EqualTo("notebook"));
            Assert.That(validated.Tags.Single(tag => tag.Key == "prototest.entry.kind").Value, Is.EqualTo("orders.validated"));
            Assert.That(validated.Tags.Single(tag => tag.Key == "prototest.outcome").Value, Is.EqualTo("succeeded"));
            Assert.That(exported.Select(activity => Tag(activity, "prototest.entry.kind")),
                Has.Some.EqualTo("test.setup").And.Some.EqualTo("test.teardown"));
            Assert.That(create.ParentSpanId, Is.EqualTo(execution.SpanId));
            Assert.That(create.TraceId, Is.EqualTo(execution.TraceId));
            Assert.That(execution.Events.Select(item => item.Name), Has.Some.EqualTo("Order requested"));
        });
    }

    [Test]
    public async Task Instrumentation_ShouldMarkFailedOperationsAsErrors()
    {
        var exported = new List<Activity>();
        using (var provider = Sdk.CreateTracerProviderBuilder()
                   .AddProtoTestInstrumentation()
                   .AddInMemoryExporter(exported)
                   .Build())
        {
            var host = new ProtoHostBuilder().Build();
            await using var ownedHost = host;
            var context = await host.StartTestAsync("failing test", TestMethod());

            var failure = new InvalidOperationException("payment declined");
            using (var operation = context.Trace.StartOperation("payments.charge", "Charge card", "Tests"))
            {
                operation.Fail(failure);
            }

            await host.CompleteTestAsync(ProtoTestResult.Failed(failure));
            provider.ForceFlush();
        }

        var charge = exported.Single(activity => Tag(activity, "prototest.entry.kind") == "payments.charge");
        Assert.Multiple(() =>
        {
            Assert.That(charge.Status, Is.EqualTo(ActivityStatusCode.Error));
            Assert.That(charge.StatusDescription, Is.EqualTo("payment declined"));
            Assert.That(Tag(charge, "prototest.outcome"), Is.EqualTo("failed"));
            Assert.That(charge.Events.Select(item => item.Name), Has.Some.EqualTo("exception"));
        });
    }

    [Test]
    public async Task WithoutInstrumentation_ShouldNotExportProtoTestSpans()
    {
        var exported = new List<Activity>();
        using (var provider = Sdk.CreateTracerProviderBuilder()
                   .AddInMemoryExporter(exported)
                   .Build())
        {
            var host = new ProtoHostBuilder().Build();
            await using var ownedHost = host;
            await host.StartTestAsync("quiet test", TestMethod());
            await host.CompleteTestAsync(ProtoTestResult.Passed);
            provider.ForceFlush();
        }

        Assert.That(exported, Is.Empty);
    }

    private static string? Tag(Activity activity, string name) => activity.GetTagItem(name) as string;

    private static MethodInfo TestMethod()
        => typeof(OpenTelemetryInstrumentationTests).GetMethod(nameof(Placeholder), BindingFlags.Static | BindingFlags.NonPublic)!;

    private static void Placeholder() { }
}
