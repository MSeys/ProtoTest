# ProtoTest engineering facts — architecture

Current at commit `61220f5` (branch `version/1.1`). Source wins over this document: when it disagrees,
the code is right and this file is the bug to fix in the same commit. Items marked **→ AUDIT** are
changes `eng/audit-plan-4.md` requires; the text describes today's behavior and the target rule.

## The model in one paragraph

A suite is one `ProtoHost`. The host is built by `ProtoHostBuilder`, started once, runs tests through
runner adapters, and stops. Every test gets one `ProtoExecutionContext` (ambient `Proto.Context`) and
one trace recorder; the lifecycle is Setup → Execution → Teardown (or Rollback on failure). Clients,
connections and other fixtures are resources with an explicit owner and scope (test or run) and a
deterministic release order. Integrations add a client, an infrastructure piece, a capability, a
collector or a skip condition through the same Core mechanisms; they do not invent parallel
lifecycles. The run's trace (`.prototrace`) and the report sinks record what happened; findings,
observations and attachments are redacted once, at the evidence boundary.

## Layers and dependency rule

```
runner adapters (NUnit, xUnit v2/v3, MSTest, TUnit)
        │
integration packages (Rest, GraphQL, gRPC, Web*, Data, Sql*, Sheets, Messaging*, Devices*, AspNetCore, Hosting)
        │
shared domain packages (ProtoTest.Json, ProtoTest.Http, ProtoTest.Testcontainers, ProtoTest.Web.Pages,
                        ProtoTest.Devices core)
        │
ProtoTest.Core          (no protocol, web, document or broker vocabulary)
```

Core must not learn integration vocabulary. When a shared concept serves two integrations, it lives in
the lowest package that can own it without Core learning it (Audit 2 D1 moved `WebPagePath` and
friends to `ProtoTest.Web.Pages`). Evidence: `src/ProtoTest.Core/` has no `Web`, `GraphQL`, `Rabbit`
or `Sheets` type names.

## Run lifecycle

`Build()` (`ProtoHostBuilder`):

1. Builds `IConfiguration`; registers it with `ProtoInfrastructureSettings`.
2. Computes the conditional-infrastructure skip set and removes skipped pieces from the run store
   **before** anything can own them.
3. Computes dropped conditional capabilities (**→ AUDIT REG-1/REG-2**: per declaration and per
   instance).
4. Selects the run clock (a `ProtoClock` registered by `ConfigureClock`, else one starting now) and
   registers the `TimeProvider` bridge.
5. Registers the internal hooks (client initializer and completion, trace export, run gates, run
   resources) and report sources.
6. Builds the provider and **constructs every `IProtoCollector`** so a bad OpenAPI/GQL schema fails
   construction, not the first test.
7. Second `Build()` throws; some builder entries throw after Build and some do not (**→ AUDIT REG-3**).

`StartAsync()`:

1. Run hooks in ascending `Order` — trace export (`int.MinValue`), run resources (`int.MinValue + 1`),
   sink export (`+2`), run gates (`+3`), HTTP auth (`100`).
2. Resets run-resource ownership for a retry, records capability descriptors as run entities.
3. Starts infrastructure in registration order; each started piece fills its declared settings keys;
   readiness probes are ordinary infrastructure and are awaited at their registration position;
   workers are infrastructure and start after everything registered before them (**→ AUDIT CFG-1**: the
   worker's `Program.Main` does not yet see the run's configuration).
4. Starts the trace listener. A start failure rolls back completed run hooks in reverse, clears
   infrastructure settings, and returns the host to Created for a retry.

`StopAsync()` (first stop wins; second returns or rethrows the remembered failure):

1. `AfterRunAsync` in descending `Order`: gates evaluate, sinks export, run resources release, the
   trace archive is written.
2. A gate failure throws `ProtoRunGateException`; sink failures aggregate; release failures are
   recorded and surfaced.

`DisposeAsync()`: stops a started run, releases leftovers after reports, clears settings, disposes the
provider, stops listening, unregisters. Start/stop/dispose racing is rejected, not serialized.

## Test lifecycle

1. The adapter evaluates skip conditions **before** `StartTestAsync`; a skipped test has no context,
   no trace entry and no setup/teardown.
