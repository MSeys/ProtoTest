# ProtoTest.Messaging.RabbitMq.Testcontainers

A RabbitMQ container owned as a run-scoped resource, so a suite can exercise messaging against a real broker without external setup.

```bash
dotnet add package ProtoTest.Messaging.RabbitMq.Testcontainers
```

## Quick start

```csharp
builder.AddInfrastructure(
    RabbitMqBroker.Container(),
    RabbitMqOptions.ConnectionStringSetting,
    "Messaging:RabbitMq:ConnectionString");

builder.AddMessaging(messaging => messaging.UseRabbitMq());
```

## What it adds

- **Container resource** — `RabbitMqBroker` derives `ProtoContainerResource<RabbitMqContainer>` with `Id "broker:rabbitmq"`, `Kind "broker"` and `Description "RabbitMQ container"`; the default image is `rabbitmq:3`, configurable through the `RabbitMqBuilder`.
- **Factories** — `Container(configure?)` builds without starting, `Start(configure?)` starts now or throws with the reason, and `TryStart(configure, out broker, out error)` reports failure instead of throwing.
- **Registration** — `AddInfrastructure(broker, RabbitMqOptions.ConnectionStringSetting, "Messaging:RabbitMq:ConnectionString")` starts it with the run and fills both keys, so the adapter and an in-process application reach the same broker; `AddResource` alone only owns its release.
- **Lifecycle** — started once per run and released when the host is disposed, after the run stopped and the reports were written.
- **Capability and keys** — the `Broker` capability comes from `UseRabbitMq`, not the container, and `RabbitMqOptions.ConnectionStringSetting` is the configuration key the adapter reads.
- **Broker state** — `ConnectionString` is empty until the container starts and `IsStarted` reports the state; the resource is start-once with a shared start task.
- **No host extension** — the package has no `AddX` of its own; register the container with `AddInfrastructure` and the broker with `UseRabbitMq`.
- **Image** — `rabbitmq:3`; pass a builder action to pin another tag or configure the container.

## Configuration

No options type of its own: the default image is `rabbitmq:3` and further container settings come from the `RabbitMqBuilder` passed to `Container`/`Start`/`TryStart`; the connection string flows through the keys passed to `AddInfrastructure`. The shape matches `ProtoTest.Sql.Testcontainers`.

The container starts with the host — before any test-level skip condition — so a missing container runtime fails the run at start. Use `TryStart` in the suite fixture before registering to choose another mode or skip the suite; `[RequiresCapability(ProtoCapabilityKinds.Broker)]` still guards tests individually.

## Learn more

- [Messaging guide](https://prototest.dev/docs/integrations/messaging/)
- [Demo broker and container setup](https://github.com/MSeys/ProtoTest/blob/main/samples/ProtoTest.Demo/Setup.cs)
