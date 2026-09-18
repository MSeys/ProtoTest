# ProtoTest.Messaging.RabbitMq.Testcontainers

A RabbitMQ container owned as a run-scoped resource, so a suite can exercise the messaging capability against a real broker without external setup.

```bash
dotnet add package ProtoTest.Messaging.RabbitMq.Testcontainers --prerelease
```

```csharp
builder.AddInfrastructure(
    RabbitMqBroker.Container(),
    "ProtoTest:Messaging:RabbitMq:ConnectionString");

builder.AddMessaging(messaging => messaging.UseRabbitMq());
```

`AddInfrastructure` starts the container with the run and fills the RabbitMQ connection-string setting, so the adapter and an in-process application both reach the same broker. (Registering it with `AddResource` only owns its release; it does not start it or fill settings.) `RabbitMqBroker.Start()` / `TryStart(...)` remain for code that wants to start a container itself and fall back when no runtime is present.

The container starts once for the run and is released when the host is disposed, after the run stopped and the reports were written. The same shape as `ProtoTest.Sql.Testcontainers`, and the same contract: the tests and the application under test share one broker.
