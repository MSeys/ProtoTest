---
sidebar_position: 3
title: Configuration
description: "Set ProtoTest options in code, in configuration, or both, so one suite runs in-process on a laptop and against a deployed environment in CI."
---

# Configuration

You can set most ProtoTest options in code, in configuration files, or in both. This lets one suite run in-process on a laptop and against a deployed environment in CI. Only the settings file changes.

## Which value wins

ProtoTest applies a value from three places, in this order. The later one wins:

1. the option's **default**,
2. your **code** (the `configure` callback),
3. **configuration**.

So your code sets the suite's defaults, and an environment can override them without a rebuild.

A base address is the one exception. A URL passed directly to `AddClient("Api", "https://...")` wins over the client's base address in configuration. That configured base address is the application's `BaseUrl` under `ProtoTest:Applications:{app}`, joined with `Endpoints:{client}` when that endpoint is set. Leave the URL out of your code when you want configuration to decide.

| You set | In code | In configuration | Which wins |
| --- | --- | --- | --- |
| An integration option | `rest => rest.MaxResponseBodyBytes = ...` | `ProtoTest:Rest:Responses:MaxResponseBodyBytes` | configuration |
| An application address | `AddClient("Api", "https://...")` | `ProtoTest:Applications:Api:BaseUrl` | the code URL |
| An application address | no URL in code | `ProtoTest:Applications:Api:BaseUrl` | configuration |

## Adding configuration sources

The host starts with an empty configuration. You add the sources you want:

```csharp
// dotnet add package Microsoft.Extensions.Configuration.Json
// dotnet add package Microsoft.Extensions.Configuration.EnvironmentVariables
using Microsoft.Extensions.Configuration;

builder.ConfigureAppConfiguration(configuration => configuration
    .AddJsonFile("appsettings.Test.json", optional: true)
    .AddEnvironmentVariables());
```

`AddJsonFile` and `AddEnvironmentVariables` come from the `Microsoft.Extensions.Configuration.Json` and `Microsoft.Extensions.Configuration.EnvironmentVariables` packages. Copy the JSON file to the output directory:

```xml
<None Update="appsettings.Test.json" CopyToOutputDirectory="PreserveNewest" />
```

In an environment variable, write `__` where the key has a colon: `ProtoTest__Applications__Api__BaseUrl`.

You can also supply values from code. The sample suite does this for file paths that depend on the build output:

```csharp
builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
    new Dictionary<string, string?>
    {
        ["ProtoTest:Applications:Api:OpenApi:Specification"] =
            Path.Combine(AppContext.BaseDirectory, "control-plane.openapi.json")
    }));
```

Tests read the resolved configuration through `Proto.Context.Configuration`.

## Host options (code only)

Two option sets exist only in code. You set them while the host is built, and they have no configuration section.

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
| `EmbedSources` | `bool` | `true`. Embedding requires `CaptureSourceLocations && EmbedSources`. | read at host build |

```csharp
builder
    .ConfigureTestIds(ids => ids.RunPrefix = 42)
    .ConfigureTracing(trace =>
    {
        trace.OutputPath = "TestResults/run.prototrace";
        trace.ActivitySources.Add("MyApp.Domain");
    });
```

To replace the id scheme, register your own `IProtoTestIdGenerator` with `ConfigureServices`.

## Repeated registration

<details>
<summary>Show the rules for each registration call</summary>

You rarely need this section. It matters when two parts of a setup call the same `Add...` method. As a rule, register infrastructure once. Clients and configuration callbacks combine. Hooks and gates are the exception, because each call adds another one. What the second call does depends on what it registers:

| You call | What a second call does |
| --- | --- |
| `AddTestHook`, `AddRunHook`, `AddRunGate` | adds another hook or gate. There is **no dedupe**. |
| `AddCapability` | an equal descriptor registers once |
| `AddSink<TSink>` | the first registration of the sink type wins. A repeated generic call appends its `configure` callback. |
| `AddInfrastructure(piece, keys)`, `AddResource` | the same instance is a no-op (`AddInfrastructure` also merges the repeated call's settings keys, and `AddInfrastructureAlways` still forces a start). A different instance under the same id throws *"already owned by the run"*. |
| `AddInfrastructure(name, chain, keys)` | a repeated target name throws. Add providers to the existing chain instead. |
| `AddClient` | clients compose and the first registration that initializes for a type and name wins |
| `ConfigureResponses`, `CaptureAttachments` (all protocols) | callbacks compose, and the known configuration section is bound over the result |
| `AddSql`, `AddSheets`, `AddEntityFrameworkCore` | the first call wins, and later calls are no-ops |
| `AddData` | composes onto one registry, and every call's callback runs |
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

Some repeats fail instead:

- A different run resource under an existing id throws.
- `AddInfrastructure` rejects a resource whose `Scope` is not `Run`. It also rejects settings keys on a piece that provides no addresses.
- A duplicate client type and name throws during setup.

A `configure` callback that throws is not remembered. A later successful call can still compose the integration.

</details>

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
    "Sql": { "Isolation": "Transaction" },
    "Messaging": { "RabbitMq": { "ConnectionString": "amqp://guest:guest@localhost:5672/" } },
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
| `ProtoTest:Rest:Responses`, `ProtoTest:Rest:Attachments` | one per integration | [REST attachments](../integrations/rest/attachments.md), [request limits](../integrations/rest/requests.md) |
| `ProtoTest:GraphQL:Responses`, `ProtoTest:GraphQL:Attachments` | one per integration | [GraphQL](../integrations/graphql/index.md) |
| `ProtoTest:Grpc:Client`, `ProtoTest:Grpc:Attachments` | one per integration | [gRPC](../integrations/grpc/index.md) |
| `ProtoTest:Messaging`, `ProtoTest:Messaging:Attachments` | one per integration | [Messaging](../integrations/messaging/index.md) |
| `ProtoTest:Messaging:RabbitMq` | one per integration | [Messaging](../integrations/messaging/index.md) (`ConnectionString` names the broker) |
| `ProtoTest:Sql` | one per integration | [SQL](../integrations/sql/index.md) (`Isolation`, `SharedWithApplications`) |
| `ProtoTest:Sheets` | one per integration | [Sheets](../integrations/sheets/index.md) (`IncludeHiddenSheets`) |
| `ProtoTest:Devices:Mqtt`, `ProtoTest:Devices:WebSocket` | one per integration | [Devices](../integrations/devices.md) |
| `ProtoTest:Web:Playwright`, `ProtoTest:Web:Selenium`, `ProtoTest:Web:Pages` | one per integration | [Web](../integrations/web/index.md) |
| `ProtoTest:Reporting:Json`, `ProtoTest:Reporting:Html` | one per integration | [Reporting](../observability/reporting.md) |
| `ProtoTest:Readiness` | one per run | [Infrastructure](../foundation/infrastructure.md) (host probes and containers) |

Each integration reads its options from one section named `ProtoTest:<Integration>`. Some add a second segment for the area they cover, for example `ProtoTest:Rest:Responses` or `ProtoTest:Grpc:Client`.

When a section is renamed, the old key keeps working as a deprecated fallback. See [Migrating from 1.0](migrating-from-1-0.md) for the gRPC rename.

Three things are set only in code: tracing (`ConfigureTracing`), test ids (`ConfigureTestIds`) and data defaults (`AddData`). Their callbacks run while the host is being built, before configuration exists, so they cannot read `IConfiguration`. If a value must vary per environment, read it yourself, for example `trace.OutputPath = Environment.GetEnvironmentVariable("TRACE_PATH") ?? "TestResults/run.prototrace";`. Inside tests, hooks and attributes, `context.Configuration` holds the resolved configuration.

## Where to next

- [Environments](./environments.md): the same suite in-process, container-backed or against a published system.
- [Infrastructure](../foundation/infrastructure.md): pieces the run starts and the settings they publish.
- [Troubleshooting](./troubleshooting.md): when a client or a container does not come up.
