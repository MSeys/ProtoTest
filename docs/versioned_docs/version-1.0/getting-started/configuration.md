---
sidebar_position: 3
title: Configuration
description: "Set ProtoTest options in code, in configuration, or both, so one suite runs in-process on a laptop and against a deployed environment in CI."
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

With environment variables, use `__` for the section separator: `ProtoTest__Applications__Api__BaseUrl`.

You can also supply values in code, which the sample suite does for file paths that depend on the build output:

```csharp
builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
    new Dictionary<string, string?>
    {
        ["ProtoTest:Applications:Api:OpenApi:Specification"] =
            Path.Combine(AppContext.BaseDirectory, "control-plane.openapi.json")
    }));
```

Tests read configuration through `Proto.Context.Configuration`.

## Host options (code only)

Two option sets are set in code while the host is built; neither binds from configuration, because Core options do not implement `IProtoConfigurableOptions` and so have no configuration section.

`ConfigureTestIds` fills a `ProtoTestIdOptions`:

| Key | Type | Default |
| --- | --- | --- |
| `RunPrefix` | `long?` | a random six-digit number per host |
| `SequenceDigits` | `int` | `6` (valid 1–9) |

`ConfigureTracing` fills a `ProtoTraceOptions`:

| Key | Type | Default |
| --- | --- | --- |
| `Enabled` | `bool` | `true` |
| `OutputPath` | `string?` | `null` → `TestResults/prototest-{runId}.prototrace` |
| `ActivitySources` | `IList<string>` | empty |
| `CaptureSourceLocations` | `bool` | `true` |
| `EmbedSources` | `bool` | `true`; embedding requires `CaptureSourceLocations && EmbedSources` |

```csharp
builder
    .ConfigureTestIds(ids => ids.RunPrefix = 42)
    .ConfigureTracing(trace =>
    {
        trace.OutputPath = "TestResults/run.prototrace";
        trace.ActivitySources.Add("MyApp.Domain");
    });
```

To replace the id scheme entirely, register your own `IProtoTestIdGenerator` with `ConfigureServices`.

## Repeated registration

The rule of thumb: **infrastructure registers once, clients compose, config callbacks accumulate** — with hooks and gates as the exception. A repeated `Add…` is safe by design, but what the second call does depends on what it registers:

| You call | What a second call does |
| --- | --- |
| `AddTestHook`, `AddRunHook`, `AddRunGate` | adds another hook or gate; there is **no dedupe** |
| `AddCapability` | an equal descriptor registers once |
| `AddSink<TSink>` | the first registration of the sink type wins; a repeated generic call appends its `configure` callback |
| `AddInfrastructure`, `AddResource` | the same instance is a no-op (`AddInfrastructure` also merges the repeated call's settings keys); a different instance under the same id throws *"already owned by the run"* |
| `AddClient` | clients compose and the first registration that initializes for a type and name wins |
| `ConfigureResponses`, `CaptureAttachments` (REST, GraphQL) | callbacks compose; the known configuration section is bound over the result |
| `CaptureAttachments` (gRPC, Messaging) | the last call replaces the previous options; the known section still binds over them |
| `AddSql`, `AddSheets`, `AddEntityFrameworkCore` | the first call wins; later calls are no-ops |
| `AddData` | composes onto one registry; every call's callback runs |
| `AddCollector<TCollector>` | the same collector type for the same target registers once |

Some repeat rules are errors rather than no-ops: a different run resource under an existing id throws, `AddInfrastructure` rejects a resource whose `Scope` is not `Run` and rejects settings keys without an `IProtoConnectionInfrastructure`, and a duplicate client type and name throws during setup. A `configure` callback that throws is not remembered — a later successful call can still compose the integration.

## Which value wins

For options that support both, values are applied in this order — **later wins**:

1. the option's **default**,
2. your **code** (the `configure` callback),
3. **configuration**.

So code sets sensible defaults for the suite, and an environment overrides them without a rebuild.

The one exception is a base address: a URL passed directly to `AddClient("Api", "https://…")` wins over the client's base address under `ProtoTest:Applications:{app}` — the application's `BaseUrl` joined with `Endpoints:{client}` when that endpoint is configured. Leave it out of code when you want configuration to decide.

## Everything configurable

```json
{
  "ProtoTest": {
    "Applications": {
      "ControlPlane": {
        "BaseUrl": "https://staging.example.test/",
        "Endpoints": { "Api": "/api", "GraphQL": "/graphql" },
        "OpenApi": { "Specification": "https://staging.example.test/swagger/v1/swagger.json" }
      }
    },
    "Rest": { "Responses": { "MaxResponseBodyBytes": 10485760 } },
    "Web": { "Playwright": { "Browser": "Chromium", "Headless": false } },
    "Reporting": { "Json": { "OutputPath": "TestResults/report.json", "Indented": true } }
  }
}
```

| Section | Documented in |
| --- | --- |
| `ProtoTest:Applications:{name}:BaseUrl` | the address of a system under test, shared by its HTTP clients and [web sessions](../integrations/web/index.md) |
| `ProtoTest:Applications:{name}:Endpoints:{client}` | a relative path appended to `BaseUrl` for that client |
| `ProtoTest:Applications:{name}:OpenApi:Specification` | [OpenAPI](../integrations/openapi.md) |
| `ProtoTest:Applications:{name}:GraphQL:*` | [GraphQL](../integrations/graphql/index.md), [schema coverage](../integrations/graphql/coverage.md) |
| `ProtoTest:Applications:{name}:Grpc:Address` | [gRPC](../integrations/grpc/index.md) |
| `ProtoTest:Rest:*` | [REST attachments](../integrations/rest/attachments.md), [request limits](../integrations/rest/requests.md) |
| `ProtoTest:GraphQL:*` | [GraphQL](../integrations/graphql/index.md) |
| `ProtoTest:Web:*` | [Web](../integrations/web/index.md) |
| `ProtoTest:Reporting:*` | [Reporting](../observability/reporting.md) |

Configured only in code: tracing (`ConfigureTracing`), test ids (`ConfigureTestIds`) and data defaults (`AddData`). Those callbacks run while the host is being built, before configuration exists, so they can't read `IConfiguration`. If a value needs to vary per environment, read it yourself — for example `trace.OutputPath = Environment.GetEnvironmentVariable("TRACE_PATH") ?? "TestResults/run.prototrace";`. Inside tests, hooks and attributes, `context.Configuration` has everything.
