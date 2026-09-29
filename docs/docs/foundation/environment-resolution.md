---
sidebar_position: 17
title: Environment resolution
description: "Declare what the run needs as a target with an ordered provider chain: the first provider whose condition holds serves it, and the trace records the decision."
---

import TraceAnatomy from '@site/src/components/TraceAnatomy';
import {lessonTraces} from '@site/src/data/traceSources';

export const compositionLayers = [
  {
    id: 'setup',
    label: 'Setup',
    when: '477.7 ms',
    lead: 'The same six hooks and four attributes as any journey. The address decision happened earlier, at chain resolution.',
    entries: [
      {kind: 'test.setup', name: 'Setup', meta: '6 hooks, 4 attributes'},
      {kind: 'client.initialize', name: 'Rest, GraphQL, loopback web, in-process, probe, messaging', meta: 'each with its address recorded'},
    ],
  },
  {
    id: 'execution',
    label: 'Execution',
    when: '138.1 ms',
    lead: 'The test reads the composed address. One request, two checks, both green.',
    entries: [
      {kind: 'http.request', name: 'REST GET /api/v1/organization', meta: '120.4 ms, 200 OK'},
      {kind: 'assert.http.status', name: 'Assert status 200 OK', meta: 'expected and actual agree'},
      {kind: 'assert.json.shape', name: 'Assert response shape', meta: '7.4 ms'},
    ],
  },
  {
    id: 'teardown',
    label: 'Teardown',
    when: '27.7 ms',
    lead: 'Attributes and hooks reverse, three attachments publish, resources release.',
    entries: [
      {kind: 'attachment.publish', name: 'Response, expected shape, scenario summary', meta: '3 files into the archive'},
      {kind: 'data.cleanup', name: 'Cleanup TenantResponse', meta: 'the provisioned tenant is removed'},
    ],
  },
];

# Environment resolution

## What it is

A **target** is something the run needs: a database, a broker, a worker, or an application's address. A **provider** is a run piece that can serve that target. Register the target once with its providers in priority order. The first provider whose condition holds serves the target, only its piece starts, and every other provider is recorded skipped with the reason.

```csharp
builder.AddInfrastructure("NorthstarDatabase", chain => chain
    .UseConfigured()                                      // ConnectionStrings:Northstar is set
    .Use(new ProtoTargetProvider("postgres container", PostgresDatabase.Container())),
    "ConnectionStrings:Northstar");
```

No provider available fails the build, naming the target and every provider's unmet condition. Never a late "no client registered" inside the first test.

## How it works

### Conditions

A condition answers "can this provider serve the target here?" against the configuration the host resolved. The canonical four live in `ProtoProviderConditions`:

| Condition | Holds when | Chain spelling |
| --- | --- | --- |
| `Configured` | every key the **target** declares has a configured value | `.UseConfigured()` |
| `Selected(key, ...)` | any of the integration-owned selection keys is set | `.Use(new ProtoTargetProvider("grid", gridPiece, ProtoProviderConditions.Selected("Grid:Enabled")))` |
| `Available(requirement, probe)` | the runtime probe returns true | `ProtoProviderConditions.Available("Docker is available", DockerIsRunning)` |
| `Always` | no condition, the fallback | `new ProtoTargetProvider("in-process", piece)` |

A provider may combine kinds by implementing `IProtoProviderCondition` itself. A container provider, for example, holds when the environment does not configure the target and the container runtime is available. Conditions are evaluated in chain order, so provider order is the priority of the environments the chain describes.

### Keys live on the target

The target declares its configuration keys once, as its identity, and every provider checks or fills those keys:

```csharp
// The database target owns the key the test-side domain and the application both read.
builder.AddInfrastructure(
    "NorthstarDatabase",
    chain => chain.UseConfigured().Use(new ProtoTargetProvider("postgres container", PostgresDatabase.Container())),
    "ConnectionStrings:Northstar");
```

There is no per-provider address parameter: a provider fills the target's keys or checks them. A provider piece registered for a target that declares keys must be able to fill them (an `IProtoConnectionInfrastructure` or `IProtoSettingsInfrastructure`), exactly like `AddInfrastructure`.

