# ProtoTest engineering facts — architecture

Current at the A4 stage tree (branch `version/1.1`; audit A1–A4 landed). Source wins over this
document: when it disagrees, the code is right and this file is the bug to fix in the same commit.

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
3. Computes dropped conditional capabilities: a declaration drops under its own condition -
   `AddCapabilityUnlessConfigured` when every key is configured, `AddCapabilityWhenProvided` when none
   of its keys is provided (a configured value or a key registered infrastructure declares, including
   pieces this build skips) - and a descriptor drops only when **every** declaration for it drops and
   no plain declaration promises it; the decision names the deciding keys and the reason in the trace.
4. Selects the run clock (a `ProtoClock` registered by `ConfigureClock`, else one starting now) and
   registers the `TimeProvider` bridge.
5. Registers the internal hooks (client initializer and completion, trace export, sink export, run gates,
   run resources) and report sources.
6. Builds the provider and **constructs every `IProtoCollector`** so a bad OpenAPI/GQL schema fails
   construction, not the first test.
7. Second `Build()` throws; every public registration entry that would mutate composition throws
   after `Build()` (a repeated same-name registration of the *same* program/type stays a no-op).

`StartAsync()`:

1. Run hooks in ascending `Order` — trace export (`int.MinValue`), run resources (`int.MinValue + 1`),
   sink export (`+2`), run gates (`+3`), HTTP auth (`100`).
2. Resets run-resource ownership for a retry, records capability descriptors as run entities.
3. Starts infrastructure in registration order; each started piece fills its declared settings keys,
   receives the run's readiness policy (`ProtoInfrastructureContext.Readiness`) and, for an address
   probe, the keys later pieces declare; readiness probes are ordinary infrastructure and are awaited
   at their registration position (a probe registered before its publisher records the ordering, not a
   mode lie); workers are infrastructure and start after everything registered before them; each
   worker receives the merged overlay (options over settings over configuration) as `--{key}={value}`
   arguments, so the worker's `Program.Main` sees final-precedence values, and the `HostBuilding`
   in-memory overlay stays as the fallback for an entry point that ignores args.
4. Starts the trace listener. A start failure rolls back the completed run hooks that own state, in
   reverse, so a suite-setup hook's state is released; the evidence hooks (gates, sink export, the
   archive) stay silent for a run that never started. It clears infrastructure settings and returns the
   host to Created for a retry.

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
2. `StartTestAsync` creates the DI scope, trace recorder and context, registers the test's clock in the
   host's `ProtoClockRegistry` (a failed start removes it; context disposal removes it), sorts
   attributes by `Order`, sets the ambient context, then runs hooks ascending (the built-in client
   initializer has `Order = int.MinValue`) and attributes ascending. It opens `test.execution` and
   makes it the parent. The caller's token becomes `ProtoExecutionContext.CancellationToken`, which
   hooks and attributes read and `ProtoTest.Sql` passes to the connection open and transaction begin;
   no runner adapter supplies one yet.
3. A setup failure records the test `Failed`, rolls back only the completed components (attributes and
   hooks in reverse), and rethrows.
4. Teardown runs attributes reverse, hooks reverse, publishes attachments, disposes the context
   (release owned resources in reverse registration order, then the DI scope), captures artifacts and
   completes the recorder. Every step is attempted; teardown failures become findings and never replace
   the adapter's result; a single exception is rethrown as-is, several aggregate.
