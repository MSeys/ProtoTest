namespace ProtoTest.Messaging.MassTransit.Tests;

using System.Net;
using System.Net.Http.Json;
using ProtoTest.AspNetCore;
using ProtoTest.Core;
using ProtoTest.Messaging;
using ProtoTest.Messaging.MassTransit.TestApi;
using ProtoTest.TestSupport;

[TestFixture]
public sealed class MassTransitRoundTripTests
{
    [Test]
    public async Task ACommandTheApplicationConsumesPublishesTheEventTheTestAwaits()
    {
        await using var host = MassTransitSuite.Builder(trace: true).Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("masstransit round trip", TestMethods.Placeholder);
        var messages = context.Messaging();

        await messages.PublishAsync("PaymentReceived", """{"invoiceId":42,"amount":10.5}""");
        var paid = await messages.AwaitAsync(
            nameof(InvoicePaid),
            message => message.Payload!.Contains("\"invoiceId\":42"),
            TimeSpan.FromSeconds(15));

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        var test = host.Trace.Snapshot().Tests.Single();
        var invoice = paid.ReadRequired<InvoicePaid>();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(invoice.InvoiceId, Is.EqualTo(42), "the application's consumer received the published command");
            Assert.That(invoice.Amount, Is.EqualTo(10.5m));
            Assert.That(paid.Destination, Is.EqualTo(typeof(InvoicePaid).FullName));
            Assert.That(paid.ContentType, Is.EqualTo("application/json"));
            Assert.That(
                test.Entries.Single(entry => entry.Kind == "messaging.publish").Attributes["messaging.system"],
                Is.EqualTo("MassTransit"));
            Assert.That(
                test.Entries.Single(entry => entry.Kind == "messaging.await").Outcome,
                Is.EqualTo(ProtoTraceOutcome.Succeeded));
            Assert.That(
                context.RecordedObservations.Any(observation =>
                    observation.Kind == "messaging.published" && observation.Identifier == "PaymentReceived"),
                Is.True);
            Assert.That(
                context.RecordedObservations.Any(observation =>
                    observation.Kind == "messaging.receive" && observation.Identifier == typeof(InvoicePaid).FullName),
                Is.True);
        }
    }

    [Test]
    public async Task AnEventTheApplicationPublishesIsObservedWithTheSharedAssertSurface()
    {
        await using var host = MassTransitSuite.Builder().Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("masstransit app publish", TestMethods.Placeholder);
        var client = context.Client<HttpClient>("Default");

        using var response = await client.PostAsJsonAsync("/invoices/7/pay", new { });
        var paid = await context.Messaging().AwaitAsync(
            nameof(InvoicePaid),
            message => message.Payload!.Contains("\"invoiceId\":7"),
            TimeSpan.FromSeconds(15));
        paid.Should.MatchShape(new { invoiceId = 7, amount = 10.5m });

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        var shape = context.RecordedObservations.Single(observation => observation.Kind == "messaging.contract.shape");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(paid.ReadRequired<InvoicePaid>().InvoiceId, Is.EqualTo(7));
            Assert.That(shape.Identifier, Is.EqualTo(typeof(InvoicePaid).FullName));
        }
    }

    [Test]
    public async Task PublishedHeadersRideTheMessageAndAreVisibleOnTheAwait()
    {
        await using var host = MassTransitSuite.Builder().Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("masstransit headers", TestMethods.Placeholder);
        var messages = context.Messaging();

        await messages.PublishAsync(
            nameof(InvoicePaid),
            """{"invoiceId":1,"amount":2}""",
            new Dictionary<string, string?> { ["tenant"] = "northstar", ["dropped"] = null });
        var paid = await messages.AwaitAsync(
            nameof(InvoicePaid),
            message => message.Headers is not null
                && message.Headers.TryGetValue("tenant", out var tenant)
                && tenant == "northstar",
            TimeSpan.FromSeconds(15));

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(paid.ReadRequired<InvoicePaid>().InvoiceId, Is.EqualTo(1));
            Assert.That(paid.Headers, Does.ContainKey("tenant").WithValue("northstar"));
        }
    }

    [Test]
    public async Task ADestinationResolvesByFullNameShortNameOrUrn()
    {
        await using var host = MassTransitSuite.Builder().Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("masstransit destination forms", TestMethods.Placeholder);
        var messages = context.Messaging();
        var urn = $"urn:message:{typeof(InvoicePaid).Namespace}:{nameof(InvoicePaid)}";

        await messages.PublishAsync(urn, """{"invoiceId":5,"amount":1}""");
        await messages.PublishAsync(nameof(InvoicePaid), """{"invoiceId":6,"amount":2}""");
        var byFullName = await messages.AwaitAsync(
            typeof(InvoicePaid).FullName!,
            message => message.Payload!.Contains("\"invoiceId\":5"),
            TimeSpan.FromSeconds(15));
        var byShortName = await messages.AwaitAsync(
            nameof(InvoicePaid),
            message => message.Payload!.Contains("\"invoiceId\":6"),
            TimeSpan.FromSeconds(15));

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(byFullName.ReadRequired<InvoicePaid>().InvoiceId, Is.EqualTo(5));
            Assert.That(byFullName.Destination, Is.EqualTo(typeof(InvoicePaid).FullName));
            Assert.That(byShortName.ReadRequired<InvoicePaid>().InvoiceId, Is.EqualTo(6));
        }
    }

    [Test]
    public async Task AwaitingADestinationThatNeverArrivesFailsNamingIt()
    {
        await using var host = MassTransitSuite.Builder().Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("masstransit timeout", TestMethods.Placeholder);

        var timeout = Assert.ThrowsAsync<TimeoutException>(async () =>
            await context.Messaging().AwaitAsync(nameof(InvoicePaid), _ => false, TimeSpan.FromMilliseconds(100)));

        await host.CompleteTestAsync(ProtoTestResult.Failed(timeout!));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(timeout!.Message, Does.Contain(nameof(InvoicePaid)));
            Assert.That(
                context.RecordedObservations.Any(observation => observation.Kind == "messaging.failure"),
                Is.True,
                "the failed await is evidence too");
        }
    }

    [Test]
    public async Task PublishingADestinationThatNamesNoContractFailsNamingIt()
    {
        await using var host = MassTransitSuite.Builder().Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("masstransit unknown publish", TestMethods.Placeholder);

        var error = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await context.Messaging().PublishAsync("NoSuchContract", "{}"));

        await host.CompleteTestAsync(ProtoTestResult.Failed(error!));
        Assert.That(error!.Message, Does.Contain("NoSuchContract"));
    }

    [Test]
    public async Task PublishingAnEmptyPayloadForARecordWithoutADefaultInstanceFailsNamingTheContract()
    {
        await using var host = MassTransitSuite.Builder().Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("masstransit empty payload", TestMethods.Placeholder);

        var error = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await context.Messaging().PublishAsync(nameof(PaymentReceived)));

        await host.CompleteTestAsync(ProtoTestResult.Failed(error!));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(error!.Message, Does.Contain(nameof(PaymentReceived)), "the error names the contract");
            Assert.That(error.Message, Does.Contain("parameterless"), "the error names the fix");
        }
    }

    [Test]
    public async Task AnEmptyPayloadForAContractWithADefaultShapePublishesItsDefaultInstance()
    {
        await using var host = MassTransitSuite.Builder(m => m
            .Tap(nameof(EmptyPayloadProbe))
            .UseMassTransit<Program>()).Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("masstransit default instance", TestMethods.Placeholder);
        var messages = context.Messaging();

        await messages.PublishAsync(nameof(EmptyPayloadProbe));
        var probe = await messages.AwaitAsync(
            nameof(EmptyPayloadProbe),
            _ => true,
            TimeSpan.FromSeconds(15));

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        Assert.That(probe.ReadRequired<EmptyPayloadProbe>().Value, Is.EqualTo(0));
    }

    [Test]
    public async Task AwaitingATappedDestinationThatNamesNoContractFailsNamingItInsteadOfTimingOut()
    {
        await using var host = MassTransitSuite.Builder(m => m.Tap("NoSuchContract").UseMassTransit<Program>()).Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("masstransit unknown tap", TestMethods.Placeholder);

        var error = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await context.Messaging().AwaitAsync("NoSuchContract", _ => true, TimeSpan.FromMilliseconds(100)));

        await host.CompleteTestAsync(ProtoTestResult.Failed(error!));
        Assert.That(error!.Message, Does.Contain("NoSuchContract"));
    }

    [Test]
    public async Task PublishingAnInterfaceContractFailsNamingTheConcreteLimit()
    {
        await using var host = MassTransitSuite.Builder().Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("masstransit interface contract", TestMethods.Placeholder);

        var error = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await context.Messaging().PublishAsync(nameof(IInvoiceNotified), """{"invoiceId":1}"""));

        await host.CompleteTestAsync(ProtoTestResult.Failed(error!));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(error!.Message, Does.Contain(nameof(IInvoiceNotified)));
            Assert.That(error.Message, Does.Contain("concrete"));
        }
    }

    [Test]
    public async Task AnInterfaceContractTheApplicationPublishesIsAwaitable()
    {
        await using var host = MassTransitSuite.Builder().Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("masstransit interface await", TestMethods.Placeholder);
        var client = context.Client<HttpClient>("Default");

        using var response = await client.PostAsJsonAsync("/invoices/11/notify", new { });
        var notified = await context.Messaging().AwaitAsync(
            nameof(IInvoiceNotified),
            message => message.Payload!.Contains("\"invoiceId\":11"),
            TimeSpan.FromSeconds(15));

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(notified.Destination, Is.EqualTo(typeof(IInvoiceNotified).FullName));
        }
    }

    [Test]
    public async Task MessagesFromAnEarlierTestDoNotSatisfyALaterAwait()
    {
        await using var host = MassTransitSuite.Builder().Build();
        await host.StartAsync();

        var first = await host.StartTestAsync("masstransit earlier test", TestMethods.Placeholder);
        await first.Messaging().PublishAsync(nameof(InvoicePaid), """{"invoiceId":1,"amount":1}""");
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        var second = await host.StartTestAsync("masstransit later test", TestMethods.Placeholder);
        var timeout = Assert.ThrowsAsync<TimeoutException>(async () =>
            await second.Messaging().AwaitAsync(nameof(InvoicePaid), _ => true, TimeSpan.FromMilliseconds(100)));

        await host.CompleteTestAsync(ProtoTestResult.Failed(timeout!));
        Assert.That(
            timeout!.Message,
            Does.Contain(nameof(InvoicePaid)),
            "the harness keeps history, but each test's consumer snapshots the position at setup");
    }

    [Test]
    public async Task ATappedAwaitAfterASubstitutionSeesTheDedicatedHarnessMessages()
    {
        await using var host = MassTransitSuite.Builder().Build();
        await host.StartAsync();

        // An earlier test publishes on the run's shared harness, and this test's first await consumes
        // one of its messages, so the tapped consumer knows a position and a consumed index there
        // when the substitution arrives.
        var earlier = await host.StartTestAsync("masstransit before the substitution", TestMethods.Placeholder);
        await earlier.Messaging().PublishAsync(nameof(InvoicePaid), """{"invoiceId":1,"amount":1}""");
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        var context = await host.StartTestAsync("masstransit substituted", TestMethods.Placeholder);
        var messages = context.Messaging();
        await messages.PublishAsync(nameof(InvoicePaid), """{"invoiceId":2,"amount":2}""");
        await messages.AwaitAsync(
            nameof(InvoicePaid),
            message => message.Payload!.Contains("\"invoiceId\":2"),
            TimeSpan.FromSeconds(5));

        // Any substitution replaces the shared server with this test's dedicated one, and with it the
        // harness the bridge resolves; the substitution itself is incidental. Both of the dedicated
        // harness's first messages must arrive: its history starts empty, so neither the replaced
        // harness's position nor its consumed indices apply to it.
        context.Override<TimeProvider>(TimeProvider.System);
        await messages.PublishAsync(nameof(InvoicePaid), """{"invoiceId":9,"amount":9}""");
        await messages.PublishAsync(nameof(InvoicePaid), """{"invoiceId":10,"amount":10}""");
        var first = await messages.AwaitAsync(
            nameof(InvoicePaid),
            message => message.Payload!.Contains("\"invoiceId\":9"),
            TimeSpan.FromSeconds(5));
        var second = await messages.AwaitAsync(
            nameof(InvoicePaid),
            message => message.Payload!.Contains("\"invoiceId\":10"),
            TimeSpan.FromSeconds(5));

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.ReadRequired<InvoicePaid>().InvoiceId, Is.EqualTo(9));
            Assert.That(second.ReadRequired<InvoicePaid>().InvoiceId, Is.EqualTo(10));
        }
    }
}

/// <summary>A contract with a parameterless shape: an empty payload publishes its default instance.</summary>
public sealed record EmptyPayloadProbe
{
    public int Value { get; init; }
}
