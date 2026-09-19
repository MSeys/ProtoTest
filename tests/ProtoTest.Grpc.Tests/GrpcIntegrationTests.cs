namespace ProtoTest.Grpc.Tests;

using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using global::Grpc.Core;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using ProtoTest.Core;
using ProtoTest.Grpc.Tests.Echo;
using ProtoTest.Http;
using ProtoTest.Http.Authenticators;

[TestFixture]
public sealed class GrpcIntegrationTests
{
    private static readonly Marshaller<EchoRequest> RequestMarshaller = Marshallers.Create<EchoRequest>(
        (request, context) => context.Complete(request.ToByteArray()),
        context => EchoRequest.Parser.ParseFrom(context.PayloadAsNewBuffer()));
    private static readonly Marshaller<EchoReply> ReplyMarshaller = Marshallers.Create<EchoReply>(
        (reply, context) => context.Complete(reply.ToByteArray()),
        context => EchoReply.Parser.ParseFrom(context.PayloadAsNewBuffer()));
    private static readonly Method<EchoRequest, EchoReply> SayMethod = new(
        MethodType.Unary, "prototest.echo.Echo", "Say", RequestMarshaller, ReplyMarshaller);
    private static readonly Method<EchoRequest, EchoReply> StreamMethod = new(
        MethodType.ServerStreaming, "prototest.echo.Echo", "Stream", RequestMarshaller, ReplyMarshaller);
    private static readonly Method<EchoRequest, EchoReply> CollectMethod = new(
        MethodType.ClientStreaming, "prototest.echo.Echo", "Collect", RequestMarshaller, ReplyMarshaller);
    private static readonly Method<EchoRequest, EchoReply> ChatMethod = new(
        MethodType.DuplexStreaming, "prototest.echo.Echo", "Chat", RequestMarshaller, ReplyMarshaller);

    private static WebApplication _server = null!;
    private static string _address = null!;

