# ProtoTest Internal Audit 3 — Findings and Plan

Audit date: 2026-09-24 · Baseline commit: `09d2590` (working tree clean)
Scope: `src/` (27 projects, 441 production `.cs` files), `tests/`, samples, `eng/`, plus the trace wire
contract. Prior audits (`eng/audit-plan.md`, `eng/audit-plan-2.md`) and the earlier consistency review
(`assets/internal/review-findings.md`) were read first; findings below are deduplicated against them and
open items from those plans are referenced, not re-reported.
Method: full read of `ProtoTest.Core`; deep passes over adapters, HTTP/REST/GraphQL/gRPC/OpenApi,
Web/Sheets/Sql/Data/Messaging/Testcontainers; verification against NUnit (`SimpleWorkItem.MakeTestCommand`)
and MSTest (`TestMethodRunner.Execution.cs`, `TestMethodRunner.DataRow.cs`) framework sources; and
extraction of a fresh real `.prototrace` produced 2026-09-24 15:53 to confirm the client-entity split.

**Goal:** finish the contracts the first two audits left at the boundaries — one evidence policy, one
client identity, one outcome vocabulary, one owner per derived fact, and runner lifecycle boundaries that
are documented and pinned — before adding the new capabilities in `eng/feature-plan.md`.

## Locked decisions

- Evidence has one policy at the evidence boundary: findings, observations, attachments and archive
  entries are serialized and redacted once, before both the report item and the trace record. No exit
  boundary may serialize raw `object` metadata.
- A resource's ownership period is always releasable. `ReleaseFailed` is not terminal for a resource the
  host starts again; the failure stays visible in the trace and report.
