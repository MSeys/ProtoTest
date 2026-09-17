# ProtoTest.Rest

Runner-independent HTTP/REST testing for ProtoTest, with named `HttpClient` instances, route and query expansion, contextual authentication, JSON shape assertions, safe diagnostics, attachments, and REST observations.

```bash
dotnet add package ProtoTest.Rest --prerelease
```

```csharp
builder.AddApplication("Api", app => app.AddRest(rest => rest.AddClient("Orders")));

[Application("Api", "Rest:Orders")]
[Auth<BearerTokenAuthenticator>("token")]
public sealed class OrderTests
{
    [ProtoTest]
    public async Task GetOrder()
    {
        using var response = await Proto.Context.Rest()
            .GetAsync("/orders/{id}", new { id = 42 });

        response.ShouldHaveHttpStatus(HttpStatusCode.OK)
            .ShouldMatchShape(new { id = 42, status = JsonValue.NotNull() });
    }
}
```

Per-test base addresses and custom `IProtoHttpAuthenticator` implementations can use typed state from `Proto.Context`. See the [REST guide](https://github.com/matthiasseys/ProtoTest/blob/main/docs/integrations/rest.md).
