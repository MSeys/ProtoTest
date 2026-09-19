namespace ProtoTest.Messaging.Tests;

using System.Reflection;
using System.Text;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using ProtoTest.Core;

[TestFixture]
public sealed class MessagingAttachmentTests
{
    [Test]
    public async Task CaptureAttachments_ShouldRedactSensitiveFieldsAndTruncate()
    {
        var builder = new ProtoHostBuilder();
        builder.AddMessaging(messaging => messaging.CaptureAttachments(options =>
            options.MaxDiagnosticBodyLength = 160));
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("messaging attachments", TestMethod());
        var messages = context.Messaging();

        var payload = $"{{\"password\":\"hunter2\",\"note\":\"{new string('x', 300)}\"}}";
        await messages.PublishAsync("invoices", payload, contentType: "application/json");
        await messages.AwaitAsync(
            "invoices",
            message => message.Destination == "invoices",
            TimeSpan.FromSeconds(2));

        var publish = context.Attachments.Single(item =>
            item.Name.EndsWith("message-publish-invoices-1-payload", StringComparison.Ordinal));
        var receive = context.Attachments.Single(item =>
            item.Name.EndsWith("message-receive-invoices-2-payload", StringComparison.Ordinal));
        var publishText = Encoding.UTF8.GetString(await publish.ReadAllBytesAsync());
        var receiveText = Encoding.UTF8.GetString(await receive.ReadAllBytesAsync());

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        Assert.Multiple(() =>
        {
            Assert.That(
                context.Attachments.Select(item => item.Name.Split('-', 2)[1]),
                Is.EqualTo(new[]
                {
                    "message-publish-invoices-1-payload",
                    "message-receive-invoices-2-payload"
                }), "the per-client sequence keeps repeated captures distinct");
            Assert.That(publish.MediaType, Is.EqualTo("application/json"));
            Assert.That(receive.MediaType, Is.EqualTo("application/json"));
            Assert.That(publishText, Does.Contain("[REDACTED]"));
            Assert.That(publishText, Does.Not.Contain("hunter2"));
            Assert.That(publishText, Does.Contain("characters truncated"));
            Assert.That(receiveText, Does.Not.Contain("hunter2"));
        });
    }

    [Test]
    public async Task CaptureAttachments_ShouldKeepEveryRepeatedCaptureDistinct()
    {
        var builder = new ProtoHostBuilder();
        builder.AddMessaging(messaging => messaging.CaptureAttachments());
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("messaging repeated captures", TestMethod());
        var messages = context.Messaging();

        await messages.PublishAsync("invoices", "{\"id\":1}", contentType: "application/json");
        await messages.PublishAsync("invoices", "{\"id\":2}", contentType: "application/json");
        await messages.AwaitAsync(
            "invoices",
            message => message.Payload == "{\"id\":1}",
            TimeSpan.FromSeconds(2));
        await messages.AwaitAsync(
            "invoices",
            message => message.Payload == "{\"id\":2}",
            TimeSpan.FromSeconds(2));

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        Assert.That(
            context.Attachments.Select(item => item.Name.Split('-', 2)[1]),
            Is.EqualTo(new[]
            {
                "message-publish-invoices-1-payload",
                "message-publish-invoices-2-payload",
                "message-receive-invoices-3-payload",
                "message-receive-invoices-4-payload"
            }), "every publish and receive to the same destination survives");
    }

    [Test]
    public async Task CaptureAttachments_ShouldCaptureAReceiveEvenWhenThePredicateMatchesMany()
    {
        var builder = new ProtoHostBuilder();
        builder.AddMessaging(messaging => messaging.CaptureAttachments());
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("messaging receive sequence", TestMethod());
        var messages = context.Messaging();

        await messages.PublishAsync("invoices", "{\"id\":1}", contentType: "application/json");
        await messages.PublishAsync("invoices", "{\"id\":2}", contentType: "application/json");
        await messages.AwaitAsync("invoices", _ => true, TimeSpan.FromSeconds(2));
        await messages.AwaitAsync("invoices", _ => true, TimeSpan.FromSeconds(2));

        var receives = context.Attachments
            .Where(item => item.Name.Contains("message-receive-invoices-"))
            .OrderBy(item => item.Name, StringComparer.Ordinal)
            .ToArray();
        var received = new List<string>();
        foreach (var receive in receives)
        {
            received.Add(Encoding.UTF8.GetString(await receive.ReadAllBytesAsync()));
        }

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        Assert.Multiple(() =>
        {
            Assert.That(receives, Has.Length.EqualTo(2));
            Assert.That(received, Is.EqualTo(new[] { "{\"id\":1}", "{\"id\":2}" }),
                "each await consumes the next match instead of re-delivering the first");
        });
    }

    [Test]
    public async Task CaptureAttachments_WhenNotConfigured_ShouldNotAttach()
    {
        var builder = new ProtoHostBuilder();
        builder.AddMessaging();
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("messaging no attachments", TestMethod());
        var messages = context.Messaging();

        await messages.PublishAsync("invoices", "{\"id\":1}", contentType: "application/json");
        await messages.AwaitAsync(
            "invoices",
            message => message.Destination == "invoices",
            TimeSpan.FromSeconds(2));

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        Assert.That(context.Attachments, Is.Empty);
    }

    [Test]
    public async Task PublishedCapture_ShouldNotHappenWhenTheBrokerCallFails()
    {
        var broker = new FailingBroker();
        var builder = new ProtoHostBuilder();
        builder.AddMessaging(messaging =>
        {
            messaging.UseBroker(_ => broker);
            messaging.CaptureAttachments();
        });
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("messaging failed publish", TestMethod());
        var messages = context.Messaging();

        var failure = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await messages.PublishAsync("invoices", "{\"id\":1}", contentType: "application/json"));

