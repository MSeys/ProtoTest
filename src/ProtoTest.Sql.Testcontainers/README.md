# ProtoTest.Sql.Testcontainers

A PostgreSQL container that can be owned by a ProtoTest run.

```bash
dotnet add package ProtoTest.Sql.Testcontainers
```

`PostgresDatabase.Container()` creates the resource. Register it as a target's `UseContainer(...)` provider to start it with the host and publish its connection string to configuration; see `ProtoTest.Testcontainers` for the shared container contract.

The container is shared for the run and does not reset the database between tests. A machine without a container runtime reports the reason through `TryStart`, so the suite can skip instead of failing; check availability before registration when the suite needs a fallback.

## Learn more

- [Infrastructure](https://prototest.dev/docs/foundation/infrastructure)
- [SQL integration](https://prototest.dev/docs/integrations/sql/)
- [Demo provisioners](https://github.com/MSeys/ProtoTest/blob/main/samples/Northstar.ProtoTest/Provisioners/NorthstarDomainProvisioners.cs)
