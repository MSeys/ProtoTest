namespace ProtoTest.Core.Tests;

using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Reflection;

[TestFixture]
public sealed class ProtoLifecycleFailureTests
{
    [Test]
    public async Task FailedAttributeSetup_ShouldRollbackCompletedComponentsInReverseOrder()
    {
        var events = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton<IProtoTestHook>(new TrackingHook("FirstHook", 10, events));
        services.AddSingleton<IProtoTestHook>(new TrackingHook("SecondHook", 20, events));
        await using var host = new ProtoHost(services.BuildServiceProvider());
        ProtoAttribute[] attributes =
        [
            new TrackingAttribute("FirstAttribute", events) { Order = 10 },
            new TrackingAttribute("FailingAttribute", events, failBefore: true) { Order = 20 }
        ];

        Assert.ThrowsAsync<InvalidOperationException>(async () => await host.StartTestAsync(
            "Failure", "00001", (MethodInfo)MethodInfo.GetCurrentMethod()!, attributes));

        Assert.That(events, Is.EqualTo(new[]
        {
            "FirstHook:Before",
            "SecondHook:Before",
            "FirstAttribute:Before",
            "FailingAttribute:Before",
            "FirstAttribute:After",
            "SecondHook:After",
            "FirstHook:After"
        }));
        Assert.Throws<InvalidOperationException>(() => _ = Proto.Context);
    }

    [Test]
    public async Task Teardown_ShouldAttemptEveryComponentAndAggregateMultipleFailures()
    {
        var events = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton<IProtoTestHook>(new TrackingHook("FirstHook", 10, events, failAfter: true));
        services.AddSingleton<IProtoTestHook>(new TrackingHook("SecondHook", 20, events, failAfter: true));
        await using var host = new ProtoHost(services.BuildServiceProvider());
        ProtoAttribute[] attributes =
        [
            new TrackingAttribute("FirstAttribute", events, failAfter: true) { Order = 10 },
            new TrackingAttribute("SecondAttribute", events, failAfter: true) { Order = 20 }
        ];
        await host.StartTestAsync("Failure", "00001", (MethodInfo)MethodInfo.GetCurrentMethod()!, attributes);
        events.Clear();

        var exception = Assert.ThrowsAsync<AggregateException>(async () => await host.CompleteTestAsync());

        Assert.That(exception!.InnerExceptions, Has.Count.EqualTo(4));
        Assert.That(events, Is.EqualTo(new[]
        {
            "SecondAttribute:After",
            "FirstAttribute:After",
            "SecondHook:After",
            "FirstHook:After"
        }));
        Assert.Throws<InvalidOperationException>(() => _ = Proto.Context);
    }

    private sealed class TrackingHook(
        string name,
        int order,
        List<string> events,
        bool failAfter = false) : IProtoTestHook
    {
        public int Order => order;

        public Task BeforeTestAsync(ProtoExecutionContext context)
        {
            events.Add($"{name}:Before");
            return Task.CompletedTask;
        }

        public Task AfterTestAsync(ProtoExecutionContext context)
        {
            events.Add($"{name}:After");
            return failAfter
                ? Task.FromException(new InvalidOperationException($"{name} failed"))
                : Task.CompletedTask;
        }
    }

    private sealed class TrackingAttribute(
        string name,
        List<string> events,
        bool failBefore = false,
        bool failAfter = false) : ProtoAttribute
    {
        public override Task BeforeTestAsync(ProtoExecutionContext context)
        {
            events.Add($"{name}:Before");
            return failBefore
                ? Task.FromException(new InvalidOperationException($"{name} failed"))
                : Task.CompletedTask;
        }

        public override Task AfterTestAsync(ProtoExecutionContext context)
        {
            events.Add($"{name}:After");
            return failAfter
                ? Task.FromException(new InvalidOperationException($"{name} failed"))
                : Task.CompletedTask;
        }
    }
}
