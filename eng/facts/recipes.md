# ProtoTest engineering facts — recipes

How to add things the ProtoTest way. Every recipe names the canonical example to copy; if no example
fits, that is a design question for the plan, not a reason to invent a second mechanism. Read
`architecture.md` first, and `gotchas.md` before touching lifecycle, addresses, capabilities or the
clock.

## Before you write code

1. Find the existing thing that already does this. Search for the concept in `src/ProtoTest.Core` and
   in a comparable integration (`Rest`, `Grpc`, `Devices`, `Sheets`, `Messaging`) before inventing.
2. If nothing fits, write the finding first (audit plan or plan-5 decision) — do not create a
   provider/manager/factory as a first move.
3. The rule of the last three audits: **delete → simplify → consolidate → make explicit → abstract**.
   A new type must pay for itself with behavior, not with symmetry.

## Recipe: a new integration package

Copy the shape of `ProtoTest.Messaging` or `ProtoTest.Devices` (small) or `ProtoTest.Rest` (protocol).

1. Project under `src/ProtoTest.<Name>/`, multi-target `net8.0;net9.0;net10.0`, `IsPackable=true`,
   central package versions in `Directory.Packages.props`.
2. `Add<Name>(this IProtoHostBuilder)` and, where applications are meaningful, the
   `IProtoApplicationBuilder` overload. In the call: register options (see the options recipe),
   declare the capability with a `ProtoCapabilityDescriptor`, and register lifecycles
   (`TryAddEnumerable` for hooks) so repeated calls compose.
3. If the integration owns an address, pick the conditional registration that matches the failure
   direction: `AddCapabilityWhenProvided` when a missing address must make the integration inert (drop
   → skip, never a setup failure) — `AddSql`/`SqlOptions.AddressKeys`, `AddEntityFrameworkCore` (same
   keys) and `UseRabbitMq` are the canonical examples — and `AddCapabilityUnlessConfigured` when the
   environment already provides what the integration would start (`AddAspNetCoreServer`, the in-process
   device transport). That is the P4f consumer rule (audit ADDR-1). A missing address must produce a
   skip, not a setup failure.
4. Options implement `IProtoConfigurableOptions` with a section name and `Validate()`; register through
   `ProtoOptionsRegistration.Configure` (callbacks compose, configuration binds over them).
5. Trace vocabulary: one trace-source constant, operation names and observation kinds as constants at
   the producer; a collector if observations should reach the report.
6. Tests: a real composition through the host builder, the failure path, a repeat-registration test,
   and a parallel-safety decision. Add the project to `tests/` and to the pack gate
   (`eng/pack.ps1`).
7. Docs: `README.md` with install line and a Limits section; a docs page under
   `docs/docs/integrations/<name>/` with Limits; `CHANGELOG.md` entry; update
   `eng/facts/architecture.md` if the model gained a concept.
8. `eng/verify.ps1` (or test + lint + check-docs) and `eng/pack.ps1` green.

## Recipe: a protocol client

Compare `ProtoHttpClientRegistration`/`ProtoHttpClientResolver` (REST/GraphQL), `ProtoGrpcClientRegistration`,
and the device client (`ProtoDeviceClient` + registration store). The shared rules:

- Named clients with `Default` fallback; resolution order explicit name → `[Application]` binding →
  the application's first client for the protocol → `Default`.
- A client is a test resource; disposal is the context's job unless ownership is explicitly external.
- Record the client as a trace entity (`ProtoTraceEntityKinds.Client`) with the same id shape the
  other protocols use; one entity per instance.
- First registration wins per name; a repeated registration must not silently replace an initializing
  client. If a losing registration describes a *different* thing, fail loudly (`AddWorkerHost` compares
  the program; the in-process device transport compares `(program, application)`).
- An in-process application transport is preferred when the application it belongs to is hosted;
  otherwise the configured address. The transport is selected by the client's application identity,
  never by path or first match. Do not branch on environment in user code.
- Client options are **per named client**: register them keyed by the scoped client name and let one
  name's callbacks compose in order; the unkeyed instance stays the run-wide default for a
  transport-backed fallback client, and the shared section binds over each client's callback.
- Response reads go through the shared mechanics: `ProtoJsonRead.Read` with the protocol's
  `ProtoJsonReadSemantics` for JSON-at-path reads, and `ProtoHttpResponse.ResolveStatusDiagnosticOptions`
  for status-body clamping; do not hand-roll a second reader.

Accessor shape: `context.<Protocol>()` returns a **client** for the protocols that address named clients
(Rest, GraphQL, gRPC, Devices, Web) and the **capability interface** for the integrations that own one
thing per run (Data, Sql, Sheets); a named client accessor fails naming the `Add*` call when the name is
unknown.

