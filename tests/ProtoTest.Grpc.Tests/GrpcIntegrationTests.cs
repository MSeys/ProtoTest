namespace ProtoTest.Grpc.Tests;

using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using global::Grpc.Core;
using Google.Protobuf.WellKnownTypes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;
using ProtoTest.Grpc.Tests.Echo;
using ProtoTest.Http;
using ProtoTest.Http.Authenticators;

[TestFixture]
public sealed class GrpcIntegrationTests
{
    [Test]
    public async Task CancelledCall_ShouldRecordACancelledOperation()
    {
        // Stage 3 (Audit 3, finding D3): one cancellation rule; gRPC reports both an OCE and a status
        // code for a cancelled call, and both are recorded as cancelled.
        var builder = new ProtoHostBuilder();
        builder.AddGrpc(grpc => grpc.AddClient("Echo", GrpcTestServer.Address));
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("grpc cancel", TestMethods.Placeholder);
        var client = context.Grpc("Echo");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var exception = Assert.CatchAsync(async () =>
            await client.UnaryAsync(
                EchoMethods.Say,
                new EchoRequest { Message = "hello" },
                cancellationToken: cancellation.Token));

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        var call = host.Trace.Snapshot().Tests.Single().Entries.Single(entry => entry.Kind == "grpc.call");
        Assert.Multiple(() =>
        {
            Assert.That(exception, Is.Not.Null);
            Assert.That(
                call.Outcome,
                Is.EqualTo(ProtoTraceOutcome.Cancelled),
                "a cancelled call is recorded as cancelled");
        });
    }

    [Test]
    public async Task UnaryCall_ShouldTraceReportAndCarryMetadata()
    {
        var builder = new ProtoHostBuilder();
        builder.AddGrpc(grpc => grpc.AddClient("Echo", GrpcTestServer.Address).AddCollector<GrpcCoverageCollector>());
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("grpc unary", TestMethods.Placeholder);
        var client = context.Grpc("Echo");

        var reply = await client.UnaryAsync(
            EchoMethods.Say,
            new EchoRequest { Message = "hello" },
            metadata => metadata.Add("authorization", "Bearer secret"));
        reply.ShouldMatchShape(new { message = "hello" });

        // Resolve coverage before the test completes: the test scope is disposed with the result.
        var coverage = context.Services.GetServices<IProtoCollector>()
            .OfType<GrpcCoverageCollector>()
            .Single()
            .GetReportItems()
            .Single();
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        var test = host.Trace.Snapshot().Tests.Single();
        Assert.Multiple(() =>
        {
            Assert.That(reply.Message, Is.EqualTo("hello"));
            Assert.That(
                test.Entries.Any(entry => entry.Kind == "assert.json.shape" && entry.Outcome == ProtoTraceOutcome.Succeeded),
                Is.True,
                "the shared shape assertion traces with the rest of the call");
            Assert.That(EchoService.LastAuthorization, Is.EqualTo("Bearer secret"));
            var call = test.Entries.Single(entry => entry.Kind == "grpc.call");
            Assert.That(call.Outcome, Is.EqualTo(ProtoTraceOutcome.Succeeded));
            Assert.That(call.Attributes["rpc.service"], Is.EqualTo("prototest.echo.Echo"));
            Assert.That(call.Attributes["rpc.metadata.authorization"], Is.EqualTo("(redacted)"));
            Assert.That(call.Attributes["auth.outcome"], Is.EqualTo("skipped"));
            Assert.That(call.Sections!.Any(section => section.Label == "Response"), Is.True);
            Assert.That(
                context.RecordedObservations.Any(observation => observation.Kind == "grpc.response"),
                Is.True);

            // The registered collector turns those observations into service/method coverage.
            Assert.That(coverage.Identifier, Is.EqualTo("prototest.echo.Echo/Say"));
            Assert.That(coverage.Category, Is.EqualTo("gRPC"));
            Assert.That(coverage.IsCovered, Is.True);
            Assert.That(coverage.Count, Is.EqualTo(1));

            // Core initializes the client during setup, like every other integration's client.
            Assert.That(
                test.Entries.Any(entry => entry.Kind == "client.initialize" && entry.Name.Contains("ProtoGrpcClient")),
                Is.True);
            var entity = test.Entities!.Single(candidate =>
                candidate.Kind == ProtoTraceEntityKinds.Client && candidate.Id.Contains("ProtoGrpcClient"));
            Assert.That(entity.State["client.address"], Does.StartWith(GrpcTestServer.Address));
            Assert.That(entity.State["client.protocol"], Is.EqualTo("Grpc"));
            Assert.That(entity.State["client.initializer"], Is.EqualTo("ProtoGrpcClientInitializer"));
        });
    }