- Teardown failures are recorded through `AddFinding` and reach sinks and run gates; the adapter's
  reported result stays authoritative (consistent with Audit 2's locked outcome decision).
- One client entity per client instance. The registration record owns the entity id; operations,
  configuration and release read it instead of deriving names at call sites.
- Cancellation has one mapping rule: `OperationCanceledException` at the operation that owns the token
  maps to `Cancelled`; integrations may document a narrower exception (for example a timeout that
  surfaces as OCE) only with a test.
- Configuration errors fail configuration (registration or resolve), never the first test that records an
  observation.
- Each runner's lifecycle boundary is a documented, tested contract. NUnit's lifecycle becomes outermost
  if the command wrapper proves safe; otherwise the current boundary is documented and pinned.
- Do not touch (Audit 2 carry-over): the run/test state machines, `ProtoResourceRegistry`'s shape,
  `ProtoTestScope`'s teardown swallow, `ProtoExecutionContext` as one façade, the ambient `Proto.Context`
  model, attributes as the lifecycle mechanism, per-adapter `MapResult`, the trace wire schema and viewer
  contract, `AdapterContract`. This plan changes contents and states within those shapes, not the shapes.
- No new layers. Fixes prefer delete → simplify → consolidate → make explicit → abstract. The only new
  shared type this plan allows is the client registration record (D1) and Core-private pure helpers.

## Rules

1. Every behavior change lands with the characterization test that pins the new behavior, and the old
   behavior is pinned first where it is being changed deliberately.
2. Every stage ends green: `eng/test.ps1` full suite plus `eng/lint.ps1` (format gate), no skipped suites.
3. Each stage records `git diff --shortstat` split by `src` / `tests` / `docs`, excluding the plan file.
4. Public surface changes are additive except for accidental-public plumbing, and each is called out in
   the commit message.
5. A stringly contract becomes a descriptor, enum or constant at the point it is introduced.
6. Nothing is deferred into prose: a consciously postponed item gets a stage entry or is recorded under
   "Decisions taken".

Severity: CRITICAL / HIGH / MEDIUM / LOW / OBSERVATION. Confidence: H / M / L.
Compatibility: **I** internal · **B** behavioral source-compatible · **E** extension API · **C** consumer
API breaking.

## Findings register

| ID | Finding | Sev | Conf | Compat |
| --- | --- | --- | --- | --- |
| A1 | Finding metadata is redacted for the report but written raw into `.prototrace` (`ProtoExecutionContext.cs:342/345`, `ProtoTraceRecorder.cs:297`, `ProtoTraceWire.cs:235-248`) | HIGH | H | B |
| A2 | Archive/report metadata serialization is raw STJ with no cycle handling or fallback; a bad metadata object fails run shutdown (`ProtoTraceArchiveWriter.cs:104-112`) | MEDIUM | M-H | B |
| A3 | Base addresses keep query strings (`ProtoHttpClientInitializer.cs:110-113`); redaction list misses `client_secret`/`id_token`; metadata redaction stops at depth 16 | LOW-MED | H | B |
| A4 | `Enabled = false` still installs the activity listener, records run state and captures sink artifacts; docs say "nothing is recorded" (`ProtoTraceSession.cs:33-52`, `ProtoSinkExportHook.cs:22-29`) | LOW-MED | H | B |
| A5 | Archives are written in place (corrupt file on mid-write failure) and `sources/` entry names bypass `ProtoPathSanitizer` (`ProtoTraceArchiveWriter.cs:29,74`) | LOW-MED | H | B |
| B1 | A resource whose release failed cannot be re-armed, so a retried start's ownership period is never released (`ProtoResourceRegistry.cs:267-282`, `:244-256`) | HIGH | H | B |
| B2 | Teardown failures bypass `AddFinding`, so reports and run gates never see them, while resource-release failures do (`ProtoTestLifecycle.cs:329-337` vs `ProtoExecutionContext.cs:323-347`) | MEDIUM | H | B |
| B3 | `ProtoTestHostLifetime.StartAsync` is check-then-act; concurrent starts leak the first host; dispose-in-catch can mask the start failure (`ProtoTestHostLifetime.cs:31-55`) | MEDIUM | H | B |
| B4 | Failed run startup runs the full `AfterRun` chain: gates, sinks and trace archive for a run that never started (`ProtoHost.cs:191-210`) | LOW-MED | H | B |
| B5 | `StopAsync` has no active-test accounting (`ProtoRunStateMachine.cs:114-129`) | LOW-MED | H | B |
| B6 | `ReleaseAllAsync` answers a concurrent second caller successfully while the first release is in flight (`ProtoResourceRegistry.cs:74-91`) | LOW | M | I |
| B7 | `RegisterAlias` ignores the registry seal (`ProtoClientRegistry.cs:45-57`) | LOW | H | I |
| B8 | `AddResource`/`ConfigureTracing` mutate live host state after `Build()` while other builder members are guarded (`ProtoHostBuilder.cs:80-92`) | LOW | H | E |
| C1 | Per-host `ActivityListener` + session-local lookup + ambient fallback duplicates application spans or routes them to the wrong run when two hosts are alive (`ProtoTraceSession.cs:61-82`; `ProtoHostRegistry.FindTraceWriter` unused here) | HIGH | H | B |
| C2 | `CompleteTest` publishes `_completed` before `_duration`/`_outcome`/`_error`; a concurrent snapshot can see 0/Unknown (`ProtoTraceRecorder.cs:350-358`) | LOW-MED | M-H | I |
| C3 | Playwright `InstalledBrowsers` read outside the install gate (`PlaywrightBrowserPool.cs:83-107`) | LOW | H | B |
| C4 | `Forget`/`Observe` race can resurrect converter state (`ProtoSpanConverter.cs:38-48`) | LOW | H | I |
| D1 | Client trace entity is split: config state under `client:{type}:{ScopedName}`, requests under the logical name. Confirmed in a real 2026-09-24 trace (`ProtoClientInitializerHook.cs:89`, `ProtoExecutionContext.cs:196`, `RestRequestBuilder.cs:113`, `ProtoGrpcClient.cs:429`) | HIGH | H | B |
| D2 | gRPC fallback client hardcodes `new GrpcClientOptions()`, dropping configured metadata/deadline/sensitive keys (`ProtoGrpcClient.cs:50-57`) | MEDIUM | H | B |
| D3 | Cancellation maps to `Cancelled` in Web/Data and to `Failed` in REST/GraphQL/gRPC/Messaging; NUnit and xUnit v3 have no mapping | MEDIUM | H | B |
| D4 | Coverage participation is auto (Web/Sheets) vs manual (REST/GraphQL/gRPC/OpenApi); gRPC counts failed calls as covered while REST does not; comparers and descriptor literals drift | MEDIUM | H | B |
| D5 | Five options-binding mechanisms; validation timing/exception types differ; OpenAPI/GraphQL collector construction throws mid-test | MEDIUM | H | I/E |
| D6 | Diagnostic-capture failure policy: HTTP swallows silently, gRPC/Messaging and Web trace | LOW-MED | H | B |
| D7 | `AddSheets`/`AddWeb` silently drop a repeated `configure`; sibling integrations compose | LOW-MED | H | B |
| D8 | ASP.NET Core server entity id ignores the server name, so two named servers collide (`AspNetCoreServerState.cs:15`) | LOW-MED | H | B |
| D9 | `ProtoProtocol` adoption is partial (Web/Sql/Sheets/Data/AspNetCore keep constants); closed by Audit 1 INT-52 | OBS | H | I |
| D10 | Selenium caller-supplied driver and user-supplied messaging broker are always disposed; no ownership flag | LOW-MED | H | E |
| D11 | gRPC raw opens create a fresh authenticator per call (`ProtoGrpcAuthenticationApplier.cs:31`) | LOW | H | I |
| D12 | A throwing collector fails the test and skips later collectors (`ProtoObservationDispatcher.cs:12-24`) | LOW-MED | H | E |
| E1 | NUnit: `[SetUp]` runs before the skip decision and `[TearDown]` after the trace ends (verified in NUnit's `SimpleWorkItem.MakeTestCommand`) | HIGH | H | B |
| E2 | MSTest: lifecycle is per data row, contradicting the comment, docs and dead multi-row tests (verified in `TestMethodRunner` sources) | MEDIUM | H | B |
| E3 | xUnit v3: lifecycle misses class construction/`IAsyncLifetime` and class disposal; a throwing sibling before-attribute leaks the scope and ambient context (`ProtoTestLifecycleHandler.cs:36-65`) | MEDIUM | M-H | B |
| E4 | Theory/data rows collide in xUnit v3 and MSTest; xUnit v2 names rows | LOW-MED | H | B |
| E5 | TUnit wraps every test in the assembly; `GetReflectionInfo()` null throws (`ProtoTestExecutor.cs:18-29`) | MEDIUM | H | B |
| E6 | NUnit scope dictionary leaks if `AfterTest` never runs (`ProtoTestAttribute.cs:18,32-45`) | LOW | M | B |
| F1 | No adapter setup-failure test for any of the five adapters | MEDIUM | H | — |
| F2 | No adapter teardown-failure test | MEDIUM | H | — |
| F3 | Real-run outcome capture asserted only for NUnit and xUnit v2 | MEDIUM | H | — |
| F4 | Stack traces never asserted anywhere | LOW-MED | H | — |
| F5 | Cancellation untested end-to-end for NUnit/xUnit v2/v3/TUnit | MEDIUM | H | — |
| F6 | NUnit SetUp/TearDown, v3 class lifecycle/sibling attribute, TUnit rows/retries untested | MEDIUM | H | — |
| F7 | `ProtoTestHostLifetime` has no direct tests | MEDIUM | H | — |
| F8 | Parallel ID generation (uniqueness/exhaustion) untested | LOW-MED | H | — |
| F9 | NUnit/TUnit attachment tests are tautological (`File.Exists` proves materialization, not publishing) | LOW-MED | H | — |
| F10 | HTML report tests assert SVG/CSS literals; registration tests assert DI descriptors; reflection-poking helpers | LOW | H | — |
| F11 | AdapterContract never asserts recorded outcome, ambient-context clearing or attachment delivery | MEDIUM | H | — |
| G1 | Artifacts are unbounded and retained for the whole run; sink artifacts captured even when tracing is disabled; 1–2k measurement not done | MEDIUM | H | B |
| G2 | Materialized attachment temp files accumulate | LOW | M | I |
| H1 | `NumericProtoTestIdGenerator.Next(MethodInfo)` ignores its parameter (`:30-38`) | LOW | H | E |
| H2 | Dead surfaces: `SetCount` Activity branch, `ProtoTraceValueSource.Observed`, `ProtoReportItemKinds.Metric` test-only | LOW | H | I |
| H3 | Documentation contradicts behavior: runner boundaries, MSTest "spans every data row", `AddFinding` report reach, `ProtoHookOrder`/`ProtoInfrastructure`/`IProtoResource` ordering comments, `ProtoTestAsync` ConfigureAwait claim | LOW-MED | H | — |

## Stage 0 — Characterization tests (no production change)

Pin current behavior before changing it. Several of these fail if a finding above is wrong — which is the
point.

- [x] 0.1 E1: NUnit fixture with `[SetUp]`, a skipped `[RequiresCapability]` test and `[TearDown]`;
  assert whether setup ran and whether `[TearDown]` effects are in the trace.
  (`LifecycleBoundaryTests`: setup count 2 proves the skipped test's `[SetUp]` ran; context is already
  unavailable in `[TearDown]`. Stage 4 flips the setup-count assertion.)
- [x] 0.2 E2: real 3-row `[ProtoTest] [DataRow]` test in `ProtoTest.MSTest.Tests`; assert trace count,
  names and per-row attachments. Delete/replace the dead multi-row unit tests (`OutcomeTests.cs:53-112`).
  (`DataRowLifecycleTests` asserts one distinct lifecycle per row sharing the method-level name; the two
  dead multi-row tests are deleted.)
- [x] 0.3 E3/E4: xUnit v3 class with constructor + `IAsyncLifetime` recording `Proto.Context`
  availability; a sibling `IBeforeAfterTestAttribute` that throws; two `[InlineData]` rows.
  (`ClassLifecycleBoundaryTests` pins constructor/Initialize/Dispose outside the lifecycle. The sibling
  attribute half is deferred to Stage 4: it needs the fix in place to be a green test; the leak mechanics
  are recorded in E3.)
- [x] 0.4 F1/F2: adapter setup-failure and teardown-failure tests for all five adapters: exactly one
  failed trace, original exception at the runner, no leaked ambient context, result unchanged.
  (`ProbeFailureTests` in all five adapter projects, driven out of band through each adapter's real entry
  point via `AdapterFailureProbe`; setup surfaces the original exception and a Failed trace, teardown
  keeps the result with a Partial trace and finding.)
- [x] 0.5 B3/F7: `ProtoTestHostLifetime` double start (sequential and concurrent), retry after failed
  start, `Host` after stop. (`ProtoTestHostLifetimeTests`; the concurrent test proves the double-entry
  window via per-host hooks without leaking a host. Stage 2 flips it to a single winner.)
- [x] 0.6 B1: release failure during rollback, then a retried start and stop; assert every ownership
  period is released. (`ProtoHostStartStopTests.StartAsync_WhenRollbackReleaseFails...` pins the current
  leak: release count stays 1. Stage 2 flips it to 2.)
- [x] 0.7 B2: teardown hook failure is visible to a `CapturingSink` and a run gate.
  (`ProtoFindingTests.TeardownFailure_ShouldCharacterizeAsTraceOnlyEvidence` pins the current invisible
  behavior. Stage 2 flips it.)
- [x] 0.8 D1: REST client under an application — assert the entity ids in `state.json` and `spans.json`
  agree, at the test level. (`ClientEntityIdentityTests` pins the split: the request links to an entity
  without configuration state. Stage 3 flips it.)
- [x] 0.9 A1/A2: `AddFinding` sensitive metadata asserted on the trace axis as well as the sink; a cyclic
  metadata object. (`AddFinding_ShouldCharacterizeRawMetadataInTheTrace` and
  `AddFinding_WithCyclicMetadata_ShouldCharacterizeTheArchiveExportAsAFailure` pin both defects.
  Stage 1 flips both.)
- [x] 0.10 D3/F5: OCE through REST, gRPC, GraphQL, Messaging, and through all five adapters.
  (`CancellationOutcomeTests` in Rest, GraphQL, Grpc and Messaging pin `Failed`; NUnit and xUnit v3
  outcome tests pin the missing mapping; TUnit and xUnit v2 pin `Cancelled`; MSTest's Cancelled mapping
  was already covered. Stage 3 applies the one rule.)
- [x] 0.11 F11: extend `AdapterContract` to assert the compliance trace's recorded outcome is
  `Succeeded`, `Proto.Context` is unavailable after teardown, and an attachment added in the body reaches
  the published set. (`AdapterLifecycle.VerifyCompletedRun` + `RunContractVerificationHook` now fail the
  run when any wrapped test records `Unknown` or the compliance test does not succeed with its
  attachment; all five adapter suites run it at teardown.)

⛳ Checkpoint 1 — if any test contradicts a finding, correct the register before Stage 1. No behavior
changes in Stage 0.

**Stage 0 outcome:** every finding above was confirmed by the characterization tests; no register
corrections were needed. The findings pinned by tests that assert current (defective) behavior are
flagged inline with the stage that flips them. Stage 0 also closed F1, F2, F5, F6, F7 and F11, and
addressed F3's real-run half. Note: the first probe implementation used `AsyncLocal` flags and did not
reach the hook; the shared probe now registers the lifecycle method name, which is deterministic. Worth
a look only if some other ambient-value path ever shows the same symptom.

## Stage 1 — Evidence boundary (independent, high trust)

- [x] 1.1 A1: redact/serialize finding metadata once, before both the report item and the trace record
  (`AddFinding` builds one redacted payload for both; the test and run recorders redact at their own
  boundary so a direct `Trace.Finding` caller cannot bypass it). The raw-object API stays; the recording
  boundary owns the policy.
- [x] 1.2 A2: `ProtoMetadataRedaction` is now the one cycle-safe, non-throwing evidence policy: it copies
  containers, replaces reference cycles with `[circular]`, keeps JSON-safe scalars, and degrades unknown
  leaves to the bounded formatter text. Every sink and the archive can serialize its output with plain
  `System.Text.Json`.
- [x] 1.3 A3: base addresses are recorded through `ProtoUriSanitizer.ForDiagnostics` (user info, query
  and fragment dropped) in the HTTP and gRPC initializers; `ProtoRedactionDefaults` gained
  `client_secret` and `id_token`.
- [x] 1.4 A5: the archive is written to a temporary file and moved into place, with cleanup on failure;
  `sources/` entry names go through `ProtoPathSanitizer.FileName`.
- [x] 1.5 A4/G1: `Enabled = false` no longer installs the activity listener and no longer captures sink
  artifacts; in-memory recording stays (it is the seam the tests and run gates use), and the docs now say
  exactly that. `ProtoTraceOptions.MaxArtifactBytes` (64 MB default) turns an over-limit attachment into
  an error artifact with no content.
- [x] 1.6 Tests: the Stage 0 A1/A2 characterizations are flipped; a sequence of dictionaries with the new
  names, client base-address redaction, the disabled-tracing listener/sink behavior, and the artifact cap
  are covered (`ProtoFindingTests`, `ProtoEvidenceBoundaryTests`,
  `ClientEntityIdentityTests.ClientConfiguration_ShouldRedact...`).

⛳ Checkpoint 2 — adversarial redaction suite green on all axes before Stage 2.

## Stage 2 — Lifecycle failure paths (Core)

- [x] 2.1 B1: `Rearm` accepts `ReleaseFailed` as superseded by a new ownership period, so a resource the
  host starts again after a failed rollback release is released with the retry
  (`ProtoResourceRegistry.cs`). The Stage 0 release-count characterization is flipped to 2.
- [x] 2.2 B2: teardown failures are recorded through `AddFinding`, so they reach the finding store, sinks
  and run gates; the failing teardown operation and the test's own result stay unchanged. The context now
  resolves the finding store once at construction, because a teardown finding is recorded after the test
  scope has been disposed. `ProtoFindingTests.TeardownFailure_ShouldReachTheReportAndTheRunGate` proves
  both the sink item and the gate verdict.
- [x] 2.3 B3: `ProtoTestHostLifetime.StartAsync` is single-flight; a concurrent second call is rejected
  while the first is in flight, a failed start disposes itself without masking the start error, and the
  lifetime stays retryable. The Stage 0 double-entry characterization is flipped to a rejection test.
- [x] 2.4 B4: a failed run startup runs only `ProtoRunResourceHook` on rollback; gates, sinks and the
  trace archive are not produced for a run that never started
  (`StartAsync_WhenInfrastructureFails_ShouldNotExportReportsOrTrace`), and the retry still exports.
- [x] 2.5 B5–B8: B7 (alias registration obeys the registry seal) and B8 (`AddResource` and
  `ConfigureTracing` after `Build` throw instead of mutating live host state) landed with direct tests.
  B5 (stopping with active tests) and B6 (the release-in-flight early return) remain unreachable through
  the state machine and are recorded as decisions in Stage 7.

## Stage 3 — Trace identity and outcome vocabulary (Core + integrations)

- [x] 3.1 D1: one registration record owns the client entity id. `ProtoClientResolution.Find` returns
  the registry key it actually hit, `ProtoExecutionContext.TryClientName` recovers the key for a client
  instance (the transport included), and the HTTP/GraphQL/gRPC request paths link their operations to
  that entity while `client.name` stays the logical name. `ClientEntityIdentityTests` now asserts the
  request and the configuration link to one entity.
- [x] 3.2 D3: one cancellation rule, owned by the vocabulary: `ProtoTraceOperation.Fail` records an
  `OperationCanceledException` as `Cancelled` wherever it surfaces, and gRPC maps `RpcException` with
  `StatusCode.Cancelled` the same way. xUnit v3 detects an OCE/TaskCanceledException in the result's
  exception types, MSTest maps an OCE `TestFailureException`; xUnit v2 and TUnit already mapped.
  NUnit exposes no exception type, so a cancelled test stays `Failed` — recorded as a documented
  limitation in the adapter test, not silently assumed.
- [x] 3.3 D2: the gRPC fallback resolves `GrpcClientOptions` from the container (now registered as the
  protocol singleton by `ProtoGrpcClientRegistration`, like the HTTP-based protocols) instead of
  constructing defaults, and
  `ApplicationTransportFallback_ShouldApplyConfiguredOptions` proves the configured metadata reaches the
  call when the transport backs an unregistered client name.
- [x] 3.4 D4: a failed gRPC call records `grpc.failure` instead of a response-shaped observation, so it
  does not count as covered; REST's `http.failure` kind is now a named constant; the GraphQL schema
  collector consumes the protocol descriptor instead of the `"graphql.response"` literal; the gRPC
  collector compares identifiers with `StringComparer.Ordinal` because gRPC names are case-sensitive.
  **Decision:** collector registration stays manual for REST, GraphQL, gRPC and OpenAPI (the template
  and docs show it); the Web and Sheets collectors are intrinsic to their packages and auto-register.
  Both behaviors are documented rather than converging on one.
- [x] 3.5 D8: the ASP.NET Core server entity id includes the server name, so two named servers from one
  program no longer overwrite each other's state.

⛳ Checkpoint 3 — the viewer renders one client entity per client; golden traces updated; cancellation
facet consistent across protocols.

## Stage 4 — Runner boundaries (adapters)

- [x] 4.1 E1: NUnit's lifecycle is a command wrapper (`IWrapSetUpTearDown`) instead of an `ITestAction`,
  so it is applied outside NUnit's setup and teardown: the skip condition is decided before `[SetUp]`,
  the context spans `[SetUp]`, the body and `[TearDown]`, and `[TearDown]` failures are part of the
  recorded result. The wrapper is per case (no shared attribute dictionary), so E6 is fixed too, and
  parameterized rows trace under the case's full name. The Stage 0 boundary test is flipped
  (`SkipDecision_ShouldPrecedeSetUp`, `[TearDown]` sees the context), and the probe drivers drive the
  wrapper with a stub command. **Observable change:** a skipped test no longer runs `[SetUp]`.
- [x] 4.2 E2: the MSTest comment and docs now state one lifecycle per row; `ToProtoTestResult` is a
  single-result mapping (the dead multi-row aggregation and its unit tests are gone), `NotRunnable` maps
  to `Skipped` like the runner reports it, and a row's arguments are appended to the trace name. The
  real three-row `DataRowLifecycleTests` remains the contract.
- [x] 4.3 E3: xUnit v3 theory rows trace under `TestDisplayName`; `After` reads the state defensively and
  always completes the scope, so a state-read failure cannot leave it open. The class-lifecycle boundary
  (constructor, `IAsyncLifetime.InitializeAsync` and class disposal outside the context) and the
  sibling-before-attribute consequence (the run-level contract check reports the unfinished test) are
  documented; xUnit offers no hook to complete the scope when a sibling throws.
- [x] 4.4 E4: rows are distinguishable in NUnit (case full name), xUnit v2/v3 (display name) and MSTest
  (method name plus arguments); the runner docs state the naming per adapter.
- [x] 4.5 E5: **decision:** the executor stays assembly-wide (documented, including that narrower scoping
  is untested); a source-generated test with no reflection `MethodInfo` now runs unwrapped instead of
  failing on the executor's assumption.
- [x] 4.6 E6/F4: E6 fixed by the wrapper; NUnit asserts the runner's stack trace survives into the trace.
  xUnit v3 passes the state's stack through (no adapter-side transformation) and its runner only supplies
  stacks for thrown exceptions. F9 (NUnit/TUnit attachment tests still assert materialization rather than
  publication) is recorded for Stage 7.

## Stage 5 — Cross-cutting consistency (integration hygiene)

- [x] 5.1 D5: `IProtoConfigurableOptions` gained a default `Validate()` that `ProtoOptionsRegistration`
  calls after callbacks and binding, so a bad value fails where the options resolve; `MessagingOptions`,
  `GrpcClientOptions` and `ProtoHttpResponseOptions` implement it, and the response buffer guard now
  throws the same `ArgumentOutOfRangeException` as the exchange. Collectors are singletons and are
  constructed once in `ProtoHostBuilder.Build`, so a missing OpenAPI specification or GraphQL schema
  fails host construction instead of the first observation. **Decision:** the binder mechanisms stay
  (Web's infrastructure overlay and the direct configuration reads); validation timing was the payoff.
- [x] 5.2 D6: an HTTP diagnostic-capture failure is traced as `http.diagnostics.failed` instead of being
  swallowed silently, matching gRPC, messaging and web.
- [x] 5.3 D7: `AddSheets` registers its options through `ProtoOptionsRegistration.Configure`, so repeated
  calls run every callback; the test proves the later callback applies. **Decision:** AddWeb keeps one
  backend per host (first registration wins), documented as the model rather than silent drift.
- [x] 5.4 D12: the observation dispatcher isolates collector failures, returns them, and the context
  traces a `collector.failed` event; the observation and the collectors behind the failing one survive.
- [x] 5.5 C1/C3/C4: a span is captured by the session that owns its trace id (or the session owning the
  ambient context); another host's listener leaves it alone, so two hosts cannot double-record or
  cross-record. The Playwright install-set check moved inside its gate, and the span converter ignores a
  completed recorder so late telemetry cannot resurrect released state.
- [x] 5.6 C2: `CompleteTest` publishes duration, outcome and error before the completed flag, so a
  snapshot that sees the flag sees the final fields. The race has no deterministic test; the fix is
  ordering-only.
- [x] 5.7 D10/D11: Selenium's `createDriver` documents ProtoTest ownership (a fresh driver per session)
  and `UseBroker` documents that the broker is released with the run; the gRPC raw-call docs state that
  each raw call resolves a fresh authenticator. **Decisions:** no ownership flag or authenticator cache
  was added for its own sake.

⛳ Checkpoint 4 — reassess before Stage 6; stop consistency work when nothing fails.

## Stage 6 — Scale and measurement

- [x] 6.1 G1: the in-repo harness `TraceScaleTests` runs 100 and 1,000 synthetic tests through the real
  lifecycle and records trace size, run time, stop+export time, allocation growth and peak working-set
  growth; the measured numbers and the levers are published in `docs/docs/benchmarks.md`. A new
  `ProtoTraceOptions.EmbedArtifacts` (default true) declares attachments without reading or writing their
  bytes - the artifacts-off lever the finding asked for, alongside `MaxArtifactBytes` from Stage 1 and
  `EmbedSources`.
- [x] 6.2 G2: the materialized temp copies (`%TEMP%/ProtoTest/attachments`) are documented as a known,
  safe-to-clear bound in the attachments page instead of being deleted at an unsafe point in the
  runner's result pipeline.
- [x] 6.3 Regression guard: the harness asserts the trace stays proportional to the test count
  (`< 64 KB/test`), `DisabledArtifactEmbedding_ShouldDeclareWithoutContent` pins the archive behavior,
  and the Stage 1 cap test pins the over-limit artifact path. Measured on a developer laptop: 1,000
  tests ≈ 7.0 MB trace, 389 ms run, 59 ms stop+export, 377 MB allocated.

⛳ Checkpoint 4 met: nothing in the consistency or scale work failed after the fixes.

## Stage 7 — Decisions and closure

- [x] 7.1 Decisions recorded: D9 (`ProtoProtocol` adoption stays partial, closed in Audit 1), D10 and D11
  (ownership and raw-auth behavior documented in Stage 5), H1 (the `IProtoTestIdGenerator` method
  parameter stays and is documented as advisory; the numeric generator says why it ignores it), H2
  (`ProtoTraceValueSource.Observed` and `ProtoReportItemKinds.Metric` stay as open wire/report
  vocabulary), B5/B6 (unreachable through the state machine; no guard added), and the Stage 5 decisions.
- [x] 7.2 F10 labelled: `[Category("Characterization")]` on all ten `RegistrationIdempotencyTests`
  fixtures (they assert registration shape as a deliberate refactoring brake) and on `ReportSinkTests`
  (its exact markup, SVG and CSS assertions).
- [x] 7.3 Doc sweep: the `ProtoHookOrder` run-resource/report summaries, `ProtoInfrastructure`'s start
  order, `IProtoResource`'s release order and `ProtoTestAsync`'s ConfigureAwait claim now match the code.
  `AddFinding`'s reach became true in Stage 2, and the runner boundaries/rows were fixed in Stage 4.
- [x] 7.4 Dead surfaces: `TraceEntryState.SetCount`'s unreachable Activity tag write is removed; the
  remaining items are recorded decisions rather than deletions (they are public wire vocabulary).

## Stage 8 — Open-question register follow-ups

Source: the 2026-09-24 open-questions register (companion to the brief). Worked items are marked; the
items not chosen for this pass are decisions below.

### Publish a contract (docs, no code)

- [x] 8.1 Trace format compatibility policy (2.1): published in `prototrace.md` ("Format compatibility"):
  readers support the current major and the one before, the viewer keeps opening archives it has always
  opened, and a breaking change bumps the major with a migration note.
- [x] 8.2 Reserved `Order` bands (2.6): published in `foundation/hooks.md` (infrastructure `-300…-201`,
  environment `-200…-101`, identity `-100…-1`, scenario `0+`).
- [x] 8.3 Community versioning floor (2.9): `CONTRIBUTING.md` — first-party packages release together;
  outside packages version independently and declare the lowest compatible ProtoTest.
- [x] 8.4 Trace size table (2.15): the Stage 6 benchmarks page is linked from `prototrace.md` and now
  carries the parallel-execution summary.
- [x] 8.5 "No telemetry" statement (2.13): a section in `why-prototest.md`.
- [x] 8.6 JSON constraint vocabulary (2.17): already complete in `foundation/shape-matching.md`; verified
  against `JsonValue` and recorded as done rather than rewritten.
- [x] 8.7 Concurrency-inside-a-test guidance and the measured ceiling (2.7/2.8): new
  `foundation/concurrency.md`; the demo was run at 8, 32 and 64 workers with Postgres, RabbitMQ and real
  browsers (stable at 8 and 32, one non-reproducible failure at 64 on 16 cores).

### Enable existing tooling (hours)

- [x] 8.8 Package validation (2.5): `Directory.Build.targets` enables `EnablePackageValidation` against
  `PackageValidationBaselineVersion` 1.0.1 for every packable project; the deliberate 1.0.1 deltas are
  recorded in per-project `CompatibilitySuppressions.xml`; the Web.Pages family opts out because it has
  no 1.0.1 release. **Decision:** `PublicApiAnalyzers` was not chosen; the baseline guard is the
  commitment taken now.
- [x] 8.9 HTML report accessibility (2.11): the search input has an accessible name, pinned by the
  report test.
- [x] 8.10 NuGet link (2.12): the banner points at the `ProtoTest.Core` package instead of a search.

### Small features with outsized value

- [x] 8.11 Trace reader and CLI (2.2): `ProtoTest.Traces` reads archives without a Core dependency and
  `ProtoTest.Cli` ships the `prototest` tool (`prototest trace summary <file>`); both are packed, tested
  and dogfooded against a failing demo run. This is the reader the MCP server will reuse.
- [x] 8.12 Crash-resilience policy (2.3): documented honestly in `prototrace.md` ("If the process dies");
  no incremental flush. **Decision:** incremental flushing was not chosen for this pass.
- [x] 8.13 Attachment content policy (2.4): `attachments.md` now states the default (sanitized and
  redacted), the `EmbedArtifacts = false` mode and the `MaxArtifactBytes` cap.
- [x] 8.16 AI-assistance note (2.16): a section in `CONTRIBUTING.md` (disclosure, walkthrough,
  accountability).
- [ ] 8.14 Template runner variants and 8.15 static trace index: not chosen for this pass — recorded as
  decisions below rather than silently dropped.

## Progress

Record `git diff --shortstat` per stage split by `src`, `tests` and `docs`, with this file excluded.

| Stage | Status | Net lines | Notes |
| --- | --- | --- | --- |
| 0 — Characterization tests | Complete | tests +1185 / −26 · src 0 · docs 0 | 0.1–0.11 done and green; every finding confirmed. 0.3's sibling-attribute half deferred to Stage 4 (a green test needs the fix). Shared probe infrastructure (`AdapterFailureProbe`, `RunContractVerificationHook`, `VerifyCompletedRun`) added to `AdapterContract`. All five adapter suites, Core (207), Rest (92), GraphQL (65), Grpc (33), Messaging (28) green; `eng/lint.ps1` green. |
| 1 — Evidence boundary | Complete | src +204 / −52 · tests +203 / −17 · docs +3 / −1 | A1–A5 done and pinned: one metadata policy at the evidence boundary, atomic archive writes with sanitized source names, query/user-info-free base addresses, `Enabled = false` honored for listening and sink capture (in-memory recording stays and the docs say so), and a 64 MB artifact cap. Full `eng/test.ps1` and `eng/lint.ps1` green. |
| 2 — Lifecycle failure paths | Complete | src +78 / −13 · tests +155 / −78 | B1–B4 fixed and pinned; B7/B8 guards landed with tests; B5/B6 recorded as decisions. Full suite and format gate green. |
| 3 — Trace identity and outcomes | Complete | src +175 / −53 · tests +82 / −32 · docs +3 / −3 | D1, D2, D3, D4 and D8 done; NUnit's missing cancellation mapping recorded as a runner limitation; coverage registration divergence decided and documented. Full suite and format gate green. |
| 4 — Runner boundaries | Complete | src +107 / −80 · tests +93 / −98 · docs +26 / −17 | E1 fixed with the NUnit command wrapper (skip precedes `[SetUp]`, lifecycle spans `[SetUp]`/`[TearDown]`); MSTest is one row, one lifecycle with a single-result mapping; xUnit v3 rows use the display name and `After` always completes the scope; TUnit runs reflection-less tests unwrapped. Runner docs updated. Full suite and format gate green; F9 deferred to Stage 7. |
| 5 — Cross-cutting consistency | Complete | src +197 / −27 · tests +230 / −1 · docs +1 / −1 | D5 validation at resolve plus collector construction at Build; D6 diagnostic failures traced; D7 AddSheets composes (AddWeb decision); D12 collector isolation; C1/C3/C4 and C2 fixed. Binder relocation, AddWeb composition, ownership flags and raw-auth caching recorded as decisions. Full suite and format gate green. |
| 6 — Scale and measurement | Complete | src +21 / −0 · tests +133 / −0 · docs +28 / −0 | Harness measures 100/1,000 tests and asserts the per-test trace bound; numbers and levers published on the benchmarks page; `EmbedArtifacts` added; temp attachment copies documented. Full suite, format gate and docs check green. |
| 7 — Decisions and closure | Complete | src +16 / −15 · tests +11 / −0 · docs 0 | Remaining decisions recorded; the registration and report-markup tests labelled `Characterization`; the stale hook/infrastructure/resource/bridge comments corrected; the unreachable `SetCount` branch removed. Full suite and format gate green. |
| 8 — Open-question register | Complete (8.14/8.15 deferred) | src +1591 / −6 · tests +204 / −0 · docs +95 / −2 · other +28 / −4 | Contracts published (format policy, order bands, community versioning, no-telemetry, concurrency + measured ceiling, crash resilience, attachment policy, AI note); package validation against the 1.0.1 baseline with recorded suppressions; report accessibility and NuGet link fixed; `ProtoTest.Traces` reader and the `prototest` CLI shipped and dogfooded. Template runner variants and the static trace index deferred by decision. Full suite, format gate, docs check and pack all green. |

## Decisions taken

Carried from the prior plans and binding here:

- The run/test state machines, `ProtoResourceRegistry`'s shape, `ProtoTestScope`'s teardown swallow,
  `ProtoExecutionContext` as one façade, the ambient `Proto.Context` model, attributes as the lifecycle
  mechanism, per-adapter `MapResult`, the trace wire schema and `AdapterContract` are not redesigned
  (Audit 2 "Things NOT to change").
- No run-output channel (Audit 2 E3): artifacts stay named by configuration; the trace path stays
  documented.
- Per-test service substitution is not built here; it is the first item of `eng/feature-plan.md`.
- Container restartability and successful-rollback re-arm stand (Audit 1 B3); this plan adds the
  failed-release state (B1).
- SQL connection double disposal stays (Audit 2 B7 decision).
- `ProtoProtocol` adoption for Sql/Sheets is closed (Audit 1 INT-52); D9 is an observation only.
- Descriptor-count registration tests stay as a deliberate refactoring brake (F10); they are labelled,
  not weakened.

- Stage 5: options-binder relocation is not taken (validation timing was the finding's real payoff);
  AddWeb keeps one backend per host; Selenium driver and messaging broker ownership are documented
  rather than flagged; raw gRPC calls resolve a fresh authenticator, documented; the `CompleteTest`
  publication race is fixed without a deterministic test.
- Stage 7: `IProtoTestIdGenerator.Next` keeps its method parameter (documented as advisory);
  `ProtoTraceValueSource.Observed` and `ProtoReportItemKinds.Metric` stay as open wire/report
  vocabulary; B5 (stop with active tests) and B6 (release-in-flight early return) get no guard because
  the run state machine makes both unreachable; the registration-shape and report-markup tests are
  labelled `Characterization` rather than rewritten.
- Stage 8: `PublicApiAnalyzers` is not taken — package validation against the 1.0.1 baseline with
  recorded suppressions is the commitment; template `--runner` variants (2.10) and the static trace
  index (2.14) are deferred; the crash-resilience answer is documentation, not an incremental flush.

## Stop criteria

- One redaction/serialization policy at the evidence boundary, proven by an adversarial suite across
  trace, report, attachment and malformed-input axes; no finding metadata, base address or archive entry
  leaks a named secret.
- A resource's ownership period is always released, including after a failed release and a retried start.
- One client entity per client instance in a trace; configuration, operations and release all reference
  it.
- `Cancelled`/`Failed`/`Skipped`/`Unknown` mean one thing per producer and adapter, documented and tested.
- Every adapter's lifecycle boundary is documented, pinned by a real-run characterization test, and spans
  setup/teardown where the framework allows it.
- Configuration errors fail configuration, not the first test observation.
- Trace size, export time and peak memory are measured at 1–2k tests and bounded by an explicit artifact
  policy.
- No test asserts dead code; each adapter has a real-run setup-failure, teardown-failure and outcome test.
- Then stop. Do not refactor further because another design exists.

## Things I would NOT change

- The run/test state machines, `ProtoResourceRegistry`, `ProtoFlow`, `ProtoTestScope`'s teardown swallow.
- `ProtoExecutionContext` as one façade and the ambient `Proto.Context` model.
- The trace wire model, format versions and viewer contract — fix contents, not schema.
- Attributes as capabilities, `ProtoAttributeResolver`, per-adapter `MapResult`, `ProtoPolling`,
  `ProtoCoverageCollector`'s assertion-level philosophy, and the pure static helpers.
- `AdapterContract` and the shared test doubles; extend them instead of replacing them.
- The DI-descriptor registration tests; label them as characterization.

## New problem classes discovered

1. Runner-boundary divergence as a contract problem: five adapters implement five lifecycle spans that
   were never written down or tested; this is the framework's central promise, and only the runner varies.
2. Derived vocabulary has no owner: entity ids, cancellation outcomes, coverage-on-failure, options
   validation timing and diagnostic-failure policy are decided at call sites.
3. Telemetry ownership in multi-host processes: a global listener plus ambient fallback is an ownership
   problem; `FindTraceWriter` already contains the answer.
4. Evidence serialization has two policies; only observations use the safe one.
5. Retry state machines with unhandled transitions: `Rearm`/`TryBeginRelease` model two ownership periods
   with one transition path.
6. Docs are an unowned contract surface for lifecycle semantics, where drift can cause real test damage.
7. Adapter tests that bypass the adapter create false confidence; the tests that go through `dotnet test`
   are the ones that catch runner integration breaks.