## Recipe: infrastructure (a run-owned piece)

`AddInfrastructure(piece, keys)` owns and releases it, starts it with the run in registration order,
and fills each declared key with `IProtoConnectionInfrastructure.ConnectionString` (or
`IProtoSettingsInfrastructure.Settings`). Implementation rules:

- `Scope` must be `ProtoResourceScope.Run`; `Id` is stable and unique.
- Declare **every** key the piece fills: `AddInfrastructure` skips the piece only when all declared
  keys are configured, and `AddInfrastructureAlways` is the opt-out.
- Implement `IProtoConfiguredInfrastructure.StartAsync(ProtoInfrastructureContext)` when the piece needs
  the run's settings/configuration (for example a worker). The default interface method keeps plain
  `IProtoInfrastructure` implementers compiling.
- Start failures must leave the piece retryable (the host re-arms it on a retried start); release must
  be idempotent.
- Long waits go through readiness (`AddReadinessProbe` or `ProtoContainerResource.ReadyOn`), never a
  `Task.Delay` in setup.
- Add a conditional path test (keys configured → not started, never owned) and an error-path test.

## Recipe: a product under test that resolves its addresses at use time

A product hosted by the suite sees the run's settings only after its entry point has run, so an eager
`configuration[...]` read in `Program.Main` is inert in container and configured modes (`Main` itself is
audit CFG-1, fixed in A3).

- Messaging: copy `OpenCsms.Messaging.RabbitMqEventPublisher` — take `IConfiguration`, resolve the
  connection string on first publish, connect lazily, and implement `IAsyncDisposable` so the host
  disposes what it created.
- Data: read the connection string inside the provider's options factory (`OpenCsms.Data.AddCsmsData`);
  do not capture it at registration.
- The configuration error then surfaces at first use, naming the key; the suite's capability gates keep
  tests from reaching that path when the address is absent.

## Recipe: a conditional capability / an integration that can be inert

```csharp
// Drop when the environment already provides the address elsewhere:
builder.AddCapabilityUnlessConfigured(
    new ProtoCapabilityDescriptor("MyAdapter", ProtoCapabilityKinds.Protocol, "ProtoTest.MyAdapter"),
    "ProtoTest:MyAdapter:Address");

// Drop when no address can exist (configured value or a key registered infrastructure declares):
builder.AddCapabilityWhenProvided(
    new ProtoCapabilityDescriptor("MyAdapter", ProtoCapabilityKinds.Protocol, "ProtoTest.MyAdapter"),
    "ProtoTest:MyAdapter:Address");
```

The rule (audit REG-1/REG-2, landed in A1; ADDR-1 in A2b): a descriptor drops only when every
conditional declaration for it drops and no plain declaration promises it; a capability that describes
an *instance* carries `Instance`; and a capability is declared only by the registration that won a
first-wins race. `[RequiresCapability]` then skips instead of failing. When the integration would fail
at setup or first use without an address, pair `AddCapabilityWhenProvided` with an inert use path that
names the missing keys (`AddSql`/`SqlOptions.AddressKeys` and `UseRabbitMq` are the canonical
examples).

## Recipe: configurable options

1. Class with a `ConfigurationSectionName` constant and defaults; implement
   `IProtoConfigurableOptions`. The section follows `ProtoTest:<Integration>[:<Area>]`, where the area
   names the options type's role (`Responses`, `Attachments`, `Client`, `WebSocket`, `RabbitMq`); an
   integration with one options set has no area segment.
2. Register with `ProtoOptionsRegistration.Configure<T>(services, () => new T(), configure)` so code
   callbacks compose in order and the section binds over them (configuration wins).
3. Validate real invariants in `Validate()`; it runs where options resolve. If a value must fail host
   construction, construct the consumer at `Build()` (the collector precedent).
4. A renamed section keeps its old key working for one release by returning the old name from
   `FallbackConfigurationSectionName`, documented as deprecated; the current section binds over the
   fallback, so its value wins. Remove the fallback at the next major.
5. Test: code value, configuration override, the validation failure, and (for a fallback) both sections
   binding with the current one winning.

## Recipe: an observation and its coverage

1. The producer records `RecordObservation(targetName, kind, identifier, data, metadata)` where the
   identifier is the thing a user would call covered (a route, a property path, a device command).
2. Kinds and categories come from the protocol descriptor/constants, not literals (audit VOC-2).
3. A collector derives from `ProtoCoverageCollector(targetName)`; `CanCollect` matches the target and
   kind; the identifier becomes a coverage item with a hit count. Only an assertion-level observation
   counts as covered — observed is not covered.