    [Test]
    public async Task ClientName_ShouldFindAHostRegisteredClientInsideAnApplication()
    {
        var builder = new ProtoHostBuilder();
        builder.AddGrpc(grpc => grpc.AddClient("Echo", GrpcTestServer.Address));
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("grpc host client", TestMethods.Placeholder);
        context.SetContext(new ProtoApplicationState("Api", new Dictionary<string, string>()));

        // The host registered "Echo"; inside application "Api" the qualified name "Api:Echo" does not
        // exist, so the explicit name must fall back to the host client instead of the transport.
        var client = context.Grpc("Echo");
        var reply = await client.UnaryAsync(EchoMethods.Say, new EchoRequest { Message = "host" });

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        Assert.That(reply.Message, Is.EqualTo("host"),
            "a host-registered client is reachable by its own name inside an application");
    }

    [Test]
    [NonParallelizable]
    public async Task AuthAttribute_ShouldApplyMetadataThroughTheSharedPipeline()
    {
        var builder = new ProtoHostBuilder();
        builder.AddGrpc(grpc => grpc.AddClient("Echo", GrpcTestServer.Address));
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("grpc auth", AuthenticatedTestMethod());
        var client = context.Grpc("Echo");

        var reply = await client.UnaryAsync(EchoMethods.Say, new EchoRequest { Message = "auth" });

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        var call = host.Trace.Snapshot().Tests.Single().Entries.Single(entry => entry.Kind == "grpc.call");
        Assert.Multiple(() =>
        {
            Assert.That(reply.Message, Is.EqualTo("auth"));
            Assert.That(EchoService.LastAuthorization, Is.EqualTo("Bearer shared-token"));
            Assert.That(call.Attributes["auth.outcome"], Is.EqualTo("applied"));
            Assert.That(call.Attributes["rpc.metadata.authorization"], Is.EqualTo("(redacted)"));
        });
    }

    [Test]
    [NonParallelizable]
    public async Task RawServerStreaming_ShouldApplyAuthMetadataWithoutTracing()
    {
        var builder = new ProtoHostBuilder();
        builder.AddGrpc(grpc => grpc.AddClient("Echo", GrpcTestServer.Address));
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("grpc raw stream auth", AuthenticatedTestMethod());
        var client = context.Grpc("Echo");

        using var call = client.Blocking.ServerStreaming(EchoMethods.Stream, new EchoRequest { Message = "a, b" });
        var replies = new List<string>();
        await foreach (var reply in call.ResponseStream.ReadAllAsync())
        {
            replies.Add(reply.Message);
        }

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        Assert.Multiple(() =>
        {
            Assert.That(replies, Is.EqualTo(new[] { "a", "b" }));
            Assert.That(EchoService.LastStreamAuthorization, Is.EqualTo("Bearer shared-token"));
            // Raw calls stay untraced: no grpc.call span is faked for them.
            Assert.That(
                host.Trace.Snapshot().Tests.Single().Entries.Any(entry => entry.Kind == "grpc.call"),
                Is.False);
        });
    }

    [Test]
    public async Task ServerStreaming_ShouldReturnEveryMessage()
    {
        var builder = new ProtoHostBuilder();
        builder.AddGrpc(grpc => grpc.AddClient("Echo", GrpcTestServer.Address));
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("grpc stream", TestMethods.Placeholder);
        var client = context.Grpc("Echo");

        var replies = await client.ServerStreamingAsync(EchoMethods.Stream, new EchoRequest { Message = "a, b, c" });

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        Assert.That(replies.Select(reply => reply.Message), Is.EqualTo(new[] { "a", "b", "c" }));
    }

