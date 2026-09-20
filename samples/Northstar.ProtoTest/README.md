# Northstar.ProtoTest

The application-specific test layer used by the Northstar demo.

This project shows the part that belongs to an application's own test code instead of a reusable ProtoTest package.

This is an example of what your setup using ProtoTest could look like.

## Includes

- `[NorthstarTenant]` and `[SignedInAs]` for setup used by several scenarios.
- Typed contexts that pass the created tenant, member and token to other integrations.
- `NorthstarAuthenticator` for authenticated HTTP and gRPC calls.
- Page objects and a login strategy for the Northstar console.
- Data defaults and provisioners for Northstar fixtures.

```csharp
builder
    .AddNorthstarTestSupport()
    .AddNorthstarData();
```

The demo includes two kinds of provisioners. Some use the API and others use the application domain to create data. You decide how you want to create your test data in one place. This is heavily dependent on which application you are going to test.

## Learn more

- [Demo suite](https://github.com/MSeys/ProtoTest/tree/main/samples/ProtoTest.Demo)
- [Attributes](https://prototest.dev/docs/foundation/attributes)
- [Data provisioners](https://prototest.dev/docs/integrations/data/provisioners)
