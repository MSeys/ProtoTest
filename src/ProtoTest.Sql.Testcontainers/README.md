# ProtoTest.Sql.Testcontainers

A PostgreSQL container that can be owned by a ProtoTest run.

```bash
dotnet add package ProtoTest.Sql.Testcontainers
```

`PostgresDatabase.Container()` creates the resource. Register it with `AddInfrastructure(...)` to start it with the host and publish its connection string to configuration.

The container is shared for the run and does not reset the database between tests. A missing container runtime fails host startup unless the suite checks availability before registration.

## Learn more

- [Infrastructure](https://prototest.dev/docs/foundation/infrastructure)
- [SQL integration](https://prototest.dev/docs/integrations/sql/)
- [Demo provisioners](https://github.com/MSeys/ProtoTest/blob/main/samples/Northstar.ProtoTest/Provisioners/NorthstarDomainProvisioners.cs)