    [Test]
    public async Task ClientStreaming_ShouldCollectEveryMessage()
    {
        var builder = new ProtoHostBuilder();
        builder.AddGrpc(grpc => grpc.AddClient("Echo", GrpcTestServer.Address));
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("grpc client stream", TestMethods.Placeholder);
        var client = context.Grpc("Echo");

        var reply = await client.ClientStreamingAsync(
            EchoMethods.Collect,
            [new EchoRequest { Message = "x" }, new EchoRequest { Message = "y" }]);

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        Assert.That(reply.Message, Is.EqualTo("x+y"));
    }

    [Test]
    public async Task DuplexStreaming_ShouldExchangeInBothDirections()
    {
        var builder = new ProtoHostBuilder();
        builder.AddGrpc(grpc => grpc.AddClient("Echo", GrpcTestServer.Address));
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("grpc duplex", TestMethods.Placeholder);
        var client = context.Grpc("Echo");

        using var call = client.Blocking.DuplexStreaming(EchoMethods.Chat);
        await call.RequestStream.WriteAsync(new EchoRequest { Message = "one" });
        await call.RequestStream.WriteAsync(new EchoRequest { Message = "two" });
        await call.RequestStream.CompleteAsync();
        var replies = new List<string>();
        await foreach (var reply in call.ResponseStream.ReadAllAsync())
        {
            replies.Add(reply.Message);
        }

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        Assert.That(replies, Is.EqualTo(new[] { "ONE", "TWO" }));
    }

    [Test]
    public async Task OpenDuplexStreamingAsync_ShouldExchangeInBothDirections()
    {
        var builder = new ProtoHostBuilder();
        builder.AddGrpc(grpc => grpc.AddClient("Echo", GrpcTestServer.Address));
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("grpc async duplex", TestMethods.Placeholder);
        var client = context.Grpc("Echo");

        using var call = await client.OpenDuplexStreamingAsync(EchoMethods.Chat);
        await call.RequestStream.WriteAsync(new EchoRequest { Message = "one" });
        await call.RequestStream.WriteAsync(new EchoRequest { Message = "two" });
        await call.RequestStream.CompleteAsync();
        var replies = new List<string>();
        await foreach (var reply in call.ResponseStream.ReadAllAsync())
        {
            replies.Add(reply.Message);
        }

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        Assert.That(replies, Is.EqualTo(new[] { "ONE", "TWO" }));
    }

    [Test]
    public async Task CaptureAttachments_ShouldRedactSensitiveRequestAndResponseFields()
    {
        var builder = new ProtoHostBuilder();
        builder.AddGrpc(grpc =>
        {
            grpc.CaptureAttachments();
            grpc.AddClient("Echo", GrpcTestServer.Address);
        });
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("grpc attachments", TestMethods.Placeholder);
        var client = context.Grpc("Echo");

        await client.UnaryAsync(
            EchoMethods.Say,
            new EchoRequest { Message = "hello", Password = "hunter2" });

        var request = context.Attachments.Single(item =>
            item.Name.Contains("grpc-Echo-prototest.echo.Echo-Say-request-", StringComparison.Ordinal));
        var response = context.Attachments.Single(item =>
            item.Name.Contains("grpc-Echo-prototest.echo.Echo-Say-response-", StringComparison.Ordinal));
        var requestText = Encoding.UTF8.GetString(await request.ReadAllBytesAsync());
        var responseText = Encoding.UTF8.GetString(await response.ReadAllBytesAsync());

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        Assert.Multiple(() =>
        {
            Assert.That(request.MediaType, Is.EqualTo("application/json"));
            Assert.That(response.MediaType, Is.EqualTo("application/json"));
            Assert.That(request.Description, Does.Contain("Echo").And.Contain("prototest.echo.Echo/Say"));
            Assert.That(requestText, Does.Contain("[REDACTED]"));
            Assert.That(requestText, Does.Not.Contain("hunter2"));
            Assert.That(responseText, Does.Contain("[REDACTED]"));
            Assert.That(responseText, Does.Not.Contain("hunter2"));
        });
    }

