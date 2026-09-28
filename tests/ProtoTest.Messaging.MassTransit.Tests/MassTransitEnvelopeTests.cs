namespace ProtoTest.Messaging.MassTransit.Tests;

using System.Text.Json;
using System.Text.Json.Nodes;
using ProtoTest.Core;
using ProtoTest.Messaging;
using ProtoTest.Messaging.MassTransit.TestApi;
using ProtoTest.TestSupport;

/// <summary>
/// The envelope interop surface on a broker that needs no infrastructure: the in-memory broker proves
/// the frame is self-contained - the conversions never touch a bus or harness - and the package's own
/// shape is pinned against the envelope it produces.
/// </summary>
[TestFixture]
public sealed class MassTransitEnvelopeTests
{
    [Test]
    public async Task Wrap_ShouldRoundTripThroughTheInMemoryBroker()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddMessaging();
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("masstransit envelope in-memory", TestMethods.Placeholder);
        var runId = Guid.NewGuid();

        var frame = MassTransitEnvelope.Wrap(
            "ProtoTest.Messaging.MassTransit.Tests:EnvelopeProbeCommand",
            new EnvelopeProbeCommand(runId, 42, 10.5m),
            correlationId: runId,
            headers: new Dictionary<string, string?> { ["tenant"] = "northstar", ["dropped"] = null });
        await context.Messaging().PublishAsync(frame.Destination, frame.Payload, frame.Headers, frame.ContentType);
        var awaited = await context.Messaging().AwaitAsync(
            frame.Destination,
            message => message.Payload!.Contains(runId.ToString()),
            TimeSpan.FromSeconds(5));

