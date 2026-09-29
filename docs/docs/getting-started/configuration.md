---
sidebar_position: 3
title: Configuration
description: "Set ProtoTest options in code, in configuration, or both, so one suite runs in-process on a laptop and against a deployed environment in CI."
---

# Configuration

Most ProtoTest options can be set in code, in configuration files, or both. One suite can run in-process on a laptop and against a deployed environment in CI, with nothing but a different settings file.

## Which value wins

Values are applied in this order, and the later wins:

1. the option's **default**,
2. your **code** (the `configure` callback),
3. **configuration**.

So code sets the defaults for the suite, and an environment overrides them without a rebuild.

The one exception is a base address: a URL passed directly to `AddClient("Api", "https://...")` wins over the client's base address under `ProtoTest:Applications:{app}`. That base address is the application's `BaseUrl` joined with `Endpoints:{client}` when that endpoint is configured. Leave it out of code when you want configuration to decide.

| You set | In code | In configuration | Which wins |
| --- | --- | --- | --- |
| An integration option | `rest => rest.MaxResponseBodyBytes = ...` | `ProtoTest:Rest:Responses:MaxResponseBodyBytes` | configuration |
| An application address | `AddClient("Api", "https://...")` | `ProtoTest:Applications:Api:BaseUrl` | the code URL |
| An application address | no URL in code | `ProtoTest:Applications:Api:BaseUrl` | configuration |

## Adding configuration sources

The host starts with an empty configuration. Add whatever sources you use:

```csharp
// dotnet add package Microsoft.Extensions.Configuration.Json
// dotnet add package Microsoft.Extensions.Configuration.EnvironmentVariables
using Microsoft.Extensions.Configuration;

builder.ConfigureAppConfiguration(configuration => configuration
    .AddJsonFile("appsettings.Test.json", optional: true)
    .AddEnvironmentVariables());
```

`AddJsonFile` and `AddEnvironmentVariables` come from the `Microsoft.Extensions.Configuration.Json` and `Microsoft.Extensions.Configuration.EnvironmentVariables` packages. Make sure JSON files are copied to the output directory:

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

Set two option sets in code while the host is built. They do not bind from configuration, so they have no configuration section.

`ConfigureTestIds` fills a `ProtoTestIdOptions`:

| Key | Type | Default | Why code-only |
| --- | --- | --- | --- |
| `RunPrefix` | `long?` | a random six-digit number per host | read at host build |
| `SequenceDigits` | `int` | `6` (valid 1 to 9) | read at host build |

`ConfigureTracing` fills a `ProtoTraceOptions`:

| Key | Type | Default | Why code-only |
| --- | --- | --- | --- |
| `Enabled` | `bool` | `true` | read at host build |
| `OutputPath` | `string?` | `null`, which means `TestResults/prototest-{runId}.prototrace` | read at host build |
| `ActivitySources` | `IList<string>` | empty | read at host build |
| `CaptureSourceLocations` | `bool` | `true` | read at host build |
| `EmbedSources` | `bool` | `true`; embedding requires `CaptureSourceLocations && EmbedSources` | read at host build |

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

As a rule, register infrastructure once. Clients compose and config callbacks accumulate. Hooks and gates are the exception: each call adds another one. A repeated `Add...` is safe by design, but what the second call does depends on what it registers:

