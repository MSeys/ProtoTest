# ProtoTest.Sql.EntityFrameworkCore

Uses an Entity Framework Core `DbContext` with the connection owned by `ProtoTest.Sql`.

```bash
dotnet add package ProtoTest.Sql.EntityFrameworkCore
```

Register a context with `AddEntityFrameworkCore<TContext>()` and resolve it through `Proto.Context.Sql<TContext>()`.

When SQL transaction isolation is enabled, the context is enlisted in the test transaction. Individual EF Core commands are not added to the ProtoTest trace.

## Learn more

- [SQL integration](https://prototest.dev/docs/integrations/sql/)
- [Write lands in the database](https://prototest.dev/docs/recipes/write-lands-in-the-database)
- [Package source](https://github.com/MSeys/ProtoTest/tree/main/src/ProtoTest.Sql.EntityFrameworkCore)