        var content = MassTransitEnvelope.Unwrap(awaited);
        var command = MassTransitEnvelope.Unwrap<EnvelopeProbeCommand>(awaited);
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(frame.ContentType, Is.EqualTo(MassTransitEnvelope.ContentType));
            Assert.That(command, Is.EqualTo(new EnvelopeProbeCommand(runId, 42, 10.5m)));
            Assert.That(content.MessageTypes, Does.Contain(UrnOf<EnvelopeProbeCommand>()));
            Assert.That(content.MessageId, Is.Not.Null);
            Assert.That(content.CorrelationId, Is.EqualTo(runId));
            Assert.That(content.ConversationId, Is.Not.Null);
            Assert.That(content.Payload, Does.Contain(runId.ToString()));
            Assert.That(awaited.Headers, Does.ContainKey("tenant").WithValue("northstar"));
        }
    }

    [Test]
    public void Wrap_ShouldEmitThePackagesEnvelopeShape()
    {
        var runId = Guid.NewGuid();
        var frame = MassTransitEnvelope.Wrap(
            "ProtoTest.Messaging.MassTransit.Tests:EnvelopeProbeEvent",
            new EnvelopeProbeEvent(runId, 7, 10.5m),
            headers: new Dictionary<string, string?> { ["tenant"] = "northstar" });

        using var document = JsonDocument.Parse(frame.Payload!);
        var root = document.RootElement;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(frame.ContentType, Is.EqualTo(MassTransitEnvelope.ContentType));
            Assert.That(Guid.TryParse(root.GetProperty("messageId").GetString(), out _), Is.True);
            Assert.That(root.GetProperty("correlationId").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(Guid.TryParse(root.GetProperty("conversationId").GetString(), out _), Is.True);
            Assert.That(root.GetProperty("sentTime").GetString(), Is.Not.Null);
            Assert.That(
                root.GetProperty("messageType").EnumerateArray().Select(type => type.GetString()),
                Does.Contain(UrnOf<EnvelopeProbeEvent>()));
            Assert.That(
                root.GetProperty("message").GetProperty("amount").GetString(),
                Is.EqualTo("10.5"),
                "the package's serializer writes decimals as strings, and so does the envelope");
            Assert.That(root.GetProperty("headers").GetProperty("tenant").GetString(), Is.EqualTo("northstar"));
            Assert.That(root.TryGetProperty("host", out _), Is.True, "a bus envelope carries the host block and so does this one");
        }
    }

    [Test]
    public void Wrap_ShouldCarryTheRequestResponseFieldsAndUnwrapShouldReadThem()
    {
        var requestId = Guid.NewGuid();
        var source = new Uri("rabbitmq://localhost/source");
        var destinationAddress = new Uri("rabbitmq://localhost/destination");
        var response = new Uri("rabbitmq://localhost/replies");

        var frame = MassTransitEnvelope.Wrap(
            "ProtoTest.Messaging.MassTransit.Tests:EnvelopeProbeCommand",
            new EnvelopeProbeCommand(Guid.NewGuid(), 1, 2),
            new MassTransitEnvelopeAddresses(
                SourceAddress: source,
                DestinationAddress: destinationAddress,
                ResponseAddress: response,
                RequestId: requestId));

        using var document = JsonDocument.Parse(frame.Payload!);
        var root = document.RootElement;
        var content = MassTransitEnvelope.Unwrap(frame);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(root.GetProperty("sourceAddress").GetString(), Is.EqualTo(source.ToString()));
            Assert.That(root.GetProperty("destinationAddress").GetString(), Is.EqualTo(destinationAddress.ToString()));
            Assert.That(root.GetProperty("responseAddress").GetString(), Is.EqualTo(response.ToString()),
                "a responder reads the response address from the envelope this frame carries");
            Assert.That(root.GetProperty("requestId").GetString(), Is.EqualTo(requestId.ToString("D")));
            Assert.That(content.SourceAddress, Is.EqualTo(source));
            Assert.That(content.DestinationAddress, Is.EqualTo(destinationAddress));
            Assert.That(content.ResponseAddress, Is.EqualTo(response));
            Assert.That(content.RequestId, Is.EqualTo(requestId));
        }
    }

    [Test]
    public void Wrap_WithoutAddresses_ShouldLeaveTheRequestResponseFieldsUnset()
    {
        var frame = MassTransitEnvelope.Wrap(
            "ProtoTest.Messaging.MassTransit.Tests:EnvelopeProbeCommand",
            new EnvelopeProbeCommand(Guid.NewGuid(), 1, 2),
            new MassTransitEnvelopeAddresses());

        var content = MassTransitEnvelope.Unwrap(frame);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(content.SourceAddress, Is.Null);
            Assert.That(content.DestinationAddress, Is.Null);
            Assert.That(content.ResponseAddress, Is.Null);
            Assert.That(content.RequestId, Is.Null);
        }
    }

    [Test]
    public void Wrap_WithAddresses_ShouldReadARawPayloadAsItsContract()
    {
        const string payload =
            """{"runId":"00000000-0000-0000-0000-000000000000","invoiceId":7,"amount":"1.5"}""";
        var requestId = Guid.NewGuid();
        var frame = MassTransitEnvelope.Wrap(
            "ProtoTest.Messaging.MassTransit.Tests:EnvelopeProbeCommand",
            payload,
            typeof(EnvelopeProbeCommand),
            new MassTransitEnvelopeAddresses(
                ResponseAddress: new Uri("rabbitmq://localhost/replies"),
                RequestId: requestId));

        var content = MassTransitEnvelope.Unwrap(frame);
        var command = MassTransitEnvelope.Unwrap<EnvelopeProbeCommand>(frame);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(command.InvoiceId, Is.EqualTo(7));
            Assert.That(content.ResponseAddress, Is.EqualTo(new Uri("rabbitmq://localhost/replies")));
            Assert.That(content.RequestId, Is.EqualTo(requestId));
        }
    }

    [Test]
    public void Unwrap_WhenAnAddressIsNotAbsolute_ShouldFailNamingIt()
    {
        var frame = MassTransitEnvelope.Wrap(
            "ProtoTest.Messaging.MassTransit.Tests:EnvelopeProbeCommand",
            new EnvelopeProbeCommand(Guid.NewGuid(), 1, 2));
        var envelope = JsonNode.Parse(frame.Payload!)!.AsObject();
        envelope["responseAddress"] = "not an address";
        var corrupted = frame with { Payload = envelope.ToJsonString() };

        var error = Assert.Throws<MessagingAssertionException>(() => MassTransitEnvelope.Unwrap(corrupted));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(error!.Message, Does.Contain("not a MassTransit envelope"));
            Assert.That(error.Message, Does.Contain("'responseAddress' is not an absolute address"));
        }
    }

    [Test]
    public void Wrap_ShouldReadARawPayloadAsItsContract()
    {
        const string payload =
            """{"runId":"00000000-0000-0000-0000-000000000000","invoiceId":7,"amount":"1.5"}""";
        var frame = MassTransitEnvelope.Wrap(
            "ProtoTest.Messaging.MassTransit.Tests:EnvelopeProbeCommand",
            payload,
            typeof(EnvelopeProbeCommand));

        var content = MassTransitEnvelope.Unwrap(frame);
        var command = MassTransitEnvelope.Unwrap<EnvelopeProbeCommand>(frame);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(JsonNode.DeepEquals(JsonNode.Parse(content.Payload), JsonNode.Parse(payload)), Is.True);
            Assert.That(command.InvoiceId, Is.EqualTo(7));
            Assert.That(command.Amount, Is.EqualTo(1.5m));
        }
    }

    [Test]
    public void Wrap_ShouldDeclareTheContractsInterfaceUrns()
    {
        var frame = MassTransitEnvelope.Wrap(
            "ProtoTest.Messaging.MassTransit.TestApi:InvoiceNotified",
            new InvoiceNotified(1));

        var content = MassTransitEnvelope.Unwrap(frame);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(content.MessageTypes, Does.Contain(UrnOf<InvoiceNotified>()));
            Assert.That(
                content.MessageTypes,
                Does.Contain(UrnOf<IInvoiceNotified>()),
                "the bus lists an interface contract beside its implementation, so a consumer of the interface matches");
        }
    }

    [Test]
    public void Unwrap_WhenTheFrameIsNotAnEnvelope_ShouldFailNamingTheDestination()
    {
        var untyped = Assert.Throws<MessagingAssertionException>(
            () => MassTransitEnvelope.Unwrap(new ProtoMessage("invoice.paid", """{"id":1}""")));
        var typed = Assert.Throws<MessagingAssertionException>(
            () => MassTransitEnvelope.Unwrap<EnvelopeProbeCommand>(new ProtoMessage("invoice.paid", """{"id":1}""")));
        var empty = Assert.Throws<MessagingAssertionException>(
            () => MassTransitEnvelope.Unwrap(new ProtoMessage("invoice.paid")));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(untyped!.Message, Does.Contain("invoice.paid"));
            Assert.That(untyped.Message, Does.Contain("not a MassTransit envelope"));
            Assert.That(typed!.Message, Does.Contain("not a MassTransit envelope"));
            Assert.That(empty!.Message, Does.Contain("not a MassTransit envelope"));
        }
    }

    [Test]
    public void Unwrap_WhenTheEnvelopeDoesNotDeclareTheType_ShouldFailNamingIt()
    {
        var frame = MassTransitEnvelope.Wrap(
            "ProtoTest.Messaging.MassTransit.Tests:EnvelopeProbeCommand",
            new EnvelopeProbeCommand(Guid.NewGuid(), 1, 2));

        var error = Assert.Throws<MessagingAssertionException>(
            () => MassTransitEnvelope.Unwrap<EnvelopeProbeEvent>(frame));

        Assert.That(error!.Message, Does.Contain(nameof(EnvelopeProbeEvent)));
    }

    [Test]
    public void Unwrap_ShouldReadTheContractsInterface()
    {
        var frame = MassTransitEnvelope.Wrap(
            "ProtoTest.Messaging.MassTransit.TestApi:InvoiceNotified",
            new InvoiceNotified(42));

        var notified = MassTransitEnvelope.Unwrap<IInvoiceNotified>(frame);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(notified.InvoiceId, Is.EqualTo(42), "an interface contract reads like the bus consumer would read it");
            Assert.That(
                MassTransitEnvelope.Unwrap(frame).MessageTypes,
                Does.Contain(UrnOf<IInvoiceNotified>()),
                "the envelope declares the interface URN beside the implementation's");
        }
    }

    [Test]
    public void Unwrap_WhenTheMessageIsNotValidJsonForTheContract_ShouldFailNamingTheDestination()
    {
        var frame = MassTransitEnvelope.Wrap(
            "ProtoTest.Messaging.MassTransit.Tests:EnvelopeProbeCommand",
            """{"invoiceId":"not-a-number"}""",
            typeof(EnvelopeProbeCommand));

        var error = Assert.Throws<MessagingAssertionException>(
            () => MassTransitEnvelope.Unwrap<EnvelopeProbeCommand>(frame));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(error!.Message, Does.Contain("ProtoTest.Messaging.MassTransit.Tests:EnvelopeProbeCommand"));
            Assert.That(error.Message, Does.Contain("not a MassTransit envelope"));
            Assert.That(error.Message, Does.Contain("not valid JSON for EnvelopeProbeCommand"));
        }
    }

    [TestCase("messageId")]
    [TestCase("correlationId")]
    [TestCase("conversationId")]
    public void Unwrap_WhenAnIdIsNotAGuid_ShouldFailNamingIt(string idName)
    {
        var frame = MassTransitEnvelope.Wrap(
            "ProtoTest.Messaging.MassTransit.Tests:EnvelopeProbeCommand",
            new EnvelopeProbeCommand(Guid.NewGuid(), 1, 2));
        var envelope = JsonNode.Parse(frame.Payload!)!.AsObject();
        envelope[idName] = "not-a-guid";
        var corrupted = frame with { Payload = envelope.ToJsonString() };

        var error = Assert.Throws<MessagingAssertionException>(() => MassTransitEnvelope.Unwrap(corrupted));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(error!.Message, Does.Contain("not a MassTransit envelope"));
            Assert.That(error.Message, Does.Contain($"'{idName}' is not a GUID"));
        }
    }

    [Test]
    public void Wrap_WhenTheRawPayloadIsJsonNull_ShouldProduceAnEnvelopeWithoutAMessage()
    {
        var frame = MassTransitEnvelope.Wrap(
            "ProtoTest.Messaging.MassTransit.Tests:EnvelopeProbeCommand",
            "null",
            typeof(EnvelopeProbeCommand));

        using var document = JsonDocument.Parse(frame.Payload!);
        var error = Assert.Throws<MessagingAssertionException>(() => MassTransitEnvelope.Unwrap(frame));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(document.RootElement.GetProperty("message").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(error!.Message, Does.Contain("carries no 'message'"));
        }
    }

    [Test]
    public void Wrap_WhenTheRawPayloadTypeIsNotAMessageContract_ShouldFailNamingIt()
    {
        var error = Assert.Throws<ArgumentException>(
            () => MassTransitEnvelope.Wrap("invoice.paid", "{}", typeof(string)));

        Assert.That(error!.Message, Does.Contain(nameof(String)));
    }

    [Test]
    public void Wrap_WhenThePayloadIsNotJson_ShouldFailNamingTheDestination()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => MassTransitEnvelope.Wrap("invoice.paid", "not json", typeof(EnvelopeProbeCommand)));

        Assert.That(error!.Message, Does.Contain("invoice.paid"));
    }

    [Test]
    public void Wrap_WhenTheTypeIsNotAMessageContract_ShouldFailNamingIt()
    {
        var error = Assert.Throws<ArgumentException>(() => MassTransitEnvelope.Wrap("invoice.paid", "plain text"));

        Assert.That(error!.Message, Does.Contain(nameof(String)));
    }

    private static string UrnOf<T>() => $"urn:message:{typeof(T).Namespace}:{typeof(T).Name}";
}