5. `CompleteTestAsync` refuses a missing or foreign test; the context disposes once; resources release
   at most once. A scope disposed off the async flow that started it, or while another test is active,
   records a `Lifecycle` finding on its test and throws rather than completing silently.

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
| `ConfigureServices` / `ConfigureAppConfiguration` / `ConfigureTestIds` | composes (appends) | throws |
| `ConfigureTracing` / `AddResource` | composes / same instance no-op | throws |
| `AddTestHook` / `AddRunHook` / `AddRunGate` | every call adds another descriptor (no dedupe) | throws |
| `AddCapability` | dedupes by descriptor value (instance included) | throws |
| `AddCapabilityUnlessConfigured` | conditional declaration; drops when every key is configured; a descriptor drops only when every declaration for it drops and no plain declaration exists; key sets compare by content | throws |
| `AddCapabilityWhenProvided` | conditional declaration; drops when none of its keys is provided (configured value, or a key a registered infrastructure piece declares); composes with the other declaration kinds per declaration | throws |
| `AddSink<TSink>` | first per sink type wins; a repeated generic call appends its configure callback; direct DI registrations are wrapped once | throws |
| `AddInfrastructure` | same instance merges keys; different instance with the same id throws; skipped when every declared key is configured, unless `AddInfrastructureAlways` | throws |
| `AddApplication` | named; first client per protocol is the default | throws (application entries too) |
| Integration `AddClient` | first registration that initializes wins (keyed factory + resolver first match) | throws |
| `AddWorkerHost` / `AddAspNetCoreServer(name)` | `AddWorkerHost`: first per name wins and a repeated name with a **different program throws**; `AddAspNetCoreServer(name)` is still first-name-wins (A1R-01 residual, different `TProgram` under one name is a no-op) | a new registration throws; a repeated name is a no-op |
| `AddData` | composes onto one registry per builder | throws |
| `AddWeb` | one backend per host, first wins; only the winner declares the browser capability | throws |

The one rule to internalize: **first-wins guards must compare identity, not just the name**. A dropped
duplicate that is actually a different program, server, backend or device type is a silent wrong state
(audit DEV-1, DEV-6).

## Capabilities and skips

- `ProtoCapabilityDescriptor(Name, Kind, Source)` plus `Instance`: the name of one server, transport
  or application when the capability describes an instance, null when it describes the host as a
  whole. Built-in kinds live in `ProtoCapabilityKinds`
  (`server`, `worker`, `device`, `protocol`, `browser`, `store`, `broker`, `data`, `document`).
- `[RequiresCapability(kind, CapabilityName = ...)]` is evaluated by the adapter before the test starts;
  a skip has no lifecycle. `[RequiresInProcess]` is `[RequiresCapability(server)]`. A suite states a
  gate's reason once with `AddCapabilityReason(kind, reason, name?)` (the typed gates included); the
  attribute reads the reason for `(kind, CapabilityName ?? CapabilityInstance)` then `(kind, null)`
  before its default, and a per-test `Reason` still wins. `RequiresApplication` checks a declaration,
  not a capability, and keeps its own reason/default.
- Honesty rule: a capability may only be declared by an integration that can serve it.
  `AddCapabilityUnlessConfigured` decides **per declaration**: a descriptor drops only when every
  conditional declaration for it drops and no plain declaration promises it, so one satisfied
  declaration never drops a capability another unsatisfied declaration and its live integration still
  need. The skipped record carries the deciding keys (`capability.keys`) and the reason
  (`capability.reason`: `already configured`, or `no key provided`).
  `AddAspNetCoreServer` and `AddInProcessWebSocketDevices` use `AddCapabilityUnlessConfigured`;
  `UseRabbitMq`, `AddSql` and `AddEntityFrameworkCore` (the last sharing `SqlOptions.AddressKeys`)
  use `AddCapabilityWhenProvided`, so a missing address drops the capability and tests skip (ADDR-1,
  done for these). The HTTP family
  (REST/GraphQL/gRPC) keeps the unconditional protocol capability: one capability covers every client
  of the protocol, and an application-scoped client is legitimately served in-process with no address,
  so a per-key drop cannot be expressed without skipping configured clients or hiding the in-process
  path; the missing-address failure stays at first use with the resolver message (decided in A2b).
  Web's absolute-URL case is the documented domain exception.
- Multi-instance capabilities carry their instance: two named servers are two descriptors, two
  capabilities and two run entities (`server:ASP.NET Core:A`), so configuring A's address drops only
  A's. `HasCapability(kind)` matches any instance; `HasCapability(kind, name)` matches the descriptor
  `Name`, not the instance; `HasCapability(kind, name, instance)` narrows by both (every non-null filter
  must match), and `[RequiresServer(name)]` uses it to address one named `AddAspNetCoreServer`
  instance. `[RequiresWorker<TProgram>]` checks the worker capability by the program assembly name,
  `[RequiresApplication(name)]` checks `ProtoHost.HasApplication(name)` (the `AddApplication`
  declaration), and `RequiresCapabilityAttribute.CapabilityInstance` exposes the instance filter to
  open kinds. The Web pair leaves exactly the winning backend's browser capability.

