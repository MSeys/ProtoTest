# ProtoTest.OpenApi

Maps ProtoTest REST observations to an OpenAPI contract and reports endpoint, response, and response-property coverage.

```bash
dotnet add package ProtoTest.OpenApi --prerelease
```

```csharp
builder
    .ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
        new Dictionary<string, string?>
        {
            ["ProtoTest:Applications:Orders:OpenApi:Specification"] =
                Path.Combine(AppContext.BaseDirectory, "orders.openapi.json")
        }))
    .AddApplication("Orders", app => app.AddRest(rest => rest
        .AddClient("Orders")
        .AddCollector<OpenApiCoverageCollector>()));
```

Specifications can come from configuration, a file, a URL, raw JSON/YAML, or an `OpenApiDocument`. The collector reads `ProtoTest:Applications:{application}:OpenApi:Specification` and throws when that key is missing, so pass a source string or a document directly to `AddCollector` when you don't use configuration. Add `ProtoTest.Reporting` to export the collected coverage. See the [OpenAPI guide](https://github.com/MSeys/ProtoTest/blob/main/docs/docs/integrations/openapi.md).
