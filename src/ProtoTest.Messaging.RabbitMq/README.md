# ProtoTest.Messaging.RabbitMq

RabbitMQ adapter for the ProtoTest messaging capability: publish to exchanges and tap events with a per-test queue.

```bash
dotnet add package ProtoTest.Messaging.RabbitMq --prerelease
```

```csharp
builder.AddMessaging(messaging => messaging.UseRabbitMq(options =>
{
    // Or set ProtoTest:Messaging:RabbitMq:ConnectionString for the environment.
    options.ConnectionString = "amqp://guest:guest@localhost:5672/";
}));
```

For a broker the run owns, add the container as infrastructure instead - the started connection string reaches both the adapter and an in-process application, and an explicitly configured string still wins:

```csharp
builder.AddInfrastructure(RabbitMqBroker.Container(),
    ProtoRabbitMqOptions.ConnectionStringSetting, "Messaging:RabbitMq:ConnectionString");
builder.AddMessaging(messaging => messaging.UseRabbitMq());
```


Publishing sends to the exchange named like the destination, so the application's topology decides routing. Awaiting declares a fresh exclusive, auto-delete queue, binds it catch-all to the destination exchange, drains it until a match or the timeout, then deletes it - a per-test tap that never competes with the application's consumers. The connection is owned and released by the run. See the [messaging guide](https://github.com/matthiasseys/ProtoTest/blob/main/docs/integrations/messaging.md).
