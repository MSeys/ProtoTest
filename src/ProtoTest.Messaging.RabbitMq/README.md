# ProtoTest.Messaging.RabbitMq

The RabbitMQ adapter for the messaging capability: publish to exchanges and tap events through a per-test queue per awaited destination.

```bash
dotnet add package ProtoTest.Messaging.RabbitMq
```

## Quick start

```csharp
builder.AddMessaging(messaging => messaging.UseRabbitMq(options =>
{
    // Or set ProtoTest:Messaging:RabbitMq:ConnectionString for the environment.
    options.ConnectionString = "amqp://guest:guest@localhost:5672/";
}));
```

For a broker the run owns, add the container as infrastructure instead — the started connection string reaches both the adapter and an in-process application, and an explicitly configured string still wins:

```csharp
builder.AddInfrastructure(RabbitMqBroker.Container(),
    RabbitMqOptions.ConnectionStringSetting, "Messaging:RabbitMq:ConnectionString");
builder.AddMessaging(messaging => messaging.UseRabbitMq());
```

## What it adds

- **Adapter** — `UseRabbitMq(configure?)` on the messaging builder attaches a `RabbitMqMessageBroker`; the first registration wins and all `AddMessaging` repeat rules apply.
- **Publishing** — sends to the exchange named like the destination with the destination as routing key, UTF-8 payloads and `ContentType` defaulting to `application/json`.
- **Per-test tap** — the test's consumer owns its own channel on the run's shared connection and declares one exclusive, auto-delete queue per awaited destination, bound with the destination routing key and with `#`; every queue is deleted when the test ends.
- **Connection** — one shared connection for the run, released as the run-scoped `messaging:broker` resource; `RabbitMqOptions.ConnectionStringSetting` names the key a container fills.
- **Tracing** — the same `messaging.publish`/`messaging.await` operations and `messaging.publish`/`messaging.receive` observations, with `messaging.system = "RabbitMQ"`.

## Configuration

| Key | Type | Default |
| --- | --- | --- |
| `ProtoTest:Messaging:RabbitMq:ConnectionString` | `string` | `amqp://guest:guest@localhost:5672/` |
| `ProtoTest:Messaging:RabbitMq:PollInterval` | `TimeSpan` | `00:00:00.025` |

A tap has no history: a destination prepared lazily at the await only sees later messages, awaiting drains and discards non-matching messages, and the exchange must already exist — the adapter does not declare application exchanges.

## Learn more

- [Messaging guide](https://prototest.dev/docs/integrations/messaging/)
- [Demo broker registration](https://github.com/MSeys/ProtoTest/blob/main/samples/ProtoTest.Demo/Setup.cs)
