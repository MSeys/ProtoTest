# ProtoTest Internal Audit 2 — Merged Plan

Audit date: 2026-09-24 · Baseline commit: `fc0afa2`
Scope: all of `src/` (28 projects), `tests/` (25 projects), plus the trace wire contract.
Method: full read of `ProtoTest.Core`; three deep-dive passes (HTTP/REST/GraphQL/OpenApi/Json,
Sql/Data/Testcontainers/Messaging/Sheets, Web/AspNetCore/OpenTelemetry/Reporting); gRPC read
directly; every finding verified against source. An independent consumer review (docs and packages
only, no source) was cross-checked and folded in as F17–F21.

**Goal:** finish the convergence the first cleanup started — a Core that stays a testing/execution
kernel, integrations that participate in one model instead of parallel-implementing, and failure
diagnostics that agree with themselves.

## Locked decisions

- Fix the client-initialization contract by making an unscoped provider initialize **once per
  (clientType, name)** and aliasing its instance into every chain that names it; built-in
  integrations must be able to use the same mechanism external ones use.
- Core must not carry web-route or document-format semantics; shared domain concepts live with an
  integration or in a shared domain package.
- Redaction and diagnostic serialization are one policy applied where evidence becomes artifact
  data, not three per-axis implementations; degradation is per element, never all-or-nothing, and
  never prints CLR/compiler type names.
- A test's outcome is recorded once; the adapter's result is authoritative and nothing downstream
  overwrites it.
- Pure functions stay static; anything with policy, IO or state becomes an owned service only when
  substitution or cancellation is actually required.
- `ProtoFlow` earns its place or goes: it gets per-step trace identity, is piloted on the lifecycle
  teardown behind characterization tests, and is reduced if the pilot does not clearly pay.
- Do not touch: the run/test state machines, `ProtoResourceRegistry`, `ProtoTestScope`'s teardown
  swallow, per-adapter `MapResult`, the trace model and wire contract, `AdapterContract`.

## Rules

1. Every behavior change lands with the characterization test that pins the new behavior, and the
   old behavior is pinned first where it is being changed deliberately.
2. Every stage ends green: solution build, the affected test projects, and `dotnet format`.
3. A stringly contract becomes a descriptor, enum or constant at the point it is introduced.
4. Public surface changes are additive except for accidental-public plumbing, and each is called
   out in the commit message.
5. Nothing is deferred into prose: a consciously postponed item gets a stage entry or is recorded
   under "Decisions taken".

## Findings register

Severity: CRITICAL / HIGH / MEDIUM / LOW / OBSERVATION. Confidence: H / M / L.
Compatibility: I = internal, B = behavioral source-compatible, E = extension API, C = consumer API.

| ID | Finding | Sev | Conf | Compat |
| --- | --- | --- | --- | --- |
| F1 | Core carries web-route semantics (`WebPagePath`/`WebPageInventory`/`WebPageConfig`) and document sniffing (`ProtoDocumentSource`), plus a stack-frame prefix list naming integration dependencies | HIGH | H | I/E |
| F2 | Client initialization: an unscoped provider is invoked once per protocol chain, so two protocols sharing a client name fail with a registry conflict; the provider-idempotency contract is implicit | HIGH | H | I/B |
| F3 | Redaction is per-axis: malformed JSON bypasses redaction, and the report axis has none while the trace redacts the same metadata | HIGH | H | B |
| F4 | A test's outcome has three sources of truth; artifact-capture failure rewrites the trace outcome and can replace the assertion error | MEDIUM | H | B |
| F5 | Container resources are one-shot while the host promises restartability; `ConnectionString` survives release | MEDIUM | H | B |
| F6 | `ProtoFlow` is a public primitive with one consumer and the wrong feature surface (retry unused; per-step trace identity missing) | MEDIUM | H | C |
| F7 | One `AddClient` writes five marker records; consumers scan them with different rules | MEDIUM | H | I |
| F8 | Trace entry handling is O(n²) (`ResolveOutcome`, `ChildEvents`); the run retains every recorder | MEDIUM | H | I |
| F9 | `WebSession` caches a faulted backend task when the factory fails synchronously | MEDIUM | H | I |
| F10 | REST object bodies serialize with STJ defaults (PascalCase), contradicting `ProtoJsonDefaults.Web` and GraphQL | MEDIUM | H | B |
| F11 | Core's optional-service lookups silently degrade a hand-built `ProtoHost(IServiceProvider)` | LOW-MED | H | C |
| F12 | Cancellation is advertised on test-scoped extension points that cannot deliver it; `ProtoDocumentSource` blocks | LOW-MED | H | E |
| F13 | Three trace format versions, one stale (`CurrentFormatVersion = "1.9"`, never read) | LOW | H | I |
| F14 | Adapter per-test state lives on attribute instances (NUnit, xUnit v3) — parallel parameterized cases unverified | LOW-MED | L | I |
| F15 | Integration-local issues: RabbitMQ async-dispatch flag, blocking broker calls, taps after reconnect, test-side container race; SQL double disposal; `AddData` partial registration | LOW | H | B |
| F16 | Sink registration manipulates the service collection by index | LOW | M | I |
| F17 | Diagnostic serialization is all-or-nothing and leaks compiler-generated type names (`<>z__ReadOnlyArray…`) into the shape-mismatch trace value | MED-HIGH | H | I |
| F18 | Foundation dependencies one major behind (`RabbitMQ.Client 6.8.1`, `Microsoft.OpenApi.Readers 1.6.31`); wrappers inherit the old APIs | MEDIUM | H | I |
| F19 | Retry/attempt trace semantics undocumented; today one record per attempt with no correlation | LOW-MED | M | B |
| F20 | NUnit failure output reportedly prints the stack trace twice — unverified | LOW | L | I |
| F21 | No run-output channel; the resolved trace path is invisible | LOW | H | OBS |

