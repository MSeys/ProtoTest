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
        await host.StartTestAsync("websocket", "1", Method());
        try
        {
            await using var subscription = await Proto.Context.GraphQL()
                .Auth(new BearerTokenAuthenticator("secret"))
                .ConnectionPayload(new { token = "init-secret" })
                .Subscription("orderCreated")
                .Select(new { id = Gql.Field, status = Gql.Field })
                .SubscribeAsync();

            using var message = await subscription.ExpectNextAsync(new { id = 42, status = "pending" });
            message.ShouldHaveNoErrors();
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
                ["ProtoTest:Clients:Default:GraphQL:SubscriptionTransport"] = "Sse"
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
        await host.StartTestAsync("sse-configuration", "2", Method());
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
    public async Task Subscription_ShouldExplainRejectedWebSocketHandshake()
    {
        var socket = new StubWebSocket(
            """{"type":"connection_error","payload":{"message":"Denied"}}""");
        var builder = new ProtoHostBuilder();
        builder.AddGraphQL(graphQL => graphQL.AddClient("Default", "https://example.test/graphql"));
        builder.ConfigureServices(services => services.AddSingleton<IGraphQLWebSocketFactory>(
            new StubWebSocketFactory(socket)));
        await using var host = builder.Build();
        await host.StartTestAsync("websocket-rejected", "3", Method());
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

    private static MethodInfo Method() => typeof(GraphQLWebSocketSubscriptionTests)
        .GetMethod(nameof(Dummy), BindingFlags.NonPublic | BindingFlags.Static)!;
    private static void Dummy() { }

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

    private sealed class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(response(request));
    }
}
