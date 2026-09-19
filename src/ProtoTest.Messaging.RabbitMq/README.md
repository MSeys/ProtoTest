# ProtoTest.Messaging.RabbitMq

RabbitMQ adapter for the ProtoTest messaging capability: publish to exchanges and tap events through a per-test queue per awaited destination.

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
    RabbitMqOptions.ConnectionStringSetting, "Messaging:RabbitMq:ConnectionString");
builder.AddMessaging(messaging => messaging.UseRabbitMq());
```


Publishing sends to the exchange named like the destination, so the application's topology decides routing. Awaiting uses a genuinely per-test tap: the test's consumer owns its own channel on the run's shared connection and declares one exclusive, auto-delete queue per awaited destination, bound to the destination exchange with the destination routing key and with `#`. Direct exchanges match the routing key, topic exchanges match the catch-all, and fanout and headers exchanges match every message because their bindings carry no arguments. A destination that was never prepared is bound just in time at the await, seeing only messages published after the await begins. Every queue is deleted when the test ends, a tap never competes with the application's own consumers, and parallel tests on the same destination each get their own queue. The connection is owned and released by the run. See the [messaging guide](https://github.com/MSeys/ProtoTest/blob/main/docs/docs/integrations/messaging/index.md).