## Address resolution (one authority per application)

An application-scoped setting resolves through one named precedence, `ProtoApplication.ResolveSetting`
(a started piece's published settings → static configuration), exposed as
`ProtoApplication.BaseUrl(context, app)` and `ProtoApplication.GrpcAddress(context, app)`. Every
address reader uses it; the in-process transport is the client resolver's fallback, never a competing
source. A first-reader-wins divergence is a bug (audit ADDR-2, fixed).

| Integration | Rule |
| --- | --- |
| REST / GraphQL / gRPC | settings-published address first, then configuration (gRPC: `Grpc:Address`, then `BaseUrl`); the in-process transport serves only when neither resolves. A missing address fails at first use with the resolver message; the protocol capability stays (A2b decision: one capability covers every client of the protocol, and an application-scoped client is legitimately in-process with no address) |
| Readiness (`AddHttpReadiness`) | same precedence at its registration position; a probe before the publisher records `readiness.skipped` naming the ordering requirement, and claims "in-process" only when a server capability backs the application |
| ASP.NET Core server | step-aside reads static configuration only (decided asymmetry); a settings-published address leaves the server in place while every address reader follows the published process |
| Web sessions | same precedence; absolute-URL sessions stay addressless |
| Devices | same precedence; registration-time throw when neither resolves. A device client carries the application it was registered under, and the in-process transport is selected by that identity: a transport serves one `(TProgram, application)` pair, so a client for B is never routed through A's `TestServer` at the same path. A path-only client (no resolver) with no matching transport fails naming the application |
| Sql / Messaging | connection resolved through DI factories/options; the capability is declared with `AddCapabilityWhenProvided` over the keys that can provide the address (`SqlOptions.AddressKeys`, `RabbitMqOptions.ConnectionStringSetting`), so a run with none drops it and skips; the SQL connection hook and the Entity Framework Core enlistment hook (same keys) stay inert (no open, no context), and the accessors name the keys |

## Device stack (A4, frozen until R2 needs more)

- Routing identity: `AddInProcessWebSocketDevices<TProgram>(application)` registers one transport per
  `(TProgram, application)`; `IProtoInProcessDeviceTransport.CanConnect(context, applicationName)`
  answers for the requested application and the server factory it needs. The interface is unreleased
  1.1 plumbing; the endpoint parameter was dropped rather than ignored.
- Options: the in-process transport resolves the registered `WebSocketDeviceOptions` from DI (validated
  with the transport, like the socket path) and applies `ConnectTimeout` to the in-process connect.
- Concurrency contract on `ProtoDevice`: connection creation is single-flight, sends are serialized,
  one receive is in flight at a time (a second fails fast naming the device), a send that races a
  disconnect fails with a device error naming the device, and send/receive may run concurrently.
- Identity: resource and trace entity ids are `device:{client}:{type}:{id}`; the entity state carries
  `device.type` and `device.connected`, and the release path is a real disconnect
  (`device.disconnect` + `device.connected = false`), so an explicit disconnect-then-reconnect and a
  test that never disconnects both end disconnected.

## Options

`IProtoConfigurableOptions` (`ConfigurationSectionName`, `FallbackConfigurationSectionName`,
`BindFromConfiguration`, `Validate`) is the
shared shape. `ProtoOptionsRegistration.Configure<T>` composes code callbacks in order, binds the
section over them (configuration wins), and resolves one instance per consumer. Validation runs where
options resolve; a bad value fails there, not the first test. Sections follow
`ProtoTest:<Integration>[:<Area>]`, where the area names the options type's role (`Responses`,
`Attachments`, `Client`, `WebSocket`, `RabbitMq`); an integration with one options set has no area
segment. A renamed section returns its old name from `FallbackConfigurationSectionName`: the fallback
binds first and the current section binds over it, so the old key keeps working (documented as
deprecated) and the current key wins. `GrpcClientOptions` is the example — `ProtoTest:Grpc:Client` over
the legacy `ProtoTest:Grpc` — and it is registered **per named client**: the keyed registration
composes each client's callbacks in order, the shared section binds over each client's callback, and
the unkeyed instance is the run-wide default a transport-backed fallback client reads.