| You call | What a second call does |
| --- | --- |
| `AddTestHook`, `AddRunHook`, `AddRunGate` | adds another hook or gate; there is **no dedupe** |
| `AddCapability` | an equal descriptor registers once |
| `AddSink<TSink>` | the first registration of the sink type wins; a repeated generic call appends its `configure` callback |
| `AddInfrastructure(piece, keys)`, `AddResource` | the same instance is a no-op (`AddInfrastructure` also merges the repeated call's settings keys, and `AddInfrastructureAlways` still forces a start); a different instance under the same id throws *"already owned by the run"* |
| `AddInfrastructure(name, chain, keys)` | a repeated target name throws; add providers to the existing chain instead |
| `AddClient` | clients compose and the first registration that initializes for a type and name wins |
| `ConfigureResponses`, `CaptureAttachments` (all protocols) | callbacks compose; the known configuration section is bound over the result |
| `AddSql`, `AddSheets`, `AddEntityFrameworkCore` | the first call wins; later calls are no-ops |
| `AddData` | composes onto one registry; every call's callback runs |
| `AddCollector<TCollector>` | the same collector type for the same target registers once |

```mermaid
flowchart TB
    Q["Called Add twice. What registers?"]
    Q --> H{"Hook, gate,\nor options callback?"}
    H -->|yes| A["Appends. Every call adds one."]
    H -->|no| S{"Same instance\nunder the same id?"}
    S -->|yes| B["No-op. Infrastructure also merges the repeated keys."]
    S -->|no| D{"Same id,\ndifferent instance?"}
    D -->|yes| C["Throws. The run is already owned."]
    D -->|no| F{"Client, sink,\nor first-wins piece?"}
    F -->|yes| E["First wins. Sink and collector callbacks still append."]
    F -->|no| G["Accumulates. Data, response and attachment callbacks compose."]
```

Some repeats fail instead. A different run resource under an existing id throws. `AddInfrastructure` rejects a resource whose `Scope` is not `Run`, and settings keys on a piece that provides no addresses. A duplicate client type and name throws during setup. A `configure` callback that throws is not remembered: a later successful call can still compose the integration.

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

| Section | Pattern | Documented in |
| --- | --- | --- |
| `ProtoTest:Applications:{name}:BaseUrl` | one per application | the address of a system under test, shared by its HTTP clients and [web sessions](../integrations/web/index.md) |
| `ProtoTest:Applications:{name}:Endpoints:{client}` | one per client | a relative path appended to `BaseUrl` for that client |
| `ProtoTest:Applications:{name}:OpenApi:Specification` | one per application | [OpenAPI](../integrations/openapi.md) |
| `ProtoTest:Applications:{name}:GraphQL:*` | one per application | [GraphQL](../integrations/graphql/index.md), [schema coverage](../integrations/graphql/coverage.md) |
| `ProtoTest:Applications:{name}:Grpc:Address` | one per application | [gRPC](../integrations/grpc/index.md) |
| `ProtoTest:Rest:*` | one per integration | [REST attachments](../integrations/rest/attachments.md), [request limits](../integrations/rest/requests.md) |
| `ProtoTest:GraphQL:*` | one per integration | [GraphQL](../integrations/graphql/index.md) |
| `ProtoTest:Web:*` | one per integration | [Web](../integrations/web/index.md) |
| `ProtoTest:Reporting:*` | one per integration | [Reporting](../observability/reporting.md) |

Each integration binds its options from one section named `ProtoTest:<Integration>`. Some add a second segment for the area covered, for example `ProtoTest:Rest:Responses` or `ProtoTest:Grpc:Client`.

A renamed section keeps its old key working as a deprecated fallback. See [Migrating from 1.0](migrating-from-1-0.md) for the gRPC rename.

Configured only in code: tracing (`ConfigureTracing`), test ids (`ConfigureTestIds`) and data defaults (`AddData`). Those callbacks run while the host is being built, before configuration exists, so they cannot read `IConfiguration`. If a value needs to vary per environment, read it yourself, for example `trace.OutputPath = Environment.GetEnvironmentVariable("TRACE_PATH") ?? "TestResults/run.prototrace";`. Inside tests, hooks and attributes, `context.Configuration` has the resolved configuration.

## Where to next

- [Environments](./environments.md): the same suite in-process, container-backed or against a published system.
- [Infrastructure](../foundation/infrastructure.md): pieces the run starts and the settings they publish.
- [Troubleshooting](./troubleshooting.md): when a client or a container does not come up.