Observations (not defects): scalar JSON read ergonomics (`ReadAsJson<T>` needs a wrapper record for
one property); the ecosystem roadmap is absorbable by the current model except per-test service
substitution, which needs one deliberate new seam; the name "ProtoTest" reads as "prototype test".

## Stage A — Characterization tests (no production change)

- [x] A1. F2: two protocols with the same client name plus an unscoped provider serving both — one
  instance, no throw, working fallback; unscoped provider invoked once per name.
  (`ProtoClientInitializerTests.UnscopedInitializer_ShouldServeEveryProtocolChainOnce`.)
- [x] A2. F3: malformed JSON with a sensitive key stays redacted; observation metadata with a
  sensitive key in the report; the same secret in the trace stays redacted.
  (`JsonShapeMatcherTests.DiagnosticSanitizer_ShouldRedactSensitiveKeysInMalformedJson`,
  `ProtoFindingTests.AddFinding_ShouldRedactSensitiveMetadataBeforeTheReport`,
  `ProtoExecutionContextTests.RecordObservation_ShouldRedactSensitiveMetadataBeforeTheReport`.)
- [x] A3. F4: artifact-capture failure on a passing test is pinned by
  `ProtoLifecycleFailureTests.ArtifactCaptureFailure_ShouldRecordPartialWithoutReplacingTheResult`
  (the result stays authoritative; the failure is a teardown finding) and the failed-test case is
  pinned by `FailingTeardown_ShouldKeepTheOriginalFailureAndRecordTheTeardownFailure`.
- [x] A4. F5: container start → release → start and `ConnectionString` after release.
  (`ProtoContainerResourceTests.StartAsync_AfterRelease_ShouldStartAFreshContainer`; the host retry
  release is pinned by `ProtoHostStartStopTests.StartAsync_WhenInfrastructureFails_ShouldReleaseEachOwnershipAndAllowRetry`.)
- [x] A5. F9: `WebSession` factory that throws synchronously then succeeds on retry.
  (`WebModelTests.WebSession_ShouldRetryBackendCreationAfterASynchronousFailure`.)
- [x] A6. F14: two parallel parameterized cases of one `[ProtoTest]` method (NUnit).
  (`ParallelParameterizedTests`, with the fixture teardown asserting two completed traces.) **The
  test proved the defect**: one case recorded `Unknown` because the attribute instance's scope
  field was clobbered; both adapters now key the scope by the test.
- [x] A7. F17: a mismatch whose expected shape contains `JsonValue.GreaterThan(...)` — the trace
  value names every mismatch and contains no `<>`, `z__` or backtick type text.
  (`ProtoShapeAssertionTests.Assert_ShouldRenderMismatchesThatCarryAConstraintWithoutCompilerTypeNames`.)
- [x] A8. F19: NUnit `[Retry]` interaction: each attempt is its own lifecycle record.
  (`RetryTests.EachAttempt_ShouldHaveRecordedItsOwnTrace`.)
- [ ] A9. F20: capture NUnit's failure output — closed as no source cause (Stage E5).

## Stage B — Localized fixes (behavior, reversible)

- [x] B1. F17: mismatches are projected per element; the sanitizer degrades collections per item
  and reports a display type, never a compiler-generated name.
- [x] B2. F3: malformed JSON is redacted by a value-pattern pass; report metadata (findings and
  coverage items) is redacted with the shared name list at item creation.