2. `StartTestAsync` creates the DI scope, trace recorder and context, sorts attributes by `Order`, sets
   the ambient context, then runs hooks ascending (the built-in client initializer has
   `Order = int.MinValue`) and attributes ascending. It opens `test.execution` and makes it the parent.
3. A setup failure records the test `Failed`, rolls back only the completed components (attributes and
   hooks in reverse), and rethrows.
4. Teardown runs attributes reverse, hooks reverse, publishes attachments, disposes the context
   (release owned resources in reverse registration order, then the DI scope), captures artifacts and
   completes the recorder. Every step is attempted; teardown failures become findings and never replace
   the adapter's result; a single exception is rethrown as-is, several aggregate.
5. `CompleteTestAsync` refuses a missing or foreign test; the context disposes once; resources release
   at most once.

## Ownership and lifetimes

| Thing | Owner | Scope | Released by |
| --- | --- | --- | --- |
| Clients registered on the context | `ProtoExecutionContext` | test | context disposal (`disposeWithContext: false` opts out, then the caller owns it) |
| Test resources (`RegisterResource`) | `ProtoExecutionContext` | test | context disposal, reverse registration order |
| Clients/resources registered via `AddResource` | `ProtoHost` | run | run resource hook after gates and sinks |
| Infrastructure (`AddInfrastructure`) | `ProtoHost` | run | same; skipped pieces are never owned |
| Containers (`ProtoContainerResource`) | the run | run | base `DisposeAsync` during release |
| Provisioned data cleanups | `ProtoTest.Data` | test | registered as resources; `data.cleanup` operations |
| External objects (user drivers, user brokers) | the user | — | ProtoTest documents that it disposes them; no ownership flag (**decided, not changed**) |

Release is once-only. A failed test-scope release does not stop later releases. A resource the host
starts again after a failed rollback is re-armed and released once per ownership period.

## Registration semantics (know this before adding an extension)

| API | Repeat behavior | After `Build()` |
| --- | --- | --- |
| `ConfigureServices` / `ConfigureAppConfiguration` / `ConfigureTestIds` | composes (appends) | no-op today (**→ AUDIT REG-3**) |
| `ConfigureTracing` / `AddResource` | composes / same instance no-op | throws today |
| `AddTestHook` / `AddRunHook` / `AddRunGate` | every call adds another descriptor (no dedupe) | no-op today |
| `AddCapability` | dedupes by descriptor value | no-op today |
| `AddCapabilityUnlessConfigured` | conditional declaration; target rule: per declaration + per instance (**→ AUDIT REG-1/REG-2**) | no-op today |
| `AddSink<TSink>` | first per sink type wins; a repeated generic call appends its configure callback; direct DI registrations are wrapped once | no-op today |
| `AddInfrastructure` | same instance merges keys; different instance with the same id throws; skipped when every declared key is configured, unless `AddInfrastructureAlways` | no-op today |
| `AddApplication` | named; first client per protocol is the default | no-op today |
| Integration `AddClient` | first registration that initializes wins (keyed factory + resolver first match) | no-op today |
| `AddWorkerHost` / `AddAspNetCoreServer(name)` | first per name wins | no-op today; **→ AUDIT DEV-6** should throw on a different program/type |
| `AddData` | composes onto one registry per builder | no-op today |
| `AddWeb` | one backend per host, first wins | no-op today |

The one rule to internalize: **first-wins guards must compare identity, not just the name**. A dropped
duplicate that is actually a different program, server, backend or device type is a silent wrong state
(audit REG-2, DEV-1, DEV-6).

## Capabilities and skips

- `ProtoCapabilityDescriptor(Name, Kind, Source)`; built-in kinds in `ProtoCapabilityKinds`
  (`server`, `worker`, `device`, `protocol`, `browser`, `store`, `broker`, `data`, `document`).
- `[RequiresCapability(kind, CapabilityName = ...)]` is evaluated by the adapter before the test starts;
  a skip has no lifecycle. `[RequiresInProcess]` is `[RequiresCapability(server)]`.