### Writing a provider

A provider is the availability condition, the start behavior and the service declaration in one public contract, so a third party can ship one without the framework knowing the environment it describes:

```csharp
public sealed class GridProvider : IProtoTargetProvider
{
    public string Name => "remote grid";

    public IProtoProviderCondition? Condition { get; } =
        ProtoProviderConditions.Selected("Grid:Enabled");

    public IProtoInfrastructure? Infrastructure { get; } = new GridSessionInfrastructure();

    public IReadOnlyList<ProtoCapabilityDescriptor> Capabilities { get; } =
        [new ProtoCapabilityDescriptor("Remote grid", ProtoCapabilityKinds.Protocol, "MyGrid")];
}
```

| Member | Job |
| --- | --- |
| **`Name`** | identifies the provider in the resolution record and in every skip reason |
| **`Condition`** | evaluated against `ProtoProviderConditionContext`: the resolved `Configuration`, the target's `Keys`, and the winners of the targets resolved before this chain (`Target("Api")`). A dependent chain declares `ResolveAfter("Api")` so it is resolved after the target it reads, whatever the registration order. |
| **`Infrastructure`** | the piece the run starts and releases when this provider wins. `null` means configuration itself serves the target and there is nothing to start. |
| **`Capabilities`** | declared only by the winner: a losing provider's capability stays absent, so `[RequiresCapability]` skips exactly when the environment cannot serve it |
| **`ConfigureServices`** | contributes the services that exist only while this provider serves its target. It runs while the host is built, for the winner only, so a losing provider cannot leave a second client or server behind. `ProtoTargetProvider` spells it `WinnerServices`. |

`ProtoTargetProvider` builds the common shape, a name, a piece, a condition and capabilities, in one line. Implement the interface when the provider itself has behavior.

## How to use it

### Applications, workers and devices

`AddApplication` is a target like any other: its derived key is `ProtoTest:Applications:{name}:BaseUrl`, and its providers decide how the run serves it.

```csharp
builder.AddApplication("Api", app => app
    .UseConfigured()                                    // the environment's address
    .UseAspireResource<OpenCsmsAppHostAnchor>("api")          // the selected AppHost's endpoint
    .UseInProcess<Program>()                            // the in-process test server
    .AddWorkerHost<BillingWorker>("Billing")            // the run hosts it when the app runs in-process
    .AddDevices(devices => devices
        .AddWebSocketClient("Chargers", path: "/ws/{deviceId}")));
```

- `UseInProcess<TProgram>()` is the in-process server as a provider: when it wins it declares the `server` and `clock` capabilities, and `[RequiresInProcess]` / `[RequiresTestClock]` gate exactly the runs it serves. `AddAspNetCoreServer` keeps working unchanged and is the provider a suite that declares no chain gets.
- `UseLoopback(createApp)` starts the hand-built application on a loopback listener and publishes the bound address. It is a real process boundary: no `server`, no `clock`, and the readiness probe waits for the address.
- `AddWorkerHost<TProgram>(name)` nests the worker under the application: `UseEnvironment()` is available when the application's winner does not run it in-process (so the environment already runs the worker and the suite must not start a second consumer), and `UseHost()` hosts the worker's entry point in this process, bridging the test clock. A worker registered on the host builder keeps `UseHost` as its default.
- Devices follow the application's winner: `AddInProcessWebSocketDevices<TProgram>(application)` serves through the TestServer while the in-process provider wins, and routes the same client over the socket at the winner's published address otherwise. Its capability is declared while the chain is served in-process, so a loopback or AppHost winner does not advertise a transport it cannot use.

An application that declares no provider registers no chain: `AddAspNetCoreServer` and a configured `BaseUrl` keep behaving as before, and the readiness probe's "in-process" claim follows the `server` capability as it always did.

### Provided providers

