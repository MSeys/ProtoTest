# ProtoTest.OpenApi

Maps ProtoTest REST observations to an OpenAPI contract and reports endpoint, response, and response-property coverage.

```bash
dotnet add package ProtoTest.OpenApi --prerelease
```

```csharp
builder.AddRest(rest => rest
    .AddClient("Orders")
    .WithCollector<OpenApiCoverageCollector>());
```

Specifications can come from configuration, a file, a URL, raw JSON/YAML, or an `OpenApiDocument`. Add `ProtoTest.Reporting` to export the collected coverage. See the [OpenAPI guide](https://github.com/matthiasseys/ProtoTest/blob/main/docs/integrations/openapi.md).
