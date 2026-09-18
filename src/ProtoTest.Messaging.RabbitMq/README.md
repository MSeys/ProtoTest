# ProtoTest.Messaging.RabbitMq

RabbitMQ adapter for the ProtoTest messaging capability: publish to exchanges and tap events through one queue per awaited destination.

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


Publishing sends to the exchange named like the destination, so the application's topology decides routing. Awaiting uses a run-level tap: one exclusive, auto-delete queue per awaited destination, bound catch-all to the destination exchange and prepared (purged) before the act so another test's messages cannot satisfy it. The queue stays declared for the run, and an await for a destination that was never prepared binds just in time, seeing only messages published after the await begins. A tap never competes with the application's own consumers; parallel tests should await distinct destinations. The connection is owned and released by the run. See the [messaging guide](https://github.com/MSeys/ProtoTest/blob/main/docs/docs/integrations/messaging/index.md).