| Integration | Provider | Serves when |
| --- | --- | --- |
| Core | `UseConfigured()` | every key the target declares has a value |
| ProtoTest.AspNetCore | `UseInProcess<TProgram>()` | always; the fallback of an application chain |
| ProtoTest.AspNetCore | `UseLoopback(createApp)` | always; publishes the loopback address |
| ProtoTest.Testcontainers | `UseContainer(container)` | `DockerProbe.IsAvailable()` (the same endpoint Testcontainers uses) |
| ProtoTest.Aspire | `UseAspireResource<TAppHost>(resource)` | `ProtoTest:Aspire:Enabled`, or the resource's own `ProtoTest:Aspire:Resources:{resource}:Enabled`, is set, the integration-owned selection keys |
| ProtoTest.Aspire | `ProtoAspireOptions.MapConnectionString(resource, key)` | a registered AppHost publishes the resource's connection string under `key` |
| ProtoTest.Hosting | `UseEnvironment()` / `UseHost()` | the application is served elsewhere / always |

`UseAspireResource` on an application chain publishes the resource's endpoint under the application's derived `BaseUrl`. On an infrastructure chain it publishes the resource's connection string under every key the target declares. A configured provider earlier in the chain always wins, so the same composition runs against an existing environment without starting anything. See [Aspire](../integrations/aspire.md) for the selection keys and the AppHost composition.

## What the trace shows

The run records one `environment.resolved` event per target with the target, its keys, the winning provider and every skipped provider with the reason, plus one `environment.provider.skipped` event per loser. A skipped provider's piece is never started, owned or released, and its run entity carries `infrastructure.state: skipped` with the reason. Read the record in a report to answer "which environment did this run actually use?" without reading the setup code.

The chain figure is in [Infrastructure](./infrastructure.md#what-it-is): one target, providers in order, one winner. The two recordings below show what that decision changes for a test. The drill hardcodes its address and never touches the composition:

```text
FailureDrills.TheAddressWasHardcodedForOneMachine
  test.execution failed in 2058.6 ms, no child operation
  HttpRequestException: ConnectionError reaching http://127.0.0.1:5099: connection refused.
```

The trace records the failure on `test.execution`, but the call it never wrapped cannot appear in it. Teardown publishes only the scenario summary. The test that takes the address from the composition runs the same journey and passes:

<TraceAnatomy
  source={lessonTraces.environmentFix}
  title="The address from the composition"
  test="Northstar.ProtoTest.FailureDrills.TheAddressComesFromTheComposition"
  layers={compositionLayers}
  blindSpots={[]}
/>

A provider that lost to an earlier one records the earlier provider as the reason. A provider whose own condition failed records the condition: the missing keys, the unset selection key, or the requirement the probe did not meet.

## Limits

- **One chain per target.** A target name is unique on a host builder; a repeated registration throws. Add providers to the chain instead of registering the target twice.
- **Resolved once, while the host is built.** Conditions read the configuration the host resolved. They never see the settings a started piece publishes later, and there is no per-test re-resolution. A retried start records the same resolved chain.
- **A chain with no satisfied provider is a build failure.** There is no implicit fallback: end a chain with an `Always` provider when the run must serve the target somehow.
- **The winner starts at its registration position.** A provider piece starts where it was registered, like any infrastructure piece, and readiness still belongs to the pieces that publish an address.
- **A worker's chain resolves after its application's.** `ResolveAfter` orders the resolution, not the registration. A worker nested under an application reads its winner, so the hosted-then-environment decision cannot race the application's own chain.
- **The winner's services are registered once.** `ConfigureServices` runs for the winning provider while the host is built. A loser contributes nothing, and a host with no chain keeps the registrations its `Add...` calls made.
- **Adapters without a chain use `AddCapabilityWhenInProcess`.** It declares the capability with the application's name and its fallback key, so the key's configured value decides.
- **The old skip-key registration is obsolete.** `AddInfrastructure(piece, keys)` keeps its all-configured skip rule for 1.x; the chain overload on this page is the replacement. See [Infrastructure](./infrastructure.md).
- **The AppHost composes like any target.** `AddAspireAppHost` registers the AppHost with the configured provider first and the AppHost provider on the selection keys. See [Aspire](../integrations/aspire.md).