Known outliers (sanctioned or audit-owed): Web backends validate through static delegates instead of
the interface method; the sink path binds but does not validate; OpenAPI/GraphQL schema sources are
read directly from configuration (decided in Audit 3 D5). `ProtoReadinessOptions` implements
`IProtoConfigurableOptions` (section `ProtoTest:Readiness`), is bound at `Build`, and is handed to
infrastructure through `ProtoInfrastructureContext.Readiness`, so one policy governs host probes and
the containers the run starts (ADDR-3 fixed); a container started outside a host keeps its own
`ReadinessTimeout`/`ReadinessInterval`.

`SqlOptions.AddressKeys` is a `SqlAddressKeys` value object, deliberately outside configuration
binding: the keys are declared in code because the Build-time capability decision cannot see a value
that binds later, and a bound-in key would let the runtime rule disagree with the decision.
`AddEntityFrameworkCore` reads the declaration `AddSql` recorded when it registers, so it shares the
same key set when it is called after `AddSql`.

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

## Assertion surface

- Every ProtoTest-owned assertable subject exposes `Should` (and `ShouldNot` where a negated form is
  meaningful); each member returns the subject, so assertions chain. The pre-facade spellings are
  `[Obsolete]` delegating shims (DX-01/DX-02), never a second implementation.
- Shape is positive-only and lives on the positive facade or a factory because C# has no extension
  properties: `response.Should.MatchShape(shape)` (REST/GraphQL), `ProtoGrpcAssertions.For(reply)`,
  `message.Should.MatchShape(shape)`, `row.Should.MatchShape(shape)` (table rows). A model row is a
  user type, so `row.ShouldMatchShape(shape)` stays the documented generic-subject extension.
