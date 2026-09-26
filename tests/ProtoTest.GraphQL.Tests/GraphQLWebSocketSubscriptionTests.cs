namespace ProtoTest.GraphQL.Tests;

using System.Net;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;
using ProtoTest.Http.Authenticators;

[TestFixture]
public sealed class GraphQLWebSocketSubscriptionTests
{
    [Test]
    public async Task Subscription_ShouldUseGraphQLTransportWsProtocol()
    {
        var socket = new StubWebSocket(
            """{"type":"connection_ack"}""",
            """{"type":"ping","payload":{"probe":"ready"}}""",
            """{"id":"1","type":"next","payload":{"data":{"orderCreated":{"id":42,"status":"pending"}}}}""",
            """{"id":"1","type":"complete"}""");
        var factory = new StubWebSocketFactory(socket);
        var builder = new ProtoHostBuilder();
        builder.AddGraphQL(graphQL => graphQL.AddClient("Default", "https://example.test/graphql"));
        builder.ConfigureServices(services => services.AddSingleton<IGraphQLWebSocketFactory>(factory));
        await using var host = builder.Build();
        await host.StartTestAsync("websocket", "1", TestMethods.Placeholder);
        try
        {
            await using var subscription = await Proto.Context.GraphQL()
                .Auth(new BearerTokenAuthenticator("secret"))
                .ConnectionPayload(new { token = "init-secret" })
                .Subscription("orderCreated")
                .Select(new { id = Gql.Field, status = Gql.Field })
                .SubscribeAsync();

            using var message = await subscription.ExpectNextAsync(new { id = 42, status = "pending" });
            message.Should.HaveNoErrors();
            Assert.That(await subscription.NextAsync(), Is.Null);

            Assert.Multiple(() =>
            {
                Assert.That(subscription.Transport, Is.EqualTo(GraphQLSubscriptionTransport.WebSocket));
                Assert.That(factory.Endpoint, Is.EqualTo(new Uri("wss://example.test/graphql")));
                Assert.That(factory.Headers!["Authorization"], Is.EqualTo("Bearer secret"));
                Assert.That(socket.Sent[0], Does.Contain("\"type\":\"connection_init\""));
                Assert.That(socket.Sent[0], Does.Contain("\"token\":\"init-secret\""));
                Assert.That(socket.Sent[1], Does.Contain("\"type\":\"subscribe\""));
                Assert.That(socket.Sent[1], Does.Contain("subscription OrderCreated"));
                Assert.That(socket.Sent, Has.Some.Contains("\"type\":\"pong\""));
            });
        }
        finally { await host.CompleteTestAsync(); }
    }