        await host.CompleteTestAsync(ProtoTestResult.Failed(failure!));
        Assert.That(context.Attachments, Is.Empty, "a payload that was never published is not captured");
    }

    [Test]
    public async Task CaptureFailure_ShouldNotFailTheOperation()
    {
        var builder = new ProtoHostBuilder();
        builder.AddMessaging(messaging => messaging.CaptureAttachments());
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("messaging capture failure", TestMethod());
        // Occupy the name the first capture will use, forcing the capture to fail.
        context.AddAttachment("message-publish-invoices-1-payload", "occupied");
        var messages = context.Messaging();

        await messages.PublishAsync("invoices", "{\"id\":1}", contentType: "application/json");

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        var entries = host.Trace.Snapshot().Tests.Single().Entries;
        Assert.Multiple(() =>
        {
            Assert.That(entries.Any(entry => entry.Kind == "messaging.attachment.failed"
                && entry.Outcome == ProtoTraceOutcome.Failed), Is.True,
                "the capture failure is on the trace");
            Assert.That(entries.Single(entry => entry.Kind == "messaging.publish").Outcome,
                Is.EqualTo(ProtoTraceOutcome.Partial),
                "the publish itself still succeeded");
        });
    }

    [Test]
    public async Task CaptureReceivedPayloads_FalseInConfiguration_ShouldOnlyAttachThePublishedPayload()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ProtoTest:Messaging:Attachments:CaptureReceivedPayloads"] = "false"
            }));
        builder.AddMessaging(messaging => messaging.CaptureAttachments());
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("messaging receive off", TestMethod());
        var messages = context.Messaging();

        await messages.PublishAsync("invoices", "{\"id\":1}", contentType: "application/json");
        await messages.AwaitAsync(
            "invoices",
            message => message.Destination == "invoices",
            TimeSpan.FromSeconds(2));

        var options = context.Service<MessagingAttachmentOptions>();
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        Assert.Multiple(() =>
        {
            Assert.That(options.CapturePublishedPayloads, Is.True);
            Assert.That(options.CaptureReceivedPayloads, Is.False);
            Assert.That(
                context.Attachments.Select(item => item.Name.Split('-', 2)[1]),
                Is.EqualTo(new[] { "message-publish-invoices-1-payload" }));
        });
    }

    [Test]
    public async Task NonJsonPayload_ShouldBeCapturedAsText()
    {
        var builder = new ProtoHostBuilder();
        builder.AddMessaging(messaging => messaging.CaptureAttachments());
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("messaging text payload", TestMethod());
        var messages = context.Messaging();

        await messages.PublishAsync("invoices", "not-json-payload");
        await messages.AwaitAsync(
            "invoices",
            message => message.Payload == "not-json-payload",
            TimeSpan.FromSeconds(2));

        var publish = context.Attachments.Single(item =>
            item.Name.EndsWith("message-publish-invoices-1-payload", StringComparison.Ordinal));
        var receive = context.Attachments.Single(item =>
            item.Name.EndsWith("message-receive-invoices-2-payload", StringComparison.Ordinal));
        var publishText = Encoding.UTF8.GetString(await publish.ReadAllBytesAsync());
        var receiveText = Encoding.UTF8.GetString(await receive.ReadAllBytesAsync());

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        Assert.Multiple(() =>
        {
            Assert.That(publishText, Is.EqualTo("not-json-payload"));
            Assert.That(receiveText, Is.EqualTo("not-json-payload"));
            Assert.That(publish.MediaType, Is.EqualTo("text/plain"));
            Assert.That(receive.MediaType, Is.EqualTo("text/plain"));
        });
    }

    [Test]
    public async Task TraceMessageSection_ShouldRedactSensitiveJson()
    {
        var builder = new ProtoHostBuilder();
        builder.AddMessaging();
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("messaging trace redaction", TestMethod());
        var messages = context.Messaging();

        await messages.PublishAsync(
            "invoices",
            "{\"password\":\"hunter2\",\"id\":1}",
            contentType: "application/json");
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        var entry = host.Trace.Snapshot().Tests.Single().Entries
            .Single(item => item.Kind == "messaging.publish");
        var content = entry.Sections!.Single(section => section.Label == "Message").Content;
        Assert.Multiple(() =>
        {
            Assert.That(content, Does.Contain("[REDACTED]"));
            Assert.That(content, Does.Not.Contain("hunter2"),
                "trace and attachments share the same redaction rules");
        });
    }

    private sealed class FailingBroker : IProtoMessageBroker
    {
        public string Name => "Failing";

        public ValueTask PublishAsync(ProtoMessage message, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("The broker is down.");

        public ValueTask<IProtoMessageConsumer> CreateConsumerAsync(CancellationToken cancellationToken = default)
            => new(new FailingConsumer());

        private sealed class FailingConsumer : IProtoMessageConsumer
        {
            public ValueTask PrepareAsync(
                IReadOnlyCollection<string> destinations,
                CancellationToken cancellationToken = default)
                => ValueTask.CompletedTask;

            public ValueTask<ProtoMessage> AwaitAsync(
                string destination,
                Func<ProtoMessage, bool> predicate,
                TimeSpan timeout,
                CancellationToken cancellationToken = default)
                => throw new TimeoutException("The fake broker never has messages.");

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private static MethodInfo TestMethod()
        => typeof(MessagingAttachmentTests).GetMethod(nameof(Placeholder), BindingFlags.Static | BindingFlags.NonPublic)!;

    private static void Placeholder()
    {
    }
}