- Honesty rule: a capability may only be declared by an integration that can serve it.
  `AddCapabilityUnlessConfigured` drops it when the environment provides the address — adopted only by
  `AddAspNetCoreServer` today (**→ AUDIT ADDR-1** completes the rule for Sql, Messaging, REST/GraphQL/
  gRPC; Web's absolute-URL case is a documented exception).
- Multi-instance capabilities must carry their instance (server name, backend name); today they do not
  (**→ AUDIT REG-2**).

## Address resolution (today vs target)

| Integration | Today | Target (audit ADDR-1/ADDR-2) |
| --- | --- | --- |
| REST / GraphQL / gRPC | static configuration only; throws at first use | one authority: settings → configuration; missing ⇒ inert + capability absent |
| ASP.NET Core server | static configuration decides step-aside; settings-published addresses do not step it aside (decided) | keep, document the asymmetry |
| Web sessions | settings first, then configuration; relative navigation throws without one | same authority; absolute-URL sessions stay addressless |
| Devices | settings first, then configuration; registration-time throw if neither | same authority |
| Readiness | settings → configuration, resolved at its registration position; may silently report "in-process" for a settings-published app | re-resolve after infrastructure starts before claiming in-process; one timeout owner (ADDR-3) |
| Sql / Messaging | connection resolved through DI factories/options; capability unconditional today | conditional capability on the address keys; inert until first use |

## Options

`IProtoConfigurableOptions` (`ConfigurationSectionName`, `BindFromConfiguration`, `Validate`) is the
shared shape. `ProtoOptionsRegistration.Configure<T>` composes code callbacks in order, binds the
section over them (configuration wins), and resolves one instance per consumer. Validation runs where
options resolve; a bad value fails there, not the first test.

Known outliers (sanctioned or audit-owed): Web backends validate through static delegates instead of
the interface method; `ProtoReadinessOptions` is not configuration-bindable and containers own a
second timeout (**→ ADDR-3**); the sink path binds but does not validate; OpenAPI/GraphQL schema
sources are read directly from configuration (decided in Audit 3 D5).

## Evidence boundary

- Findings, observations, attachments and archive entries are serialized and redacted **once**, where
  evidence is created — `ProtoMetadataRedaction`, `ProtoDataRedactionPolicy`, `JsonDiagnosticSanitizer`
  and `ProtoUriSanitizer` are the policies. No exit boundary may serialize a raw `object`.
- The trace records operations/events on a recorder; observations are intentional facts the test
  states and the only input coverage collectors consume; findings are for the report and run gates;
  attachments materialize files and are published in teardown.
- Artifact caps: `ProtoTraceOptions.EmbedArtifacts` (declare without bytes),
  `MaxArtifactBytes` (default 64 MB), `EmbedSources`, `CaptureSourceLocations`; tracing
  `Enabled = false` stops listening and sink capture, in-memory recording stays.
- Trace format compatibility: readers support the current major and the previous one; a breaking
  change bumps the major with a migration note (`docs/docs/observability/prototrace.md`).

## Context rules (Audit 3 CTX-1)

`Proto.Context` is for code that runs **inside a test on the test's flow**: test bodies and
test-author entries (`context.Data().For<T>()`, `row.ShouldMatchShape(...)`, assertions), and the
plumbing they call. Prefer explicit passing when it keeps a callee constructible in a unit test.
Run scope uses the host (`ProtoHost.CurrentHost`, or the reference a hook receives). Off-flow telemetry
uses `ProtoHost.FindTraceWriter(Activity?)` and correlates by trace id. An object whose lifetime spans
tests resolves per call and never holds a context.

## Vocabulary ownership

- One protocol descriptor per protocol (`ProtoProtocol`): trace source, operation names, observation
  kinds, coverage category, capability.
- Trace sources, observation kinds, coverage categories and entity kinds are constants where they are
  introduced, not literals at call sites (**→ AUDIT VOC-2**: web page kinds and `graphql.failure` still
  literals).
- Entity ids: `client:{type}:{name}`, `context:{type}`, `capability:{kind}:{name}`,
  `device:{client}:{id}` (target: include the device type, audit DEV-4), infrastructure `Id`, resources
  `Id`, value items `{type}:{identity}`.
- New vocabulary is additive to the wire; the viewer contract is not edited casually.

## Runner adapters

Each adapter owns its lifecycle boundary and maps the runner's result to the trace outcome; none
re-implements lifecycle. Consolidated facts (Audit 3 Stage 4): NUnit uses an `IWrapSetUpTearDown`
command wrapper (skip precedes `[SetUp]`, lifecycle spans setup/teardown); MSTest is one lifecycle per
data row; xUnit v3 traces theory rows by display name and always completes the scope; TUnit runs
reflection-less tests unwrapped; xUnit v2 names rows. `AdapterContract` is the shared compliance
suite; extend it, do not fork it.

## Inventory — the one of everything

| Concept | Type | Location |
| --- | --- | --- |
| Host builder | `ProtoHostBuilder` / `IProtoHostBuilder` | `src/ProtoTest.Core/` |
| Host | `ProtoHost` | `src/ProtoTest.Core/ProtoHost.cs` |
| Execution context | `ProtoExecutionContext`, `Proto.Context`, `ProtoHost.CurrentHost` | `src/ProtoTest.Core/` |
| Run state machine | `ProtoRunStateMachine` | `src/ProtoTest.Core/Internal/` |
| Test lifecycle | `ProtoTestLifecycle` | `src/ProtoTest.Core/Internal/` |
| Resource registry | `ProtoResourceRegistry`, `ProtoRunResourceStore` | `src/ProtoTest.Core/Internal/` |
| Infrastructure registration | `ProtoInfrastructureRegistration`, `ProtoInfrastructureSettings`, `IProtoConfiguredInfrastructure`, `ProtoInfrastructureContext` | `src/ProtoTest.Core/Infrastructure/` |
| Capability | `ProtoCapabilityDescriptor`, `ProtoCapabilityKinds`, `AddCapabilityUnlessConfigured` | `src/ProtoTest.Core/Applications/ProtoCapability.cs` |
| Client resolution | `ProtoClientRegistry`, `ProtoClientInitializerHook`, `ProtoClientResolution` | `src/ProtoTest.Core/Internal/` |
| Application resolution | `ProtoApplication`, `ProtoApplicationResolution`, `[Application]` | `src/ProtoTest.Core/Applications/` |
| Options | `IProtoConfigurableOptions`, `ProtoOptionsRegistration` | `src/ProtoTest.Core/` |
| Polling | `ProtoPolling` | `src/ProtoTest.Core/` |
| Flow | `ProtoFlow`, `ProtoStepDescriptor` | `src/ProtoTest.Core/` |
| Tracing | `ProtoTraceRecorder`, `ProtoTraceSession`, `ProtoTraceWire`, `ProtoTraceContracts` | `src/ProtoTest.Core/Tracing/` |
| Redaction | `ProtoMetadataRedaction`, `ProtoUriSanitizer`, `JsonDiagnosticSanitizer` | Core / ProtoTest.Json |
| Reporting | `IProtoSink`, `IProtoReportSource`, `IProtoCollector`, `ProtoReportItem`, run gates | `src/ProtoTest.Core/Reporting/` |
| Clock | `ProtoClock`, `ProtoTestTimeProvider`, `ProtoRequestClock` | `src/ProtoTest.Core/Time/` |
| Readiness | `ProtoReadiness`, `ProtoReadinessOptions`, `IProtoReadinessProbe` | `src/ProtoTest.Core/Readiness/` |
| Skip | `RequiresCapabilityAttribute`, `RequiresInProcessAttribute`, `ProtoTestSkip` | `src/ProtoTest.Core/Applications/` |
| Adapters | five runner packages + `tests/ProtoTest.AdapterContract` | `src/`, `tests/` |

## Invariants to protect

1. A test's outcome is recorded once; the adapter's result is authoritative and nothing overwrites it.
2. A resource's ownership period is always released, including after a failed release and a retried
   start.
3. A skipped piece is never owned, started or released.
4. Evidence is serialized and redacted once, at its boundary; cycles become `[circular]`, never a
   thrown export.
5. One client entity per client instance; configuration, operations and release reference the same id.
6. `Cancelled` / `Failed` / `Skipped` / `Unknown` mean one thing per producer and adapter.
7. Configuration errors fail configuration or resolve, not the first test observation.
8. A capability is declared only by something that can serve it; a dropped capability changes skips,
   never silently disables a live integration (**→ AUDIT REG-1**).
9. Core stays free of protocol/web/document vocabulary.
10. No user-facing artifact teaches a symbol that does not exist.