- [x] B3. F5: `ConnectionString` is cleared on release; containers are restartable, and the host
  re-arms a resource it starts again so the retry's new ownership period is released. Docs updated.
- [x] B4. F9: the backend task is published only for a settleable start; a faulted creation clears
  the cache via a continuation, so the retry is race-free.
- [x] B5. F10: REST `Body(object)` defaults to `ProtoJsonDefaults.Web`; test + changelog.
- [x] B6. F13: the stale snapshot version constant is gone; the run reports the span format version.
- [x] B7. F15: RabbitMQ consumes asynchronously; the RabbitMQ test fixture starts one container
  through a `Lazy` and reports the start error in the skip; `AddData` adopts its registry only
  once `configure` returns. **Decision:** the scoped SQL `DbConnection` stays disposed by both the
  session and the DI scope — `DbConnection.Dispose` is contractually idempotent, and removing the
  session's disposal would make the release chain depend on scope-disposal ordering.

## Stage B2 — Bounded foundation migrations

- [x] B2.1. F18: `RabbitMQ.Client` 6.8.1 → 7.2.2. Connection, channel, publish, consume, queue
  declare/bind/delete and disposal are async; `DispatchConsumersAsync` no longer exists (F15's
  flag finding retires with it). The delivery is converted inside the consumer handler, because 7.x
  only keeps the body buffer valid while the handler runs — holding `BasicDeliverEventArgs` for a
  later await was the round-trip flake. Verified against a real broker: 9/9 tests, three runs.
- [x] B2.2. F18: `Microsoft.OpenApi` 1.6.31 → 3.10.2 (the current major; the 2.x line was already
  superseded). JSON and YAML both read (`Microsoft.OpenApi.YamlReader` registered), 3.1 documents
  supported, the dead route-matcher condition removed, and the missing direct `ProtoTest.Core`
  reference added (C3). `OpenApiSchemaReference.Target` replaces the manual components lookup.

## Stage C — Internal consolidation (one pass per subsystem)

- [x] C1. F7: the endpoint and base-address markers are one `ProtoHttpClientEntry` per client; the
  resolver reads one record. `ProtoApplicationTarget` stays public for collectors.
- [x] C2. F2: the initializer hook memoizes an unscoped provider per test, so it runs once and the
  later chains reuse its client; the ASP.NET initializer is idempotent per context.
- [x] C3. F8: descendant failures mark their ancestors as entries complete, so `ResolveOutcome`
  no longer scans; the wire groups timeline events by parent once.
- [x] C4. F4: artifact capture is a teardown step: its failure is a finding and fails the teardown
  operation, but it never replaces the result the adapter reported. A passing test whose capture
  fails records `Partial` with no error, matching a failing teardown hook.
- [x] C5. F11: the `ProtoHost(IServiceProvider)` constructor is internal; `ProtoHostBuilder` is the
  only composition path. Changelog Breaking entry added.
- [x] C6. F6: `ProtoFlow` gained `ProtoStepDescriptor`, lost the unused retry/timeout options,
  migrated SQL's release steps (the rollback is now the step's own operation), and **the lifecycle
  teardown is a descriptor flow**. Publishing/disposal run in a second flow so attachments added
  by teardown hooks are still published. Checkpoint met: `TeardownAsync` reads as a step list and
  `TraceCleanupAsync` is gone.

## Stage D — Boundary restoration

- [x] D1. F1: `WebPagePath`/`WebPageInventory`/`WebPageConfig` live in the new, small
  `ProtoTest.Web.Pages` package (public, Core-only dependency); both `ProtoTest.Web` and
  `ProtoTest.AspNetCore` reference it, so AspNetCore does not pull the session/page/assertion
  surface for three helpers. Core's grants to Web and AspNetCore are removed; the shared
  `web.page.available` kind and `web.page.source` key are constants there. Core no longer contains
  web-route semantics.
- [x] D2. F1: `ProtoDocumentSource` keeps only "multi-line or JSON object/array is inline"; GraphQL
  and OpenApi supply their own document prefixes. **Decision:** the stack-frame prefix list stays -
  it names third-party libraries (Npgsql, RabbitMQ, Docker, Grpc, OpenQA), not ProtoTest packages,
  which the `ProtoTest.Framework` marker already skips; removing it could silently degrade recorded
  source locations for no layering gain.

## Stage E — Decisions

- [x] E1. F12: `IProtoClientInitializer.TryInitializeAsync` lost the never-delivered cancellation
  token; the interface documents that setup is not cancellable and that a run-scoped hook is the
  cancellable extension point. `ProtoDocumentSource` stays synchronous by design: its consumers
  construct synchronously, and an async overload would be speculative surface.
