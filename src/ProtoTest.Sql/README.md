# ProtoTest.Sql

A database connection owned by each test, with optional transaction rollback.

```bash
dotnet add package ProtoTest.Sql
```

## Includes

- Access to the test's connection, SQL session and transaction through `Proto.Context`.
- Transaction isolation with rollback at teardown, or no transaction when the application must see the same data.
- Connection and transaction lifecycle entries in the trace.

The transaction only covers the connection owned by ProtoTest. It does not automatically include connections opened by the application.

## Learn more

- [SQL integration](https://prototest.dev/docs/integrations/sql/)
- [Write lands in the database](https://prototest.dev/docs/recipes/write-lands-in-the-database)
- [Infrastructure](https://prototest.dev/docs/foundation/infrastructure)