    [OneTimeSetUp]
    public async Task StartServer()
    {
        var port = FreePort();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(
            IPAddress.Loopback,
            port,
            endpoint => endpoint.Protocols = HttpProtocols.Http2));
        builder.Services.AddGrpc();
        _server = builder.Build();
        _server.MapGrpcService<EchoService>();
        await _server.StartAsync();
        _address = $"http://127.0.0.1:{port}";
    }

    [OneTimeTearDown]
    public async Task StopServer() => await _server.DisposeAsync();

    [Test]
    public async Task UnaryCall_ShouldTraceReportAndCarryMetadata()
    {
        var builder = new ProtoHostBuilder();
        builder.AddGrpc(grpc => grpc.AddClient("Echo", _address).AddCollector<GrpcCoverageCollector>());
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("grpc unary", TestMethod());
        var client = context.Grpc("Echo");

        var reply = await client.UnaryAsync(
            SayMethod,
            new EchoRequest { Message = "hello" },
            metadata => metadata.Add("authorization", "Bearer secret"));

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
            reply.ShouldMatchShape(new { message = "hello" });
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
            Assert.That(entity.State["client.address"], Does.StartWith(_address));
            Assert.That(entity.State["client.protocol"], Is.EqualTo("Grpc"));
            Assert.That(entity.State["client.initializer"], Is.EqualTo("ProtoGrpcClientInitializer"));
        });
    }

    [Test]
    public async Task AuthAttribute_ShouldApplyMetadataThroughTheSharedPipeline()
    {
        var builder = new ProtoHostBuilder();
        builder.AddGrpc(grpc => grpc.AddClient("Echo", _address));
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("grpc auth", AuthenticatedTestMethod());
        var client = context.Grpc("Echo");

        var reply = await client.UnaryAsync(SayMethod, new EchoRequest { Message = "auth" });

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
    public async Task RawServerStreaming_ShouldApplyAuthMetadataWithoutTracing()
    {
        var builder = new ProtoHostBuilder();
        builder.AddGrpc(grpc => grpc.AddClient("Echo", _address));
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("grpc raw stream auth", AuthenticatedTestMethod());
        var client = context.Grpc("Echo");

        using var call = client.ServerStreaming(StreamMethod, new EchoRequest { Message = "a, b" });
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
        builder.AddGrpc(grpc => grpc.AddClient("Echo", _address));
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("grpc stream", TestMethod());
        var client = context.Grpc("Echo");

        var replies = await client.ServerStreamingAsync(StreamMethod, new EchoRequest { Message = "a, b, c" });

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        Assert.That(replies.Select(reply => reply.Message), Is.EqualTo(new[] { "a", "b", "c" }));
    }

    [Test]
    public async Task ClientStreaming_ShouldCollectEveryMessage()
    {
        var builder = new ProtoHostBuilder();
        builder.AddGrpc(grpc => grpc.AddClient("Echo", _address));
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("grpc client stream", TestMethod());
        var client = context.Grpc("Echo");

        var reply = await client.ClientStreamingAsync(
            CollectMethod,
            [new EchoRequest { Message = "x" }, new EchoRequest { Message = "y" }]);

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        Assert.That(reply.Message, Is.EqualTo("x+y"));
    }

    [Test]
    public async Task DuplexStreaming_ShouldExchangeInBothDirections()
    {
        var builder = new ProtoHostBuilder();
        builder.AddGrpc(grpc => grpc.AddClient("Echo", _address));
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("grpc duplex", TestMethod());
        var client = context.Grpc("Echo");

        using var call = client.DuplexStreaming(ChatMethod);
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
        builder.AddGrpc(grpc => grpc.AddClient("Echo", _address));
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("grpc async duplex", TestMethod());
        var client = context.Grpc("Echo");

        using var call = await client.OpenDuplexStreamingAsync(ChatMethod);
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
            grpc.AddClient("Echo", _address);
        });
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("grpc attachments", TestMethod());
        var client = context.Grpc("Echo");

        await client.UnaryAsync(
            SayMethod,
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
            grpc.AddClient("Echo", _address);
        });
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("grpc attachment cap", TestMethod());
        var client = context.Grpc("Echo");

        await client.UnaryAsync(SayMethod, new EchoRequest { Message = new string('m', 300) });

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
        builder.AddGrpc(grpc => grpc.AddClient("Echo", _address));
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("grpc no attachments", TestMethod());
        var client = context.Grpc("Echo");

        await client.UnaryAsync(SayMethod, new EchoRequest { Message = "plain" });

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
            grpc.AddClient("Echo", _address);
        });
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("grpc repeated attachments", TestMethod());
        var client = context.Grpc("Echo");

        var first = await client.UnaryAsync(SayMethod, new EchoRequest { Message = "one" });
        var second = await client.UnaryAsync(SayMethod, new EchoRequest { Message = "two" });

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
            grpc.AddClient("Echo", _address);
            grpc.AddClient("Mirror", _address);
        });
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("grpc two clients", TestMethod());

        await context.Grpc("Echo").UnaryAsync(SayMethod, new EchoRequest { Message = "one" });
        await context.Grpc("Mirror").UnaryAsync(SayMethod, new EchoRequest { Message = "two" });

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
            grpc.AddClient("Echo", _address);
        });
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("grpc unformattable attachment", TestMethod());
        var client = context.Grpc("Echo");

        var reply = await client.UnaryAsync(
            SayMethod,
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
            grpc.AddClient("Echo", _address);
        });
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("grpc streaming attachments", TestMethod());
        var client = context.Grpc("Echo");

        var request = new EchoRequest
        {
            Message = string.Join(", ", Enumerable.Range(1, 12).Select(index => $"m{index}"))
        };
        var replies = await client.ServerStreamingAsync(StreamMethod, request);
        await client.ClientStreamingAsync(
            CollectMethod,
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
            _address,
            options => options.SensitiveMetadataKeys.Add("x-custom-secret")));
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("grpc metadata redaction", TestMethod());
        var client = context.Grpc("Echo");

        await client.UnaryAsync(
            SayMethod,
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
            services.AddSingleton<IProtoClientInitializer>(new TransportClientInitializer("Echo", _address));
        });
        builder.AddApplication("Echo", app => app.AddGrpc(grpc => grpc.AddClient("Default")));
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("grpc transport", ApplicationTransportTestMethod());
        var client = context.Grpc();

        var reply = await client.UnaryAsync(SayMethod, new EchoRequest { Message = "transport" });

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        Assert.Multiple(() =>
        {
            Assert.That(reply.Message, Is.EqualTo("transport"));
            var entity = host.Trace.Snapshot().Tests.Single().Entities!.Single(candidate =>
                candidate.Kind == ProtoTraceEntityKinds.Client && candidate.Id.Contains("ProtoGrpcClient"));
            Assert.That(entity.State["client.endpoint_source"], Is.EqualTo("deferred"));
        });
    }

    private sealed class TransportClientInitializer(string name, string address) : IProtoClientInitializer<HttpClient>
    {
        public string Name { get; } = name;

        public Task<bool> TryInitializeAsync(ProtoExecutionContext context, CancellationToken cancellationToken = default)
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
        builder.AddGrpc(grpc => grpc.AddClient("Echo", _address));
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("grpc async auth raw stream", AsyncAuthenticatedTestMethod());
        var client = context.Grpc("Echo");

        // The raw synchronous entry point is called on a thread whose synchronization context never
        // pumps: if the blocking part ran there, the async authenticator's continuation would be posted
        // back to the blocked thread and the call would deadlock.
        var call = RunWithNonPumpingContext(
            () => client.ServerStreaming(StreamMethod, new EchoRequest { Message = "a, b" }),
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
            StreamMethod,
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

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static MethodInfo TestMethod()
        => typeof(GrpcIntegrationTests).GetMethod(nameof(Placeholder), BindingFlags.Static | BindingFlags.NonPublic)!;

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

    private static void Placeholder()
    {
    }
}