    [Test]
    public async Task CaptureAttachments_ShouldCapLongMessages()
    {
        var builder = new ProtoHostBuilder();
        builder.AddGrpc(grpc =>
        {
            grpc.CaptureAttachments(options => options.MaxDiagnosticBodyLength = 64);
            grpc.AddClient("Echo", GrpcTestServer.Address);
        });
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("grpc attachment cap", TestMethods.Placeholder);
        var client = context.Grpc("Echo");

        await client.UnaryAsync(EchoMethods.Say, new EchoRequest { Message = new string('m', 300) });

        var request = context.Attachments.Single(item =>
            item.Name.Contains("grpc-Echo-prototest.echo.Echo-Say-request-", StringComparison.Ordinal));
        var response = context.Attachments.Single(item =>
            item.Name.Contains("grpc-Echo-prototest.echo.Echo-Say-response-", StringComparison.Ordinal));
        var requestText = Encoding.UTF8.GetString(await request.ReadAllBytesAsync());
        var responseText = Encoding.UTF8.GetString(await response.ReadAllBytesAsync());

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        Assert.Multiple(() =>
        {
            Assert.That(requestText, Does.EndWith("…"));
            Assert.That(requestText.Length, Is.LessThan(100));
            Assert.That(responseText, Does.EndWith("…"));
        });
    }

    [Test]
    public async Task CaptureAttachments_WhenNotEnabled_ShouldNotAttach()
    {
        var builder = new ProtoHostBuilder();
        builder.AddGrpc(grpc => grpc.AddClient("Echo", GrpcTestServer.Address));
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("grpc no attachments", TestMethods.Placeholder);
        var client = context.Grpc("Echo");

        await client.UnaryAsync(EchoMethods.Say, new EchoRequest { Message = "plain" });

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        Assert.That(context.Attachments, Is.Empty);
    }

