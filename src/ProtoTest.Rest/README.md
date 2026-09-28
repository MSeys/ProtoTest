# ProtoTest.Rest

REST requests that use the ProtoTest context, authentication and trace.

```bash
dotnet add package ProtoTest.Rest
```

```csharp
// ExpectAsync asserts the response shape in the call; it is the same facade assertion.
using var response = await Proto.Context.Rest()
    .GetAsync("/orders/{id}", new { id = 42 })
    .ExpectAsync(new { id = 42, status = "active" });

// After the fact, or exact (every field must be mentioned):
response.Should.HaveHttpStatus(HttpStatusCode.OK)
    .Should.MatchShape(new { id = 42, status = "active" });

var id = response.ReadRequired<int>("$.id");  // throws naming the route and path when missing
```

## What does it add?

The request can use a named target when your application has more than one API. Authentication can also use information that was added to the test context during setup.

Requests and checks are added to the same test trace as the rest of the test. Optional collectors can report endpoint, OpenAPI and traffic coverage: the fields that arrived in responses but that no shape assertion mentioned.

## Learn more

- [REST integration](https://prototest.dev/docs/integrations/rest/)
- [REST authentication](https://prototest.dev/docs/integrations/rest/authentication)
- [Shape matching](https://prototest.dev/docs/foundation/shape-matching)