    [Test]
    public async Task Configuration_ShouldAllowSseWithoutChangingTheRequestApi()
    {
        string? accept = null;
        var builder = new ProtoHostBuilder();
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ProtoTest:Applications:Default:GraphQL:SubscriptionTransport"] = "Sse"
            }));
        builder.AddGraphQL(graphQL => graphQL.AddClient("Default", "https://example.test/graphql", http =>
            http.ConfigurePrimaryHttpMessageHandler(() => new StubHttpHandler(request =>
            {
                accept = request.Headers.Accept.Single().MediaType;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "event: complete\ndata:\n\n",
                        Encoding.UTF8,
                        "text/event-stream")
                };
            }))));
        await using var host = builder.Build();
        await host.StartTestAsync("sse-configuration", "2", TestMethods.Placeholder);
        try
        {
            await using var subscription = await Proto.Context.GraphQL()
                .Subscription("finished")
                .Select(new { id = Gql.Field })
                .SubscribeAsync();

            Assert.That(await subscription.NextAsync(), Is.Null);
            Assert.Multiple(() =>
            {
                Assert.That(subscription.Transport, Is.EqualTo(GraphQLSubscriptionTransport.Sse));
                Assert.That(accept, Is.EqualTo("text/event-stream"));
            });
        }
        finally { await host.CompleteTestAsync(); }
    }

    [Test]
    public async Task WithSubscriptionTransport_CalledTwice_KeepsTheFirstRegistration()
    {
        var builder = new ProtoHostBuilder();
        builder.AddGraphQL(graphQL =>
        {
            graphQL.AddClient("Default", "https://example.test/graphql", http =>
                http.ConfigurePrimaryHttpMessageHandler(() => new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "event: complete\ndata:\n\n",
                        Encoding.UTF8,
                        "text/event-stream")
                })))
                .WithSubscriptionTransport(GraphQLSubscriptionTransport.Sse)
                .WithSubscriptionTransport(GraphQLSubscriptionTransport.WebSocket);
        });
        await using var host = builder.Build();
        await host.StartTestAsync("first-transport-wins", "3", TestMethods.Placeholder);
        try
        {
            await using var subscription = await Proto.Context.GraphQL()
                .Subscription("finished")
                .Select(new { id = Gql.Field })
                .SubscribeAsync();

            Assert.That(await subscription.NextAsync(), Is.Null);
            Assert.That(subscription.Transport, Is.EqualTo(GraphQLSubscriptionTransport.Sse));
        }
        finally { await host.CompleteTestAsync(); }
    }

    [Test]
    public async Task SseSubscription_ShouldRejectAFrameLargerThanTheConfiguredResponseLimit()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ProtoTest:Applications:Default:GraphQL:SubscriptionTransport"] = "Sse"
            }));
        builder.AddGraphQL(graphQL =>
        {
            graphQL.ConfigureResponses(options => options.MaxResponseBodyBytes = 32);
            graphQL.AddClient("Default", "https://example.test/graphql", http =>
                http.ConfigurePrimaryHttpMessageHandler(() => new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        $"event: next\ndata: {{\"data\":{{\"value\":\"{new string('x', 128)}\"}}}}\n\n",
                        Encoding.UTF8,
                        "text/event-stream")
                })));
        });
        await using var host = builder.Build();
        await host.StartTestAsync("sse-limit", "4", TestMethods.Placeholder);
        try
        {
            await using var subscription = await Proto.Context.GraphQL()
                .Subscription("orderCreated")
                .Select(new { id = Gql.Field })
                .SubscribeAsync();

            var exception = Assert.ThrowsAsync<GraphQLProtocolException>(() => subscription.NextAsync());

            Assert.That(exception!.Message, Does.Contain("exceeded the configured limit"));
        }
        finally { await host.CompleteTestAsync(); }
    }

    [Test]
    public async Task Subscription_WhenTheServerRejectsTheOperation_ShouldNameTheServerError()
    {
        // A rejected subscription arrives as an errors-only response; the shape
        // assertion names the server's message instead of only "the response did not contain data".
        var socket = new StubWebSocket(
            """{"type":"connection_ack"}""",
            """{"id":"1","type":"error","payload":[{"message":"The field `orderCreated` does not exist on the type `Subscription`."}]}""");
        var builder = new ProtoHostBuilder();
        builder.AddGraphQL(graphQL => graphQL.AddClient("Default", "https://example.test/graphql"));
        builder.ConfigureServices(services => services.AddSingleton<IGraphQLWebSocketFactory>(
            new StubWebSocketFactory(socket)));
        await using var host = builder.Build();
        await host.StartTestAsync("websocket-error-frame", "5", TestMethods.Placeholder);
        try
        {
            await using var subscription = await Proto.Context.GraphQL()
                .Subscription("orderCreated")
                .Select(new { id = Gql.Field })
                .SubscribeAsync();

            var exception = Assert.ThrowsAsync<GraphQLAssertionException>(() =>
                subscription.ExpectNextAsync(new { id = 42 }));

            Assert.Multiple(() =>
            {
                Assert.That(exception!.Message, Does.Contain("The field `orderCreated` does not exist"));
                Assert.That(exception.Message, Does.Contain("Server errors:"));
            });
        }
        finally { await host.CompleteTestAsync(); }
    }

    [Test]
    public async Task Subscription_ShouldExplainRejectedWebSocketHandshake()
    {
        var socket = new StubWebSocket(
            """{"type":"connection_error","payload":{"message":"Denied"}}""");
        var builder = new ProtoHostBuilder();
        builder.AddGraphQL(graphQL => graphQL.AddClient("Default", "https://example.test/graphql"));
        builder.ConfigureServices(services => services.AddSingleton<IGraphQLWebSocketFactory>(
            new StubWebSocketFactory(socket)));
        await using var host = builder.Build();
        await host.StartTestAsync("websocket-rejected", "3", TestMethods.Placeholder);
        try
        {
            var exception = Assert.ThrowsAsync<GraphQLProtocolException>(() => Proto.Context.GraphQL()
                .Subscription("restricted")
                .Select(new { id = Gql.Field })
                .SubscribeAsync());

            Assert.Multiple(() =>
            {
                Assert.That(exception!.Message, Does.Contain("rejected"));
                Assert.That(exception.ResponseContent, Does.Contain("Denied"));
                Assert.That(socket.State, Is.EqualTo(WebSocketState.Closed));
            });
        }
        finally { await host.CompleteTestAsync(); }
    }


    [Test]
    public async Task Enumeration_ShouldDisposeEachEventAsItAdvances()
    {
        // An await foreach without a per-element using must not leak a document
        // and response per event; the enumerator disposes the previous event when the next arrives.
        var socket = new StubWebSocket(
            """{"type":"connection_ack"}""",
            """{"id":"1","type":"next","payload":{"data":{"value":1}}}""",
            """{"id":"1","type":"next","payload":{"data":{"value":2}}}""",
            """{"id":"1","type":"complete"}""");
        var builder = new ProtoHostBuilder();
        builder.AddGraphQL(graphQL => graphQL.AddClient("Default", "https://example.test/graphql"));
        builder.ConfigureServices(services => services.AddSingleton<IGraphQLWebSocketFactory>(
            new StubWebSocketFactory(socket)));
        await using var host = builder.Build();
        await host.StartTestAsync("websocket enumeration", "6", TestMethods.Placeholder);
        try
        {
            await using var subscription = await Proto.Context.GraphQL()
                .Subscription("orderCreated")
                .Select(new { value = Gql.Field })
                .SubscribeAsync();

            var events = new List<GraphQLResponse>();
            await foreach (var response in subscription)
            {
                events.Add(response);
            }

            using (Assert.EnterMultipleScope())
            {
                Assert.That(events, Has.Count.EqualTo(2));
                Assert.That(
                    Assert.ThrowsAsync<ObjectDisposedException>(
                        () => events[0].RawResponse.Content.ReadAsStringAsync()),
                    Is.Not.Null,
                    "the previous event is disposed when the enumerator advances");
                Assert.That(events[1].ReadDataAs<int>("$.value"), Is.EqualTo(2),
                    "the last event stays with the caller, like an event from NextAsync");
            }
            events[1].Dispose();
        }
        finally { await host.CompleteTestAsync(); }
    }

    private sealed class StubWebSocketFactory(StubWebSocket socket) : IGraphQLWebSocketFactory
    {
        public Uri? Endpoint { get; private set; }
        public IReadOnlyDictionary<string, string>? Headers { get; private set; }

        public ValueTask<WebSocket> ConnectAsync(
            Uri endpoint,
            IReadOnlyDictionary<string, string> headers,
            CancellationToken cancellationToken = default)
        {
            Endpoint = endpoint;
            Headers = headers;
            return ValueTask.FromResult<WebSocket>(socket);
        }
    }

    private sealed class StubWebSocket(params string[] messages) : WebSocket
    {
        private readonly Queue<byte[]> _messages = new(messages.Select(Encoding.UTF8.GetBytes));
        private WebSocketState _state = WebSocketState.Open;
        public List<string> Sent { get; } = [];
        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override string SubProtocol => "graphql-transport-ws";
        public override WebSocketState State => _state;

        public override void Abort() => _state = WebSocketState.Aborted;
        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
        {
            _state = WebSocketState.Closed;
            return Task.CompletedTask;
        }
        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
        {
            _state = WebSocketState.CloseSent;
            return Task.CompletedTask;
        }
        public override void Dispose() => _state = WebSocketState.Closed;
        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
        {
            if (_messages.Count == 0)
                return Task.FromResult(new WebSocketReceiveResult(0, WebSocketMessageType.Close, true));
            var message = _messages.Dequeue();
            message.CopyTo(buffer.Array!, buffer.Offset);
            return Task.FromResult(new WebSocketReceiveResult(message.Length, WebSocketMessageType.Text, true));
        }
        public override Task SendAsync(
            ArraySegment<byte> buffer,
            WebSocketMessageType messageType,
            bool endOfMessage,
            CancellationToken cancellationToken)
        {
            Sent.Add(Encoding.UTF8.GetString(buffer.Array!, buffer.Offset, buffer.Count));
            return Task.CompletedTask;
        }
    }
}
