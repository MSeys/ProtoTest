# ProtoTest.Testcontainers

Shared container plumbing for ProtoTest integrations: a run-scoped container resource registered with `AddInfrastructure` so the host starts it, exposes its connection string under a configuration key, and releases it after the reports are written. `AddResource` alone only registers the resource for ownership and release; it starts nothing and fills no key.

```bash
dotnet add package ProtoTest.Testcontainers --prerelease
```

Technology packages build on it - `ProtoTest.Sql.Testcontainers` and `ProtoTest.Messaging.RabbitMq.Testcontainers` - so each keeps its own Testcontainers module dependency and shares one lifecycle. The base itself depends only on ProtoTest.Core.
