# Northstar.ProtoTest

The application-specific test layer used by the Northstar demo.

This project shows the part that belongs to an application's own test code instead of a reusable ProtoTest package.

This is an example of what your setup using ProtoTest could look like.

## Includes

- `[NorthstarMember]`, a composite attribute grouping `[NorthstarTenant]` and the authenticator that carries the member's bearer token through REST, GraphQL and gRPC.
- The shipped `[SignedInAs]`, whose identity `NorthstarMember` turns into the tenant's owner (no role declared) or an invited member for the first declared role.
- Typed contexts that pass the created tenant, member and token to other integrations.
- `NorthstarAuthenticator` for authenticated HTTP and gRPC calls; it resolves the member lazily, so the shipped identity is all a test declares.
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