    [Test]
    public async Task CaptureAttachments_RepeatedCalls_ShouldUseDistinctNames()
    {
        var builder = new ProtoHostBuilder();
        builder.AddGrpc(grpc =>
        {
            grpc.CaptureAttachments();
            grpc.AddClient("Echo", GrpcTestServer.Address);
        });
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("grpc repeated attachments", TestMethods.Placeholder);
        var client = context.Grpc("Echo");

        var first = await client.UnaryAsync(EchoMethods.Say, new EchoRequest { Message = "one" });
        var second = await client.UnaryAsync(EchoMethods.Say, new EchoRequest { Message = "two" });

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        var names = context.Attachments.Select(item => item.Name).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(first.Message, Is.EqualTo("one"));
            Assert.That(second.Message, Is.EqualTo("two"));
            Assert.That(names, Has.Length.EqualTo(4));
            Assert.That(names.Distinct(StringComparer.OrdinalIgnoreCase).Count(), Is.EqualTo(4));
            Assert.That(names.Any(name => name.EndsWith("grpc-Echo-prototest.echo.Echo-Say-request-1", StringComparison.Ordinal)), Is.True);
            Assert.That(names.Any(name => name.EndsWith("grpc-Echo-prototest.echo.Echo-Say-response-1", StringComparison.Ordinal)), Is.True);
            Assert.That(names.Any(name => name.EndsWith("grpc-Echo-prototest.echo.Echo-Say-request-2", StringComparison.Ordinal)), Is.True);
            Assert.That(names.Any(name => name.EndsWith("grpc-Echo-prototest.echo.Echo-Say-response-2", StringComparison.Ordinal)), Is.True);
        });
    }

    [Test]
    public async Task CaptureAttachments_TwoClientsOnTheSameMethod_ShouldNotCollide()
    {
        var builder = new ProtoHostBuilder();
        builder.AddGrpc(grpc =>
        {
            grpc.CaptureAttachments();
            grpc.AddClient("Echo", GrpcTestServer.Address);
            grpc.AddClient("Mirror", GrpcTestServer.Address);
        });
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("grpc two clients", TestMethods.Placeholder);

        await context.Grpc("Echo").UnaryAsync(EchoMethods.Say, new EchoRequest { Message = "one" });
        await context.Grpc("Mirror").UnaryAsync(EchoMethods.Say, new EchoRequest { Message = "two" });

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        var names = context.Attachments.Select(item => item.Name).ToArray();
        var entries = host.Trace.Snapshot().Tests.Single().Entries;
        Assert.Multiple(() =>
        {
            Assert.That(names, Has.Length.EqualTo(4), "both clients' request and response attachments survive");
            Assert.That(names.Distinct(StringComparer.OrdinalIgnoreCase).Count(), Is.EqualTo(4),
                "the sanitized client name keeps the two clients' attachment names apart");
            Assert.That(names.Any(name => name.EndsWith("grpc-Echo-prototest.echo.Echo-Say-request-1", StringComparison.Ordinal)), Is.True);
            Assert.That(names.Any(name => name.EndsWith("grpc-Mirror-prototest.echo.Echo-Say-request-1", StringComparison.Ordinal)), Is.True);
            Assert.That(entries.Any(entry => entry.Kind == "grpc.attachment.failed"), Is.False,
                "a collision must not drop the second client's capture");
        });
    }

    [Test]
    public async Task CaptureAttachments_WhenMessageCannotBeFormatted_ShouldNotFailTheCall()
    {
        var builder = new ProtoHostBuilder();
        builder.AddGrpc(grpc =>
        {
            grpc.CaptureAttachments();
            grpc.AddClient("Echo", GrpcTestServer.Address);
        });
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("grpc unformattable attachment", TestMethods.Placeholder);
        var client = context.Grpc("Echo");

        var reply = await client.UnaryAsync(
            EchoMethods.Say,
            new EchoRequest
            {
                Message = "any",
                Payload = new Any { TypeUrl = "type.googleapis.com/unknown.Type" }
            });

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        var test = host.Trace.Snapshot().Tests.Single();
        Assert.Multiple(() =>
        {
            Assert.That(reply.Message, Is.EqualTo("any"));
            Assert.That(
                test.Entries.Single(entry => entry.Kind == "grpc.call").Outcome,
                Is.EqualTo(ProtoTraceOutcome.Partial));
            Assert.That(
                test.Entries.Any(entry => entry.Kind == "grpc.attachment.failed"
                    && entry.Outcome == ProtoTraceOutcome.Failed),
                Is.True);
        });
    }

    [Test]
    public async Task StreamingCapture_ShouldCapMessagesAndRecordCount()
    {
        var builder = new ProtoHostBuilder();
        builder.AddGrpc(grpc =>
        {
            grpc.CaptureAttachments();
            grpc.AddClient("Echo", GrpcTestServer.Address);
        });
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("grpc streaming attachments", TestMethods.Placeholder);
        var client = context.Grpc("Echo");

        var request = new EchoRequest
        {
            Message = string.Join(", ", Enumerable.Range(1, 12).Select(index => $"m{index}"))
        };
        var replies = await client.ServerStreamingAsync(EchoMethods.Stream, request);
        await client.ClientStreamingAsync(
            EchoMethods.Collect,
            Enumerable.Range(1, 12).Select(index => new EchoRequest { Message = $"m{index}" }));

        var streamAttachment = context.Attachments.Single(item =>
            item.Name.Contains("grpc-Echo-prototest.echo.Echo-Stream-response-", StringComparison.Ordinal));
        var collectAttachment = context.Attachments.Single(item =>
            item.Name.Contains("grpc-Echo-prototest.echo.Echo-Collect-request-", StringComparison.Ordinal));
        using var streamJson = JsonDocument.Parse(
            Encoding.UTF8.GetString(await streamAttachment.ReadAllBytesAsync()));
        using var collectJson = JsonDocument.Parse(
            Encoding.UTF8.GetString(await collectAttachment.ReadAllBytesAsync()));

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        var entries = host.Trace.Snapshot().Tests.Single().Entries
            .Where(entry => entry.Kind == "grpc.call")
            .ToArray();
        var streamEntry = entries.Single(entry => entry.Attributes["rpc.method"] == "Stream");
        var collectEntry = entries.Single(entry => entry.Attributes["rpc.method"] == "Collect");
        Assert.Multiple(() =>
        {
            Assert.That(replies, Has.Count.EqualTo(12));
            Assert.That(streamEntry.Attributes["grpc.response.count"], Is.EqualTo("12"));
            Assert.That(collectEntry.Attributes["grpc.request.count"], Is.EqualTo("12"));
            Assert.That(collectEntry.Attributes["grpc.response.count"], Is.EqualTo("1"));
            Assert.That(streamJson.RootElement.GetArrayLength(), Is.EqualTo(10));
            Assert.That(collectJson.RootElement.GetArrayLength(), Is.EqualTo(10));
            Assert.That(streamAttachment.Description, Does.Contain("12 messages"));
            Assert.That(collectAttachment.Description, Does.Contain("12 messages"));
        });
    }

    [Test]
    public async Task SensitiveMetadataKeys_ShouldRedactConfiguredAndDefaultKeys()
    {
        var builder = new ProtoHostBuilder();
        builder.AddGrpc(grpc => grpc.AddClient(
            "Echo",
            GrpcTestServer.Address,
            options => options.SensitiveMetadataKeys.Add("x-custom-secret")));
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("grpc metadata redaction", TestMethods.Placeholder);
        var client = context.Grpc("Echo");

        await client.UnaryAsync(
            EchoMethods.Say,
            new EchoRequest { Message = "metadata" },
            metadata =>
            {
                metadata.Add("authorization", "Bearer secret");
                metadata.Add("x-custom-secret", "top-secret-value");
            });

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        var call = host.Trace.Snapshot().Tests.Single().Entries.Single(entry => entry.Kind == "grpc.call");
        Assert.Multiple(() =>
        {
            Assert.That(call.Attributes["rpc.metadata.authorization"], Is.EqualTo("(redacted)"));
            Assert.That(call.Attributes["rpc.metadata.x-custom-secret"], Is.EqualTo("(redacted)"));
        });
    }

    [Test]
    public async Task ApplicationTransport_ShouldBackTheChannel()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureServices(services =>
        {
            // The application's transport: a client under the application's name, exactly what
            // AddAspNetCoreServer registers, so the deferred channel resolves it at first call.
            services.AddSingleton(new ProtoApplicationTransport("Echo", "Echo"));
            services.AddSingleton<IProtoClientInitializer>(new TransportClientInitializer("Echo", GrpcTestServer.Address));
        });
        builder.AddApplication("Echo", app => app.AddGrpc(grpc => grpc.AddClient("Default")));
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("grpc transport", ApplicationTransportTestMethod());
        var client = context.Grpc();

        var reply = await client.UnaryAsync(EchoMethods.Say, new EchoRequest { Message = "transport" });

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        Assert.Multiple(() =>
        {
            Assert.That(reply.Message, Is.EqualTo("transport"));
            var entity = host.Trace.Snapshot().Tests.Single().Entities!.Single(candidate =>
                candidate.Kind == ProtoTraceEntityKinds.Client && candidate.Id.Contains("ProtoGrpcClient"));
            Assert.That(entity.State["client.endpoint_source"], Is.EqualTo("deferred"));
        });
    }

    [Test]
    public async Task ApplicationTransportFallback_ShouldApplyConfiguredOptions()
    {
        // Stage 3 (Audit 3, finding D2): a client resolved through the application transport keeps the
        // options configured for the protocol instead of starting from defaults. The call names an
        // unregistered client, which is the path that falls back to the transport.
        var builder = new ProtoHostBuilder();
        builder.AddGrpc(grpc => grpc.AddClient(
            "Configured",
            GrpcTestServer.Address,
            configure: options => options.Metadata.Add("x-fallback", "configured")));
        builder.ConfigureServices(services =>
        {
            services.AddSingleton(new ProtoApplicationTransport("Echo", "Echo"));
            services.AddSingleton<IProtoClientInitializer>(new TransportClientInitializer("Echo", GrpcTestServer.Address));
        });
        builder.AddApplication("Echo", _ => { });
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("grpc fallback options", ApplicationTransportTestMethod());
        var client = context.Grpc("Unregistered");

        var reply = await client.UnaryAsync(EchoMethods.Say, new EchoRequest { Message = "options" });

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        var call = host.Trace.Snapshot().Tests.Single().Entries.Single(entry => entry.Kind == "grpc.call");
        Assert.Multiple(() =>
        {
            Assert.That(reply.Message, Is.EqualTo("options"));
            Assert.That(
                call.Attributes["rpc.metadata.x-fallback"],
                Is.EqualTo("configured"),
                "the fallback client carries the configured metadata");
        });
    }

    private sealed class TransportClientInitializer(string name, string address) : IProtoClientInitializer<HttpClient>
    {
        public string Name { get; } = name;

        public Task<bool> TryInitializeAsync(ProtoExecutionContext context)
        {
            var client = new HttpClient { BaseAddress = new Uri(address) };
            context.RegisterClient(client, Name);
            return Task.FromResult(true);
        }
    }

    [Test]
    [CancelAfter(60_000)]
    public async Task RawServerStreaming_WithAnAsynchronousAuthenticator_ShouldNotDeadlock()
    {
        var builder = new ProtoHostBuilder();
        builder.AddGrpc(grpc => grpc.AddClient("Echo", GrpcTestServer.Address));
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("grpc async auth raw stream", AsyncAuthenticatedTestMethod());
        var client = context.Grpc("Echo");

        // The raw synchronous entry point is called on a thread whose synchronization context never
        // pumps: if the blocking part ran there, the async authenticator's continuation would be posted
        // back to the blocked thread and the call would deadlock.
        var call = RunWithNonPumpingContext(
            () => client.Blocking.ServerStreaming(EchoMethods.Stream, new EchoRequest { Message = "a, b" }),
            TimeSpan.FromSeconds(20));
        var replies = new List<string>();
        using (call)
        {
            await foreach (var reply in call.ResponseStream.ReadAllAsync())
            {
                replies.Add(reply.Message);
            }
        }

        // The async raw variant never blocks the caller and works with the same authenticator.
        using var asyncCall = await client.OpenServerStreamingAsync(
            EchoMethods.Stream,
            new EchoRequest { Message = "c" });
        var asyncReplies = new List<string>();
        await foreach (var reply in asyncCall.ResponseStream.ReadAllAsync())
        {
            asyncReplies.Add(reply.Message);
        }

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        Assert.Multiple(() =>
        {
            Assert.That(replies, Is.EqualTo(new[] { "a", "b" }));
            Assert.That(asyncReplies, Is.EqualTo(new[] { "c" }));
            Assert.That(EchoService.LastStreamAuthorization, Is.EqualTo("Bearer async-token"));
        });
    }

    private static T RunWithNonPumpingContext<T>(Func<T> action, TimeSpan timeout)
    {
        var result = default(T);
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new NonPumpingSynchronizationContext());
            try
            {
                result = action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        })
        {
            IsBackground = true
        };
        thread.Start();
        if (!thread.Join(timeout))
        {
            throw new TimeoutException(
                $"The blocking call did not complete within {timeout}; it deadlocked on the synchronization context.");
        }

        if (failure is not null) throw failure;
        return result!;
    }

    private sealed class NonPumpingSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state)
        {
            // Deliberately never runs the callback.
        }
    }

    private sealed class AsynchronousAuthenticator : IProtoHttpAuthenticator
    {
        public async ValueTask AuthenticateAsync(
            ProtoHttpAuthenticationContext context,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(10, cancellationToken).ConfigureAwait(false);
            context.Request.Headers.TryAddWithoutValidation("authorization", "Bearer async-token");
        }
    }

    [Auth<BearerTokenAuthenticator>("shared-token")]
    private static void AuthenticatedPlaceholder()
    {
    }

    private static MethodInfo AuthenticatedTestMethod()
        => typeof(GrpcIntegrationTests).GetMethod(nameof(AuthenticatedPlaceholder), BindingFlags.Static | BindingFlags.NonPublic)!;

    [Auth<AsynchronousAuthenticator>]
    private static void AsyncAuthenticatedPlaceholder()
    {
    }

    private static MethodInfo AsyncAuthenticatedTestMethod()
        => typeof(GrpcIntegrationTests).GetMethod(nameof(AsyncAuthenticatedPlaceholder), BindingFlags.Static | BindingFlags.NonPublic)!;

    [Application("Echo")]
    private static void ApplicationTransportPlaceholder()
    {
    }

    private static MethodInfo ApplicationTransportTestMethod()
        => typeof(GrpcIntegrationTests).GetMethod(nameof(ApplicationTransportPlaceholder), BindingFlags.Static | BindingFlags.NonPublic)!;

}