- [x] E2. F16: sink registration keeps its current semantics (one registration per sink type, direct
  DI registrations wrapped once). It is pinned by `ReportSinkTests`; the simplification would trade
  a supported composition for fewer lines.
- [x] E3. F21: no run-output channel is added. Artifacts are named by configuration and the trace
  path is documented; a console line would need a logging abstraction Core deliberately does not have.
- [x] E4. Per-test service substitution: not built. The test scope already exists; the seam should be
  designed with the first integration that needs it, not speculatively.
- [x] E5. F14 fixed (both adapters key the scope by the test; the NUnit parallel-case test proved the
  defect). F19 pinned (one record per retry attempt). F20 closed: source inspection shows the
  adapter neither wraps nor rethrows, so the duplicated stack trace is NUnit's own reporting; no
  change without a reproduced case.

## Progress

| Stage | Status | Notes |
| --- | --- | --- |
| A - Characterization tests | Done | A1–A8; A9 closed as no source cause |
| B - Localized fixes | Done | B1–B7; SQL double disposal recorded as a decision |
| B2 - Foundation migrations | Done | OpenApi 1.6.31 → 3.10.2 (JSON+YAML); RabbitMQ.Client 6.8.1 → 7.2.2 verified against a real broker (9/9, three runs) |
| C - Internal consolidation | Done | C1–C6; F7 consolidated, F4 single-sourced, F6's lifecycle pilot landed |
| D - Boundary restoration | Done | Web semantics out of Core; document vocabulary with the formats |
| E - Decisions | Done | E1–E5 recorded |

## Decisions taken

- **SQL connection double disposal** (Stage B7): accepted. `DbConnection.Dispose` is idempotent and
  the session's explicit disposal keeps the release chain independent of scope-disposal ordering.
- **Container restartability** (B3): containers are restartable and the connection string is cleared
  on release; the host re-arms each infrastructure piece it starts again, so a resource the retry
  never restarts is not released twice. Docs (`infrastructure.md`) were updated to match.
- **`ProtoFlow`** (C6): the primitive keeps the descriptor and loses retry/timeout; the lifecycle
  teardown and SQL release are its two consumers, which is what makes the option surface worth
  deleting rather than extending.
- **`ProtoHost(IServiceProvider)`** (C5): internalized; a host assembled without the builder used to
  lose findings and resource reports silently.
- **C1/F7 deferred**: the five marker records still exist; consolidating them touches every
  HTTP-based protocol's registration and deserves its own change with the registration tests.
- **C4/F4**: done - artifact capture is a teardown step; the adapter's result stays authoritative.
- **RabbitMQ 7.x** (B2.1): done and verified against a real broker. The adapter is async-only, and
  the tap queues the converted `ProtoMessage` rather than the delivery event args, whose body buffer
  is only valid during the handler.

## Stop criteria

- No user-visible artifact contains a compiler-generated type name; redaction is proven by an
  adversarial suite across trace, report, attachment and malformed-input axes.
- Two protocols with one client name compose without a registry error; one registration record per
  client.
- One outcome per test, never contradicted by another field; retry semantics documented and tested.
- Run-resource lifetime has one contract honored by Core and every built-in resource.
- Core's surface contains no protocol/web vocabulary; no public type whose only consumer uses none
  of its features.
- Trace size, export time and peak memory measured at 1–2k tests with the quadratic scans gone.
- Every remaining finding fixed or carrying a recorded decision.

## Things NOT to change

The run/test state machines; `ProtoTestScope`'s teardown swallow; `ProtoExecutionContext` as one
façade; the ambient `Proto.Context` model; attributes as the lifecycle mechanism; per-adapter
`MapResult`; `ProtoCoverageCollector`; `ProtoPolling`; `ProtoAssertion`; the trace wire and viewer
contract; `AdapterContract` and the shared test doubles; `ProtoContainerResource`'s start-task
pattern; the pure static helpers.

## New problem classes discovered

1. Redaction and evidence policy implemented per output axis instead of at the evidence boundary.
2. Serialization fallbacks that expose CLR internals instead of degrading per element.
3. Format/version vocabulary drift: the wire is versioned and tested, the snapshot model is stale.
4. Extension-point contract asymmetry across one lifecycle phase (cancellation, failure semantics).
5. Configuration facts modeled as ad-hoc DI markers with divergent read rules.
6. Contradictory lifecycle contracts pinned only as tests in different packages.
7. Outcome derivation recomputed at three layers instead of annotated.
8. Foundation-major debt dictating wrapper interfaces.
9. No run-output channel, so run artifacts are discoverable only by convention.
10. A shared primitive with the wrong feature surface: retry (unused) instead of trace identity.
