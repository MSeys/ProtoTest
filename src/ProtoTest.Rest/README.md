# ProtoTest.Rest

REST requests that use the ProtoTest context, authentication and trace.

```bash
dotnet add package ProtoTest.Rest
```

```csharp
using var response = await Proto.Context.Rest()
    .GetAsync("/orders/{id}", new { id = 42 });

response.Should.HaveHttpStatus(HttpStatusCode.OK)
    .ShouldMatchShape(new { id = 42, status = "active" });
```

## What does it add?

The request can use a named target when your application has more than one API. Authentication can also use information that was added to the test context during setup.

Requests and checks are added to the same test trace as the rest of the test. Optional collectors can report endpoint and OpenAPI coverage.

## Learn more

- [REST integration](https://prototest.dev/docs/integrations/rest/)
- [REST authentication](https://prototest.dev/docs/integrations/rest/authentication)
- [Shape matching](https://prototest.dev/docs/foundation/shape-matching)