- A shape producer wraps the shared matcher failure after `ProtoShapeAssertion.Assert` has recorded and
  failed the operation, so trace attributes/sections/observations stay byte-identical: the protocol's
  assertion exception starts with the subject (`request.identifier`, `graphql.operation`, the message
  type, `messaging.destination`, or the row's `Sheet!Range`) and keeps the matcher exception - with
  `Mismatches` - as `InnerException`. The GraphQL data-less path keeps its own message and section.

## Context rules (Audit 3 CTX-1)

`Proto.Context` is for code that runs **inside a test on the test's flow**: test bodies and
test-author entries (`context.Data().For<T>()`, `row.ShouldMatchShape(...)`, assertions), and the
plumbing they call. Prefer explicit passing when it keeps a callee constructible in a unit test.
Run scope uses the host (`ProtoHost.CurrentHost`, or the reference a hook receives). Off-flow telemetry
uses `ProtoHost.FindTraceWriter(Activity?)` and correlates by trace id. An object whose lifetime spans
tests resolves per call and never holds a context. `context.UniqueName(name, sequence)` derives a
deterministic test-scoped name (`name-{TestId}`, `name-{TestId}-{sequence}`) for records that outlive
the process; `ProtoTest.Data`'s per-member generated defaults are deterministic per test in the same
spirit (see `ProtoDataValueContext`).

## Vocabulary ownership

- One protocol descriptor per protocol (`ProtoProtocol`): trace source, operation names, observation
  kinds, coverage category, capability.
- Trace sources, observation kinds, coverage categories and entity kinds are constants where they are
  introduced, not literals at call sites (VOC-2 fixed, DX-17 included: `WebPageInventory` owns
  `web.page.available|visited|verified`, each protocol's builder owns its `*.failure` and
  `*.contract.shape` kinds — `ProtoMessagingProtocol.ShapeObservationKind` for Messaging, which has no
  builder type of its own).
- `sheets.workbook` is record-only evidence: opening a workbook is not an assertion, so
  `SheetsCoverageCollector` consumes only `sheets.range` (decided in A5; not changed).
- Messaging records the `messaging.publish` operation and the `messaging.published`, `messaging.receive`,
  `messaging.failure` and `messaging.contract.shape` observations as trace evidence and ships no collector;
  destinations are deliberately not a coverage category (A5 VOC-1 decision: the promise was deleted, not
  shipped).
- Entity ids: `client:{type}:{name}`, `context:{type}`, `capability:{kind}:{name}` (with `:{instance}`
  when the descriptor carries one), `device:{client}:{deviceType}:{id}`, infrastructure `Id`, resources
  `Id`, value items `{type}:{identity}`.
- New vocabulary is additive to the wire; the viewer contract is not edited casually.

## Runner adapters

Each adapter owns its lifecycle boundary and maps the runner's result to the trace outcome; none
re-implements lifecycle. Consolidated facts (Audit 3 Stage 4): NUnit uses an `IWrapSetUpTearDown`
command wrapper (skip precedes `[SetUp]`, lifecycle spans setup/teardown); MSTest is one lifecycle per
data row; xUnit v3 traces theory rows by display name and always completes the scope; TUnit runs
reflection-less tests unwrapped; xUnit v2 names rows. Classification is shared:
`ProtoTestResult.FromException` (with `IsCancellation(string)` for xUnit v3's result state) records a
runner-reported cancellation as `Cancelled` and anything else as `Failed`; NUnit is the documented
exception (its result carries no exception, so a cancelled test reads `Failed`). xUnit v2 completes the
scope in a `finally`; TUnit appends row arguments through `ProtoTestName.ForRow`, the same form MSTest
records. `AdapterContract` is the shared compliance
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
| Capability | `ProtoCapabilityDescriptor`, `ProtoCapabilityKinds`, `AddCapabilityUnlessConfigured`, `AddCapabilityWhenProvided` | `src/ProtoTest.Core/Applications/ProtoCapability.cs` |
| Client resolution | `ProtoClientRegistry`, `ProtoClientInitializerHook`, `ProtoClientResolution` | `src/ProtoTest.Core/Internal/` |
| Application resolution | `ProtoApplication`, `ProtoApplicationResolution`, `[Application]` | `src/ProtoTest.Core/Applications/` |
| Options | `IProtoConfigurableOptions`, `ProtoOptionsRegistration` | `src/ProtoTest.Core/` |
| Polling | `ProtoPolling` | `src/ProtoTest.Core/` |
| Flow | `ProtoFlow`, `ProtoStepDescriptor` | `src/ProtoTest.Core/` |
| Tracing | `ProtoTraceRecorder`, `ProtoTraceSession`, `ProtoTraceWire`, `ProtoTraceContracts` | `src/ProtoTest.Core/Tracing/` |
| Redaction | `ProtoMetadataRedaction`, `ProtoUriSanitizer`, `JsonDiagnosticSanitizer` | Core / ProtoTest.Json |
| JSON value read | `JsonPathResolver`, `JsonPathException` | `src/ProtoTest.Json/` |
| Reporting | `IProtoSink`, `IProtoReportSource`, `IProtoCollector`, `ProtoReportItem`, run gates | `src/ProtoTest.Core/Reporting/` |
| Clock | `ProtoClock`, `ProtoTestTimeProvider`, `ProtoRequestClock`, `ProtoClockRegistry` | `src/ProtoTest.Core/Time/` |
| Readiness | `ProtoReadiness`, `ProtoReadinessOptions` | `src/ProtoTest.Core/Readiness/` |
| Skip | `RequiresCapabilityAttribute`, `RequiresInProcessAttribute`, `RequiresWorkerAttribute<TProgram>`, `RequiresServerAttribute`, `RequiresApplicationAttribute`, `ProtoTestSkip` | `src/ProtoTest.Core/Applications/` |
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
   never silently disables a live integration.
9. Core stays free of protocol/web/document vocabulary.
10. No user-facing artifact teaches a symbol that does not exist.
