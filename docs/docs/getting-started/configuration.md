---
sidebar_position: 3
title: Configuration
---

# Configuration

Most ProtoTest options can be set in code, in configuration files, or both. That lets one suite run in-process on a laptop and against a deployed environment in CI, with nothing but a different settings file.

## Adding configuration sources

The host starts with an empty configuration. Add whatever sources you use:

```csharp
builder.ConfigureAppConfiguration(configuration => configuration
    .AddJsonFile("appsettings.Test.json", optional: true)
    .AddEnvironmentVariables());
```

`AddJsonFile` and `AddEnvironmentVariables` come from the `Microsoft.Extensions.Configuration.Json` and `…EnvironmentVariables` packages. Make sure JSON files are copied to the output directory:

```xml
<None Update="appsettings.Test.json" CopyToOutputDirectory="PreserveNewest" />
```

With environment variables, use `__` for the section separator: `ProtoTest__Clients__Api__BaseUrl`.

You can also supply values in code, which the sample suite does for file paths that depend on the build output:

```csharp
builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
    new Dictionary<string, string?>
    {
        ["ProtoTest:Clients:Api:OpenApi:Specification"] =
            Path.Combine(AppContext.BaseDirectory, "control-plane.openapi.json")
    }));
```

Tests read configuration through `Proto.Context.Configuration`.

## Which value wins

For options that support both, values are applied in this order — **later wins**:

1. the option's **default**,
2. your **code** (the `configure` callback),
3. **configuration**.

So code sets sensible defaults for the suite, and an environment overrides them without a rebuild.

The one exception is a client's `BaseUrl`: a URL passed directly to `AddClient("Api", "https://…")` wins over `ProtoTest:Clients:Api:BaseUrl`. Leave it out of code when you want configuration to decide.

## Everything configurable

```json
{
  "ProtoTest": {
    "Clients": {
      "Api": {
        "BaseUrl": "https://staging.example.test/",
        "OpenApi": { "Specification": "https://staging.example.test/swagger/v1/swagger.json" }
      },
      "GraphQL": {
        "BaseUrl": "https://staging.example.test/graphql",
        "GraphQL": {
          "Schema": "schema.graphql",
          "SubscriptionTransport": "WebSocket"
        }
      }
    },
    "Rest": {
      "Attachments": {
        "CaptureRequestBodies": true,
        "CaptureResponses": true,
        "CaptureExpectedShapes": true,
        "RedactSensitiveData": true,
        "MaxDiagnosticBodyLength": 65536
      },
      "Responses": { "MaxResponseBodyBytes": 10485760 }
    },
    "GraphQL": {
      "Attachments": { "CaptureResponses": true },
      "Responses": { "MaxResponseBodyBytes": 10485760 }
    },
    "Web": {
      "Playwright": { "Browser": "Chromium", "Headless": true },
      "Selenium": { "ActionTimeout": "00:00:05" },
      "Sessions": { "Admin": { "TraceRetention": "Always" } }
    },
    "Reporting": {
      "Json": { "OutputPath": "TestResults/report.json", "Indented": true },
      "Html": { "OutputPath": "TestResults/report.html", "Title": "ProtoTest Report" }
    }
  }
}
```

| Section | Documented in |
| --- | --- |
| `ProtoTest:Clients:{name}:BaseUrl` | [REST](../integrations/rest/index.md#base-url-from-configuration), [GraphQL](../integrations/graphql/index.md) |
| `ProtoTest:Clients:{name}:OpenApi:Specification` | [OpenAPI](../integrations/openapi.md) |
| `ProtoTest:Clients:{name}:GraphQL:*` | [GraphQL](../integrations/graphql/index.md#target-options), [schema coverage](../integrations/graphql/coverage.md) |
| `ProtoTest:Rest:*` | [REST attachments](../integrations/rest/attachments.md), [request limits](../integrations/rest/requests.md#response-size-limit) |
| `ProtoTest:GraphQL:*` | [GraphQL](../integrations/graphql/index.md#builder-options) |
| `ProtoTest:Web:*` | [Web](../integrations/web/index.md#from-configuration) |
| `ProtoTest:Reporting:*` | [Reporting](../advanced/reporting.md#configuring-from-files) |

Configured only in code:

- tracing — `ConfigureTracing`,
- test ids — `ConfigureTestIds`,
- data defaults — `AddData`.

Those callbacks run while the host is being built, before configuration exists, so they can't read `IConfiguration`. If a value needs to vary per environment, read it yourself — for example `trace.OutputPath = Environment.GetEnvironmentVariable("TRACE_PATH") ?? "TestResults/run.prototrace";`. Inside tests, hooks and attributes, `context.Configuration` has everything.
