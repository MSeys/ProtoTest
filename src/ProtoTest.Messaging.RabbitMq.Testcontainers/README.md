# ProtoTest.Messaging.RabbitMq.Testcontainers

A RabbitMQ container that can be owned by a ProtoTest run.

```bash
dotnet add package ProtoTest.Messaging.RabbitMq.Testcontainers
```

`RabbitMqBroker.Container()` creates the resource. Register it with `AddInfrastructure(...)` so the messaging adapter and the application receive the same connection string.

The container starts before individual test skip conditions are evaluated. Check container availability before registration when the suite needs a fallback.

## Learn more

- [Infrastructure](https://prototest.dev/docs/foundation/infrastructure)
- [Messaging integration](https://prototest.dev/docs/integrations/messaging/)
- [Demo setup](https://github.com/MSeys/ProtoTest/blob/main/samples/ProtoTest.Demo/Setup.cs)