4. Register it where the rest of the protocol registers (`AddCollector<T>` on the target, or
   `TryAddEnumerable` when intrinsic to the package) and document whether it is manual or automatic.
5. Metadata passes the evidence boundary: no raw `object` exits.

## Recipe: trace operations and entities

- Open operations on the ambient recorder (`context.Trace`) around the real work; name them
  `{protocol}.{action}`; set attributes as strings with the shared key vocabulary.
- Entity ids must match the cross-protocol shapes in `architecture.md`; `SetEntityState` writes only
  the keys that changed.
- Sensitive values are redacted at the boundary (`ProtoHttpDiagnosticSanitizer`,
  `ProtoMetadataRedaction`, `JsonDiagnosticSanitizer`); never trace header values, tokens or user
  credentials.
- New wire vocabulary is additive; the viewer contract is not edited casually.

## Recipe: a skip condition

Prefer the shipped ones: `[RequiresCapability(kind, CapabilityName = ...)]` (or
`CapabilityInstance = ...`), `[RequiresInProcess]`, `[RequiresDevice<T>]`,
`[RequiresWorker<TProgram>]`, `[RequiresServer(name)]`, `[RequiresApplication(name)]`. A new condition
implements `IProtoSkipCondition` and is evaluated by adapters before `StartTestAsync` (no lifecycle, no
context). The reason must name what is missing and how to provide it.

## Recipe: a runner adapter

Copy an existing adapter and extend `tests/ProtoTest.AdapterContract`. An adapter: resolves attributes
and skip reasons, starts/completes the lifecycle through the framework's entry points (never
re-implements lifecycle), maps the runner's outcome to `ProtoTestResult`, and publishes attachments
through the runner's API. Add the real-run lifecycle boundary tests (skip-before-setup, setup failure,
teardown failure, outcome capture, cancellation) and run `dotnet test` through the adapter — unit
tests that call hooks directly create false confidence (Audit 3 class 7).

## Recipe: a test double

Put shared doubles in `tests/ProtoTest.TestSupport` (`StubHttpHandler`, `StubTransportInitializer`,
`StaticConfigurationSource`, temp-trace helpers, `FreePort`). Copy nothing twice; the lint gate greps
for known duplicates (audit TST-2).

## Test style (samples, the template and new tests)

- Group assertions with `using (Assert.EnterMultipleScope()) { … }` (NUnit 4), not
  `Assert.Multiple(() => …)`; older tests are not churned for style.
- Integration journeys, samples and the template use a PascalCase sentence
  (`CreatingAnOrderReturnsIt`); pure unit fixtures keep `Subject_ShouldOutcome`.
- `using` directives go inside the file-scoped namespace; only an assembly-attribute file (the TUnit
  and xUnit v3 `Setup.cs` files) puts them before the namespace.
- A suite states a capability gate's skip reason once with
  `AddCapabilityReason(kind, reason, name?)`; a gate's own `Reason` still overrides it, and an
  unregistered `(kind, name)` falls back to the attribute's default message.

## Anti-patterns (audit-backed)

- **A second registry/resolver/lookup** for a concept Core already owns. Key an existing one by the
  identity you need (host + test id) instead (audit CFG-3).
- **A provider/manager/factory wrapping one constructor** to look architectural.
- **A process-global map without an owner.** `ProtoHostRegistry.FindTraceWriter` is the pattern.
- **`Proto.Context` reads in builders.** It is for code running inside a test's flow; run scope uses
  the host (Audit 3 CTX-1).
- **Serializing raw metadata at an exit boundary.** The evidence boundary owns serialization and
  redaction.
- **A capability declared by a registration that can lose a first-wins race** (audit REG-2) or by an
  integration whose address can be missing (audit ADDR-1).
- **`Task.Delay` in setup.** Use readiness.
- **A first-wins guard that compares only a name.** Compare the identity; throw on a real conflict
  (audit DEV-6).
- **A poll/retry loop of your own.** Use `ProtoPolling` / `ProtoFlow` / `ProtoReadiness`.
- **Editing the trace wire or viewer contract to fit one integration.**
- **Advertising a package or API in docs before it ships** (never-advertise rule).

## Change checklist (every behavior change)

- [ ] The plan item it belongs to is named in the commit.
- [ ] Characterization test first where the behavior changes deliberately.
- [ ] Failure path and parallel-safety considered.
- [ ] Trace conventions documented if new vocabulary exists.
- [ ] README/docs Limits updated; `CHANGELOG.md` entry.
- [ ] `eng/facts/` updated when the model changed.
- [ ] `eng/verify.ps1` green; `eng/pack.ps1` when packaging changed.
- [ ] Plan row updated with the evidence line.
