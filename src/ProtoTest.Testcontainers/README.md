# ProtoTest.Testcontainers

Shared container plumbing for ProtoTest integrations: a run-scoped container resource that starts with the host, exposes its connection string under a configuration key, and is released after the reports are written.

```bash
dotnet add package ProtoTest.Testcontainers --prerelease
```

Technology packages build on it - `ProtoTest.Sql.Testcontainers` and `ProtoTest.Messaging.RabbitMq.Testcontainers` - so each keeps its own Testcontainers module dependency and shares one lifecycle. The base itself depends only on ProtoTest.Core.
