# ProtoTest.OpenApi

Maps ProtoTest REST observations to an OpenAPI contract and reports endpoint, response and response-property coverage.

```bash
dotnet add package ProtoTest.OpenApi
```

## Quick start

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

## What it adds

- **Collector** — `OpenApiCoverageCollector` registers on a REST target with `AddCollector<OpenApiCoverageCollector>()`; a repeat for the same target is a no-op.
- **Sources** — the configuration key `ProtoTest:Applications:{application}:OpenApi:Specification`, a source string (file, inline JSON/YAML or URL), or a prebuilt `OpenApiDocument`; a required `BaseUrl` resolves relative specification URLs.
- **Coverage items** — endpoints (`"OpenAPI"`, `{METHOD} {path}`), responses (`"OpenAPI Response"`, exact status, `4XX` wildcard or `default`) and body properties (`"OpenAPI Property"`) consumed from `RestResponseData` and `RestShapeMatchData`.
- **Report integration** — items are coverage report entries; add `ProtoTest.Reporting` to export them.

## Configuration

| Key | Type | Default |
| --- | --- | --- |
| `ProtoTest:Applications:{application}:OpenApi:Specification` | `string` | required by the configuration constructor |
| `ProtoTest:Applications:{application}:BaseUrl` | `string` | unset; only needed to resolve a relative specification URL |
| `ProtoTest:Applications:{scope}:Application` | `string` | the target name |

The collector reports coverage, never validation: a route outside the specification is ignored, a property counts only when a shape assertion matched it, and there are no trace operations of its own — it reads REST observations.

## Learn more

- [OpenAPI guide](https://prototest.dev/docs/integrations/openapi)
- [Demo collector registration](https://github.com/MSeys/ProtoTest/blob/main/samples/ProtoTest.Demo/Setup.cs)
