# ProtoTest.Messaging.RabbitMq.Testcontainers

A RabbitMQ container owned as a run-scoped resource, so a suite can exercise the messaging capability against a real broker without external setup.

```bash
dotnet add package ProtoTest.Messaging.RabbitMq.Testcontainers --prerelease
```

```csharp
var broker = RabbitMqBroker.Start();      // or TryStart(...) to fall back when Docker is absent
builder.AddResource(broker);

builder.AddMessaging(messaging => messaging.UseRabbitMq(options =>
    options.ConnectionString = broker.ConnectionString));
```

The container starts once for the run and is released when the host is disposed, after the run stopped and the reports were written. The same shape as `ProtoTest.Sql.Testcontainers`, and the same contract: the tests and the application under test share one broker.
