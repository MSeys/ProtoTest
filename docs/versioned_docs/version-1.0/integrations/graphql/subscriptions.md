---
sidebar_position: 4
title: Subscriptions
description: "Test GraphQL subscriptions over WebSocket (graphql-transport-ws) or Server-Sent Events, awaiting the next result with a shape and a timeout."
---

# Subscriptions

Subscriptions stream results over **WebSocket** (the `graphql-transport-ws` protocol, the default) or **Server-Sent Events**.

## A complete example

```csharp
[ProtoTest]
[SampleUser]
public async Task SubscriptionStreamsShapeMatchedEvents()
{
    var expected = new
    {
        id = JsonValue.GreaterThan(0),
        product = "live-notebook",
        total = 15m,
        status = "pending"
    };

    await using var subscription = await Proto.Context.GraphQL()
        .Subscription("orderCreated")
        .Select(expected)
        .SubscribeAsync();

    using var created = await Proto.Context.GraphQL()
        .Mutation("createOrder", new
        {
            input = Gql.Variable("CreateOrderInput!", new CreateOrderRequest("live-notebook", 1, 15m))
        })
        .Select(new { id = Gql.Field })
        .ExecuteAsync();
    created.ShouldHaveNoErrors();

    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    using var notification = await subscription.ExpectNextAsync(expected, timeout.Token);
    notification.ShouldHaveNoErrors();
}
```

The same `expected` object builds the subscription's selection set and asserts the event that arrives.

:::tip[Give the server a moment]
`SubscribeAsync()` returns once the server acknowledges the **connection**, not the individual subscription. If you trigger the event immediately afterwards, a fast server can publish it before it has registered the subscriber. The sample suite waits briefly (`await Task.Delay(100)`) before firing the mutation; `PlatformJourney` starts the read first and publishes until it lands.
:::

## `GraphQLSubscription`

```csharp
public sealed class GraphQLSubscription : IAsyncEnumerable<GraphQLResponse>, IAsyncDisposable
{
    bool IsCompleted { get; }
    GraphQLSubscriptionTransport Transport { get; }

    Task<GraphQLResponse?> NextAsync(CancellationToken cancellationToken = default);   // null = stream ended
    Task<GraphQLResponse> ExpectNextAsync<TShape>(TShape expectedShape, CancellationToken cancellationToken = default);
}
```

Every `GraphQLResponse` you receive is yours to dispose. Only one `NextAsync` may be pending at a time — a concurrent call throws `InvalidOperationException`.

Because it's `IAsyncEnumerable`, you can also iterate:

```csharp
await foreach (var message in subscription.WithCancellation(timeout.Token))
{
    using (message)
    {
        message.ShouldHaveNoErrors();
        if (++received == 3) break;
    }
}
```

Always pass a cancellation token — a subscription that never receives an event will otherwise wait forever.

## Connection payload

Many servers authenticate WebSocket connections through the `connection_init` payload rather than headers:

```csharp
await using var subscription = await Proto.Context.GraphQL()
    .ConnectionPayload(new { authToken = token })
    .Subscription("orderCreated")
    .Select(expected)
    .SubscribeAsync();
```

Without `ConnectionPayload`, `connection_init` is sent with a `null` payload.

## Protocol behaviour

**WebSocket:** ProtoTest sends `connection_init` (with your payload), waits for `connection_ack` — answering server `ping`s with `pong`, echoing the ping payload when there is one — then sends `subscribe`. `next` messages become responses, `complete` ends the stream, and an `error` message is delivered as a final response with errors. `connection_error`, an unexpected message before the ack, or a non-text or oversized message throws `GraphQLProtocolException`.

**SSE:** `event: next`, `event: complete` and `event: error` frames map the same way. A response that isn't `text/event-stream` is read to the end within the message limit and delivered as one response.

Each event is recorded: a `graphql.response` observation and a `graphql.subscription.next` event (event number, transport, error count), with response attachments named `graphql-{n:00}-event-{nn}-response`. The end of the stream records `graphql.subscription.complete` with the event count, transport and duration.

**Disposal** sends `complete`, then closes the socket. Socket errors during close are swallowed so they never mask a test failure.

The endpoint scheme is rewritten automatically: `http` → `ws`, `https` → `wss`.

Message size is capped by `ProtoTest:GraphQL:Responses:MaxResponseBodyBytes` (10 MiB default) on both transports.

## Choosing the transport

```csharp
graphQL.AddClient("Api", "https://api.example.test/graphql")
    .WithSubscriptionTransport(GraphQLSubscriptionTransport.Sse);
```

or `ProtoTest:Applications:Api:GraphQL:SubscriptionTransport = "Sse"` in configuration. The per-target registration wins; otherwise the configured value is read the first time the test calls `GraphQL()`. An invalid value throws `InvalidOperationException` naming `WebSocket` and `Sse`.

## Supplying your own WebSocket

By default ProtoTest opens a real `ClientWebSocket`. An in-process test server can't be reached that way, so you register a factory:

```csharp
public interface IGraphQLWebSocketFactory
{
    ValueTask<WebSocket> ConnectAsync(
        Uri endpoint,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken cancellationToken = default);
}
```

The returned socket must already have negotiated the `graphql-transport-ws` subprotocol. The sample suite connects straight to ASP.NET Core's `TestServer`:

```csharp
using System.Net.WebSockets;
using ProtoTest.AspNetCore;
using ProtoTest.Core;
using ProtoTest.GraphQL;

internal sealed class SampleAppGraphQLWebSocketFactory : IGraphQLWebSocketFactory
{
    public async ValueTask<WebSocket> ConnectAsync(
        Uri endpoint,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken cancellationToken = default)
    {
        var client = Proto.Context.ServerFactory<Program>("Api").Server.CreateWebSocketClient();
        client.SubProtocols.Add("graphql-transport-ws");
        client.ConfigureRequest = request =>
        {
            foreach (var header in headers)
                request.Headers[header.Key] = header.Value;
        };
        return await client.ConnectAsync(endpoint, cancellationToken);
    }
}
```

```csharp
builder.ConfigureServices(services =>
    services.AddSingleton<IGraphQLWebSocketFactory, SampleAppGraphQLWebSocketFactory>());
```

Your registration replaces the default one.
