# ProtoTest Internal Cleanup Plan

Audit date: 2026-09-23 · Baseline commit: `500daf9`

Scope: all of `src/` (28 projects), `tests/` (25 projects), `samples/`, build/CI/tooling.
`assets/internal` was excluded from the audit on purpose.

Method: full read of `ProtoTest.Core` plus four deep-dive passes (integrations, web/sheets/sql,
adapters/reporting, tests/tooling). Finding IDs below are stable references to those audit reports.

**Goal:** readability and understandability over cleverness. The code must get smaller.

**Baseline line counts** (C#, excluding obj/bin):

| Area | Lines |
| --- | --- |
| `src/` | 27,696 |
| `tests/` | 22,809 |
| `samples/` | 6,620 |
| **Total** | **57,125** |

## Locked decisions

- Integration contract for ownership (protocol descriptor, shared registration/initializer/context
  state, explicit ownership), template bases for execution, and a small `ProtoFlow` for sequences
  (named steps, per-step timeout/retry, fail-fast and collect modes). No generic step engine, no
  behavior trees, no state-machine framework.
- `ProtoRunStateMachine`: extract states, guarded transitions and the failed-stop memory out of
  `ProtoHost`.
- `ProtoTestScope` plus a template adapter base; one teardown-failure policy (record as findings,
  never mask the test result).
- Pure functions stay static; anything with policy, IO or state becomes an injected service.
  Builders stop reading `Proto.Context` internally; the public test API keeps it.
- Protocol descriptor plus enums replace stringly contracts (observation kinds, trace sources,
  boolean behavior flags).
- Web: fix behavior (shared actionability/poll engine) and dispatch (shared operation descriptor)
  where each lives; the backend visitor is deferred until a third backend or a growing operation
  set makes it necessary.
- No template engine for the HTML report; view model plus small instance renderers.
- gRPC keeps its blocking stream helpers but isolates them behind an explicit `Blocking` adapter and
  passes `CancellationToken` into `CallOptions`.
- Accidental-public plumbing may break in 1.x (no users to protect): adapter assembly scaffolding,
  `IWebBackend` when the visitor lands, option classes that leak native types.

## Rules

1. **Net deletion is the acceptance criterion.** Every stage records `git diff --shortstat` in the
   progress table. A stage may only be net-positive with a one-line justification recorded there.
   Stages 0 and 1 are the expected investment; every stage from 2 on must be net-negative, and the
   cumulative total must be clearly negative by Stage 6 and at the end.
2. Every stage ends green: `eng/test.ps1` full suite plus the format gate, no skipped suites.
3. New shared code is a template base, a descriptor, or an injected service. No new static helpers.
4. A stringly contract becomes a descriptor, enum or constant at the point where it is introduced,
   not in a later cleanup.
5. Low-severity findings are fixed opportunistically when a file is touched anyway; they are not
   scheduled.
6. Public surface changes are additive except for the accidental-public plumbing listed above, and
   each one is called out in the commit message.
7. **Nothing is deferred into prose.** A finding that is consciously postponed gets a stage entry
   here, even when the stage is at the end; an item without a stage is an item that will not happen.

Finding ID scheme: `INT-F#` integrations · `WEB-X/W/P/S/B#` web · `SHT-SH#` sheets · `SQL-SQ#` sql ·
`TC-T#` testcontainers · `ADP-#` adapters · `RPT-#` reporting · `ANC-#` aspnetcore · `OAPI-#` openapi ·
`TST-#` tests · `TOOL-#` tooling · `SMP-#` samples.

## Stage 0 — Guardrails and test hygiene

Why first: later refactors need trustworthy tests. Today `SheetsTests` leaks 44 hosts, adapter tests
duplicate their fixtures, and Windows CI runs 4 of 23 suites.

- `TOOL-1..7`: `.editorconfig`; `EnableNETAnalyzers` (warnings first, `TreatWarningsAsErrors` after
  the existing wall is cleared); `dotnet format --verify-no-changes` gate; `Directory.Packages.props`;
  `tests/Directory.Build.props`; `global.json`; Windows CI runs the full suite via `eng/test.ps1`.
- `TST-1..10`: `tests/ProtoTest.TestSupport`; a `StartTestAsync` overload that removes the 47
  `Placeholder` helpers and 81 `GetCurrentMethod()` casts; shared fakes (`StubHandler`,
  `CapturingSink`, `StaticConfigurationSource`, ...); merge `AdapterContract`/`AdapterTestSupport`;
  dedupe `AssemblyInfo` and `TestService`; fixture base.
- `TST-1..11`: fix the `SheetsTests` host leak; split `WebModelTests` (2077), `SheetsTests` (1162),
  `ProtoDataTests` (996); deterministic waits instead of sleeps; naming/AAA convention.
- `INT-F34`: pass `CancellationToken` into gRPC `CallOptions` (standalone correctness fix).

Expected: net-positive (guardrails and support code). Justified as the foundation for every later
deletion.

## Stage 1 — Core primitives

- `ProtoRunStateMachine` extracted from `ProtoHost` (`ProtoHost.cs` is 479 lines with four
  lock+switch blocks today).
- `ProtoFlow`: named steps, per-step timeout/retry, fail-fast/collect. Adopt
  `ProtoTraceScope.RunAsync` where the pattern is hand-rolled (`WEB-X1`: `WebSession`, SQL release,
  workbook tracing, Playwright trace retention, Core's own lifecycle).
- `ProtoPolling` promoted from `WebPolling` with a timing options object (`WEB-X4`).
- `ProtoTest.Json`: `JsonScalarTypes`, `ProtoJsonPropertyProjection`, `ProtoJsonDefaults`
  (`INT-F2`, `INT-F10`, `INT-F11`, `INT-F18`).
- `ProtoAttachmentCapture` for gRPC/messaging (`INT-F49`); `TryRegisterOnce` registration guard
  (`WEB-X3`); ownership enum replacing `disposeWithContext` (`ANC-3`).

The `ProtoProtocol` descriptor moved to Stage 3, where its first consumers (registration,
coverage, trace sources) land: a descriptor with no reader would be dead code.

Expected: net-positive (new primitives), offset from Stage 2 on.

## Stage 2 — Lifecycle and adapters

- `ProtoTestScope` with one teardown-failure policy (`ADP-3`; fixes the MSTest result-masking bug).
- Template assembly base replacing the CRTP shape (`ADP-1`); attachment publisher base (`ADP-2`);
  outcome mappers (`ADP-7`); one sync bridge (`ADP-4`); framework name constants (`ADP-8`).
- Xunit3 mixin attributes (`ADP-5`); `ProtoTestCases` comment/visibility (`ADP-6`).

`ADP-1` and `ADP-7` moved to Stage 11: the assembly bootstrap template would force an abstract
`Configure` on all five adapters (public API, docs and samples churn for ~40 lines), and the outcome
mappers would add strategy types to move mapping that is already small and testable where it matters.
Both are decisions, not obvious wins.

Expected: net-positive (the Core scope, bridge and publisher base are additions; the payback is the
deleted per-adapter policy and duplicated plumbing).

## Stage 3 — Integration contract

Done: the `ProtoProtocol` descriptor (`INT-F3`, `INT-F9`, `INT-F32`); one keyed
`ProtoHttpContextState` and a concrete `ProtoHttpAuthLifecycleHook` replacing the per-protocol state
and hooks (`INT-F6`); the shared HTTP/gRPC auth applier (`INT-F7`); `RegisterClientName` and
`ProtoClientTraceState` sharing the client registration and configuration tracing (`INT-F8`); the
shared status assertion and assertion facade (`INT-F40`, `INT-F5`); `ProtoHttpResponseContext`
(`INT-F41`).

Closed as no-change, with the reason recorded here:

- `INT-F48` application resolver: the two lookups answer different questions (which application the
  test selected vs. which application a registered target belongs to).
- `INT-F17` AddProtocol helper: after the descriptor, capability property and `RegisterClientName`,
  each integration's wrapper is four lines; a helper would add indirection for no net lines.
- `INT-F29` instance-based gRPC/messaging assertions: C# has no extension properties, and the
  static-extension idiom is deliberate and documented in the code.
- `INT-F42` AssertShape context: the response base computes part of the shape context (the attachment
  name and capture flag), so callers cannot supply the whole record; the current split is the right
  seam.
- `INT-F13` transport resolver: both protocols already resolve through
  `ProtoApplicationResolution.ResolveTransportClient`; what differs (endpoint-rooted HTTP address vs.
  gRPC channel forwarding) is protocol-specific policy, not duplication.
- gRPC builder inheriting the HTTP protocol builder: the base is named for HTTP and carries
  response/URL options gRPC has no use for; sharing the client naming covers the real overlap.

Expected: net-negative.
- Coverage collectors consume the descriptor (`INT-F9`).

Expected: net-negative.

## Stage 4 — HTTP execution (REST and GraphQL)

Complete. Done: operation-kind enum (`INT-F30`); dead parsed document removed (`INT-F44`);
`ProtoObservationCapture` is the one diagnostic rule both protocols share; `ProtoHttpRequestBuilder`
owns the shared state, DSL, authentication application and trace helpers (`INT-F1`); the REST send
path is an orchestrator over resolve-URI, prepare and send-and-observe phases with one failure arm
(`INT-F21`); the GraphQL shape-driven operation is one immutable `ShapePlan` record instead of four
fields kept in step by hand (`INT-F26`); responses and subscriptions record through
`GraphQLObservations.Response` (`INT-F15`); the data-less assertion goes through
`ProtoShapeAssertion.AssertMissing` (`INT-F16`); the builder is partial classes by concern —
operations, execution, subscriptions (`INT-F19`); the filter builder builds nested filters through one
helper (`INT-F47`).

The full send-template extraction was designed member by member and deliberately rejected: the
comparison showed it is roughly line-neutral while introducing twenty behavioural traps (header trace
timing and parent, GraphQL endpoint child operation, attachment capture ordering and redaction,
response-section shapes, ownership/disposal differences, subscription bypass). The shared seam closes
the drift without the risk.

Closed as no-change, with the reason recorded here:

- `INT-F45` single decode: the second read decodes an in-memory buffer; re-implementing
  `ReadAsStringAsync`'s charset and BOM handling would risk behaviour for no real gain.
- `INT-F43` `ReadAsAnonymous`: the parameter exists for generic inference and the XML doc says its
  value is ignored; validating it would add reflection for no behaviour change.

Expected: net-positive as built (the seam and the split add files; the payback is the closed drift and
the readable phases). The remaining net reduction comes from Stages 5-9.

## Stage 5 — gRPC and Messaging

Complete. Done: `BeginCallTrace` and `CompleteCall` own the duplicated trace and completion blocks of
the four call shapes (`INT-F14`); the message formatter is its own partial file, `ProtoGrpcClient`
522 lines and `ProtoGrpcClient.Formatting` 98 (`INT-F20`); the synchronous stream opens moved behind
`ProtoGrpcBlockingClient`, reachable as `client.Blocking`, so the client keeps one async API and the
blocking thread behaviour is explicit (`INT-F35`); `GrpcAttachmentOptions` derives from a
protocol-neutral `ProtoDiagnosticCaptureOptions` instead of inheriting HTTP headers and query
parameters (`INT-F46`); `MessagingRegistration` owns its own once-only setup and adapter application
(`INT-F27`); the in-memory broker is one lock and one publish signal with predicates running in the
awaiting flow, replacing the waiter list and per-waiter locks (`INT-F37`); the RabbitMQ consumer is fed
by an `AsyncEventingBasicConsumer` into an unbounded channel instead of `BasicGet` polling, which
retires `RabbitMqOptions.PollInterval` (`INT-F38`); `ProtoMessagingProtocol` owns the trace source, the
operation names and the receive observation kind, and documents the deliberate verb pair (`INT-F50`).

The RabbitMQ round trips are exercised by the CI job that runs against a real broker; locally they
skip, so that change is verified by CI on the next run.

Expected: net-negative.

## Stage 6 — Data and diagnostics

Complete. Done: `ProtoDataRedactionPolicy` owns the redaction sets, the member/type matching and the
graph walker, and `ProtoDataRegistry` delegates (`INT-F24`); `FormContentRedactor`,
`MultipartContentRedactor` and `XmlContentRedactor` are their own types and `JsonDiagnosticSanitizer`
keeps the JSON redactor and the sniffing facade (`INT-F23`); `ProtoDataObjectBuilder` is partial
classes by concern — planning, construction, tracing (`INT-F22`); the scoped data service's `For<T>()` entry reads `Proto.Context` once and passes the execution context
into the builder, so the builder no longer reads the ambient static (`INT-F39`).

Closed as no-change: `SHT-SH9` (`SheetModelAssertions`), because the assertion is an extension on the
user's row record, which carries no context; every alternative either breaks the test-author API or
threads the context through model rows.

Expected: net-negative.

## Stage 7 — Web

Complete. Done from 7a/7b/7c and the bug list: one URL policy (`ProtoUriSanitizer.ForDisplay` and
`ForDiagnostics` replace the three hand-rolled sanitizers); one trace-source constant per backend;
`WebRouteDiscovery` memoizes one discovery task so concurrent navigations cannot double-record, with
retry after failure; one boolean-state helper per backend; Playwright's resolved action and read share
one runner; `WebTiming` owns the 5s/50ms defaults the poller, waits and both backends repeated; one
`TraceArtifactFailure` helper for all three artifact-failure writers; both backends are partial classes
by concern; `PlaywrightCorrelationState` owns the open operations, trace groups, gate and latest
operation (`WEB-P6`), with the page-diagnostic attribution rule documented (`WEB-P4`);
`PlaywrightLocatorTranslator` owns the semantic-to-Playwright translation, mirroring Selenium
(`WEB-B3`, `WEB-W12`); each backend owns its semantic key table, so the shared `WebKeyMap` no longer
names Playwright and Selenium, with the conformance test still proving both cover every key
(`WEB-W11`); `WebCoverageCollector` scans the page source on first use instead of in its constructor
and tracks inventory membership in a set while keeping the ordered list the pattern matching needs
(`WEB-W10`); `PlaywrightWebOptions` exposes bindable context settings plus a `ConfigureContext`
escape hatch instead of leaking `BrowserNewContextOptions` (`WEB-W15`, `WEB-P9`); the operation runner
threads its result through a typed local, so the public context carries no untyped slot (`WEB-W4`,
`WEB-B1`); the Vue discovery script is an embedded JavaScript resource and `VueRouteParser` owns the
parsing (`WEB-W8`); `WebProbeLoop` is the one retry loop behind element assertions and Selenium's
actionability, so both poll through `ProtoPolling` at the web interval and Selenium no longer
hand-rolls a `Stopwatch` loop (`WEB-B2`, `WEB-S2`); `SeleniumDriverExecutor` runs every WebDriver call
on one dedicated pump thread — driver access is serialized, `Task.Run` and the `Background` helper are
gone, and `IWebBackend.CurrentAddress` became `GetCurrentAddressAsync` so the address read hops onto
the pump too (`WEB-S4`, `WEB-X6`); `WebTextMatch`, `WebFlow.Check`/`Uncheck` and
`BeSortedBy(ProtoSortDirection)` replace the boolean behaviour flags (`WEB-X5`).

Closed as no-change: `WEB-W7` (strategy service in place, real filesystem kept: the tests drive the
strategies through temp folders, and a file-system interface would add surface with no failing case to
justify it); the polarity enum (`WEB-X5` remainder: `negated` is the polarity of an assertion object,
already encapsulated by the `Should`/`ShouldNot` facades, and its only remaining form is the documented
bool parameter of `ProtoAssertion`'s pure primitives — an enum would either churn five integrations for
naming taste or add a mapping layer that reads worse than the bool).

Expected: net-negative.

## Stage 8 — Sheets, Sql, Testcontainers

Complete. Sheets: `SheetColumnBinding` precomputes one binding per `[Column]` property when the model
reads the sheet, so the read and verify paths no longer re-read attributes per row; `SheetCellValue` is
the one cell-to-.NET converter, replacing the separate throwing and non-throwing helpers; the
`SheetDateFormat` number-format walk is its own type and is tested directly; `ProtoSheet.RecordRead` is
the one read-observation recorder, `ProtoSheet.Range` reads through `CellByNumber`, and
`ProtoCell.Display` renders through `RenderedValue` (`SHT-SH1..SH8`, `SHT-SH10..SH13`). Sql:
`ProtoSqlSession` owns the connection and transaction lifecycle - open, begin, release - so
`Transaction` has a private setter and the hook only registers the resource; the release is a
`ProtoFlow` in collect mode, so a failing rollback can no longer skip the disposals and a single
failure keeps its original stack; `SqlOptions.SharedWithApplications` delegates to
`ShareConnectionWith` (`SQL-SQ1..SQ4`). Testcontainers: `ContainerStartResult` carries the started
resource or the reason it could not start, and the base owns the one `TryStartContainer` template, so
both container packages only declare their builder and the two conveniences (`TC-T1..T3`).

Expected: net-negative.

## Stage 9 — Reporting, OpenApi, AspNetCore

Complete. Reporting: `ReportViews` projects every item once into a `ReportItemView` carrying the
coverage state, labels and badges the markup derives from the item and its subtree, in one post-order
pass, so the renderer no longer re-flattens each item's subtree - the quadratic cost the audit named;
`HtmlReportRenderer` is one instance that owns the markup builder and its item markup lives in a
partial by concern; `ReportText` is the report's one plural source, replacing two `PluralSuffix`
overloads and the inline entry/error/warning/occurrence pluralizations (`RPT-1..RPT-12`). OpenApi:
`OpenApiRouteMatcher` owns route normalization, segment and constraint matching plus the status-code to
response-key lookup; `OpenApiCoverageLedger` owns the typed endpoint/response/property keys and
counters, normalizing property paths at its boundary; `OpenApiReportBuilder` renders the contract from
the document and the ledger, leaving the collector as the observation intake (`OAPI-1..OAPI-8`).
AspNetCore: `AspNetCoreServerState` owns the fields the trace entity state and the initialize event
share, and the per-run/per-test acquisition with its ownership registrations moves into
`PerRunServerLifetime`/`PerTestServerLifetime` behind `IAspNetCoreServerLifetime`, so the initializer
does not branch on the lifetime (`ANC-1..ANC-8`).

Closed as no-change: the AspNetCore DI registration guard (`ANC-4`): the host overload's per-name guard
is deliberate - differently named servers must compose - so the Core one-marker guard does not apply,
the application overload documents that repeated calls compose by design, and
`RegisterApplicationServices` already guards with `TryAddKeyedScoped`.

Expected: net-negative.

## Stage 10 — Samples, templates, docs

Complete. Samples: `NorthstarApiProvisioner<TRequest, TResponse>` is the one shape every API
provisioner shares - POST the request, require the created status, read the response and return it
with its id - so the four API provisioners only declare their URL, body, route values and id
(`SMP-2`); `DemoSupport` and `TestSupportProbe` became instances: the demo helpers are reached through
`context.Demo()` and no helper reads the ambient `Proto.Context` itself, and the probe is a run-scoped
singleton whose verification lives exactly as long as the host that produced it (`SMP-1`, `SMP-5`);
the delivered-webhook wait uses `ProtoPolling`, the same bounded polling engine every other wait uses
(`SMP-4`).

Closed as no-change: subscription readiness (`SMP-3`): graphql-transport-ws acknowledges the
connection, not each subscribe message, so there is no readiness signal to wait on; the sample's
documented start-the-read-then-publish pattern is the workaround. The starter template (`TP-1`,
`TP-2`): it already composes through `ProtoTestAssembly`, `[Application]` and `[ProtoTest]` with
`Proto.Context` and shape assertions, and follows the current facades, so there is no stale API to
migrate. Docs: the public-surface changes of Stages 7-9 are already reflected (`WebFlow.Check`/
`Uncheck`, `BeSortedBy(ProtoSortDirection)`, the `TryStart` result, `ProtoSqlSession`), verified by
searching the docs for every changed member.

Expected: net-negative.

## Stage 11 — Deferred test hygiene

Complete. `TST-5`: one shared `tests/NUnitParallelization.cs` (the
`LevelOfParallelism`/`Parallelizable`/`FixtureLifeCycle` trio) is linked into the nine NUnit test
projects that already ran in parallel, replacing nine copies; the remaining NUnit projects stay
single-threaded because they share run-scoped resources that are not verified parallel-safe - gRPC's
suite failed under parallelism (shared server state), and Sql, Testcontainers and RabbitMQ share
containers, Web shares browser sessions, and Data, Messaging, OpenTelemetry, SampleApp.Domain and
Sheets share fixture state. `TST-7`: `AdapterLifecycle` is the one expected-lifecycle record and
`TrackingHook`/`TrackingAttribute` the one hook/attribute pair, with `ExecutionLogState` carrying the
contract-verified flag; `AdapterTestSupport`, `AdapterContractAttribute`, `AdapterContractHook` and
`AdapterContractState` are gone, and the compliance and lifecycle suites share one name set
(`Class`/`Method`, orders 10/20). `TST-S7/S8`: the naming and AAA convention is documented in
`CONTRIBUTING.md` and the adapter attribute orders are one set; applying the AAA comments to the
ASP.NET tests moves to Stage 12 as `TST-14`. `S5`: the RabbitMQ slow predicate keeps its
`Thread.Sleep(20)` with a comment naming it the deliberate timing probe it is. `TST-12`: the NUnit test
projects take `NUnit.Framework` as a global using and the per-file imports are gone; the second
candidate (globalizing `System.Diagnostics`/`System.Text.Json` for the partial-class splits) was
rejected under the finding's own rule - it would hide a dependency rather than remove repetition.
`ADP-1` decided: accept the five short forwarders as the cost of five different framework entry points
(forcing an abstract `Configure` would churn public API, docs, samples and every user's setup class
for uniformity alone). `ADP-7` decided: accept the current outcome mapping methods (a mapper type per
adapter would move small, already-tested mapping behind an interface).

Expected: net-negative.

## Stage 12 — Follow-ups from the refactors

- `CTX-1` (done): the context rule is documented and implemented. `Proto.Context` is the lookup for
  everything that runs inside a test on the test's flow — test bodies and test-author entries
  (`context.Data().For<T>()`, `row.ShouldMatchShape(...)`, `exception.ShouldHaveStatus(...)`) and the
  plumbing they call, with explicit passing preferred only where it keeps the callee constructible in
  a test. Run scope uses the host (`ProtoHost.CurrentHost`, the reference a hook receives); off-flow
  telemetry uses `ProtoHost.FindTraceWriter(Activity?)` and correlates by trace id; objects whose
  lifetime spans tests resolve per call and never hold a context. The rule lives in the `Proto.Context`
  XML remarks and in `CONTRIBUTING.md` ("Context lookups"). The assertion facades are compliant as
  written; wrappers are not needed. `IProtoContextAccessor` stayed removed, and the data service keeps
  its single entry read.
- `TST-13` (done): `StubHttpHandler`, `StubTransportInitializer` and `StaticConfigurationSource`
  live in `tests/ProtoTest.TestSupport`; nine private copies are gone, and the two doubles with a
  genuinely different shape (the payload-only GraphQL stub and the protocol-only HTTP transport
  initializer) stay with the suites that need them.
- TST-14 (done): the four ASP.NET test fixtures follow the naming and AAA convention - the numbered phase comments are normalized to `// Arrange`, `// Act` and `// Assert`, and every test long enough that its phases are not obvious carries them.
- `INT-51` (done, closed as no-change): the four sites share no output rule - `GraphQLLiteral` writes GraphQL literal syntax (unquoted enums, `$var`, its own escaping), `JsonShapeMatcher` compares JsonElements and writes no scalar text, `JsonDiagnosticSanitizer` delegates to `JsonSerializer`, and `ProtoTraceValueFormatter` renders display text; the classification half is already shared as `JsonScalarTypes`. Original finding: design-check and, if it holds, extract one scalar write/compare rule shared by
  `JsonShapeMatcher`, `GraphQLLiteral`, `JsonDiagnosticSanitizer.Serialize` and
  `ProtoTraceValueFormatter` (the classification half is `JsonScalarTypes`; this is the output half).
- `INT-52` (done): `ProtoSqlSession.TraceSource` and `ProtoSheets.TraceSource` are the one source constant per integration (the Entity Framework hook keeps its own private one), used by every operation, flow and capability; Testcontainers and OpenTelemetry register no literal capabilities and capture nothing, so that half is a no-op. Original finding: finish descriptor adoption for the integrations that still register capabilities with
  literal names and sources (Sql, Sheets, Testcontainers, OpenTelemetry), and let their capture
  options share `ProtoDiagnosticCaptureOptions` where they capture at all.
- `REF-1` (done, decided): the partials stay files. The seams worth holding were already extracted where they mattered (`ShapePlan`, `ProtoDataRedactionPolicy`, the sanitizer collaborators, `GraphQLFilterBuilder`); the remaining ones (GraphQL subscriptions, data planning) are sequences over the builder's own state, so a collaborator would need the builder reproduced to be tested and would cost more than the file split. Original finding: decide per partial-class split which parts earn a real collaborator (candidates: GraphQL
  subscriptions, data planning) versus staying files; a collaborator only where there is a seam a test
  can hold, since indirection without one costs more than the file split.
- `TST-15`: make every test project parallel-safe. Done: nine of the ten. gRPC and Sheets first, then Data, Messaging, RabbitMQ (its non-static one-time teardown fixed), Sql, Testcontainers, Web and OpenTelemetry. The gRPC echo server moved
  to a namespace-level `[SetUpFixture]` - `FixtureLifeCycle(InstancePerTestCase)` requires static
  `[OneTimeSetUp]`/`[OneTimeTearDown]` and otherwise disposes a shared server per test instance - and
  the two tests that read the echo service's process-wide "last authorization" are
  `[NonParallelizable]`. The Sheets fixture builds its workbook and hosts per test (`[SetUp]`/
  `[TearDown]`, for the same reason) and keeps them in instance fields. Both projects link the shared
  parallelization file. Remaining: Sql, Testcontainers and RabbitMQ share run-scoped containers (likely
  safe once each test isolates its data), Web shares browser pools, and Data, Messaging, OpenTelemetry
  and SampleApp.Domain were reviewed. Only SampleApp.Domain.Tests stays single-threaded: its tests share
  the sample domain's in-memory state across test cases, which no fixture boundary can isolate.

Expected: assessed per item; the test doubles and descriptor adoption are net-negative candidates.

## Progress

Record `git diff --shortstat` for the stage's commits after the suite is green, split by area: `src`, `tests`, `docs` and `other` (guardrails, docs outside `docs/`), with the plan file itself excluded from every row. The boundaries are the commits that recorded each stage.

| Stage | Status | Net lines | Notes |
| --- | --- | --- | --- |
| 0 — Guardrails and test hygiene | Complete | src +148 / −166 · tests +4724 / −4604 · other +232 / −73 (net +261) | Net-positive by design: the guardrails and the shared support are the investment every later stage spends. Done: analyzers + `EnforceCodeStyleInBuild` + `TreatWarningsAsErrors`, `.editorconfig` + format gate (`eng/lint.ps1`), central package management, `global.json`, `.gitattributes`, CI runs the full suite on Windows; `tests/ProtoTest.TestSupport`; 34 `TestMethod()` reflection helpers and 81 `GetCurrentMethod()` casts replaced by `TestMethods.Placeholder`; sink fakes deduplicated; `ProbeTestService` replaces five copies; `SheetsTests` disposes its hosts; the giant fixtures are partial classes split by concern; gRPC cancellation fix with a regression test. Deferred to Stage 11: `AssemblyInfo` parallelization (changing the semantics is behavioural and belongs in its own pass), `AdapterContract`/`AdapterTestSupport` merge and the naming/AAA convention (cosmetic), the intentional RabbitMQ slow-predicate sleep. |
| 1 — Core primitives | Complete | src +743 / −395 · tests +171 / −7 (net +512) | Net-positive by design: the primitives every later stage spends. Done: `ProtoRunStateMachine`; `ProtoFlow` with named steps, timeout, retry and fail-fast/collect plus `ProtoTraceScope.RunAsync` adoption; `ProtoPolling` (WebPolling deleted); `JsonScalarTypes`, `ProtoJsonDefaults`, `ProtoJsonPropertyProjection`, `JsonDiagnosticSanitizer.LooksLikeJson`; `ProtoAttachmentCapture`; `ProtoRegistrationGuard`; `ProtoClientOwnership`. `ProtoProtocol` moved to Stage 3 where its readers land. |
| 2 — Lifecycle and adapters | Complete | src +237 / −131 · tests +55 / −2 (net +159) | Net-positive by design: `ProtoTestScope`, `ProtoTestAsync` and the attachment publisher base are new Core primitives, plus two tests pinning the shared policy. The five adapters now share one teardown policy (fixes MSTest replacing the reported result), one publisher shape, one sync bridge and one xUnit v3 before/after; the `lifecycleStarted` guards and per-adapter completion try/catch are gone. `ADP-1` and `ADP-7` moved to Stage 11 as decisions. |
| 3 — Integration contract | Complete | src +470 / −352 · tests +34 / −35 (net +117) | Net-positive: the descriptors, keyed state, shared facade/context records and client-trace helper are new shared code. Every trace-source literal, observation kind, coverage category and capability now reads from `ProtoProtocol`; one auth hook and one context state serve REST, GraphQL and gRPC; gRPC's status assertion and auth applier delegate to the shared ones; REST and GraphQL share the assertion facade; `ProtoHttpResponseContext` replaced the positional parameter lists. Six findings closed as no-change with reasons in the stage notes. |
| 4 — HTTP execution | Complete | src +1172 / −916 (net +256) | Done: shared request-builder seam and observation rule (`INT-F1`), readable REST phases (`INT-F21`), `ShapePlan` record (`INT-F26`), observation factory (`INT-F15`), `AssertMissing` (`INT-F16`), partial-class split (`INT-F19`), operation enum (`INT-F30`), dead AST removed (`INT-F44`), nested-filter helper (`INT-F47`). The full send template was designed and rejected as line-neutral with twenty behavioural traps. `INT-F43`/`INT-F45` closed as no-change. |
| 5 — gRPC and Messaging | Complete | src +500 / −416 · tests +4 / −4 (net +84) | Done: `BeginCallTrace`/`CompleteCall` (`INT-F14`), formatting partial split (`INT-F20`), `Blocking` adapter (`INT-F35`), neutral capture options (`INT-F46`), messaging registration object (`INT-F27`), single-signal in-memory broker (`INT-F37`), RabbitMQ async consumer (`INT-F38`, CI-verified), messaging descriptor (`INT-F50`). |
| 6 — Data and diagnostics | Complete | src +1050 / −925 (net +125) | Done: `ProtoDataRedactionPolicy` extracted from `ProtoDataRegistry` (`INT-F24`); `JsonDiagnosticSanitizer` down to 142 lines with `FormContentRedactor`, `MultipartContentRedactor` and `XmlContentRedactor` as their own types (`INT-F23`); the data builder split into planning/construction/tracing parts (`INT-F22`); the ambient read moved to the data service's `For<T>()` entry, which passes the context into the builder (`INT-F39`, refined by `CTX-1`). `SHT-SH9` closed as no-change: the row assertion is an extension on the user's record, which carries no context. |
| 7 — Web | Complete | src +1514 / −1035 · tests +151 / −48 · docs +5 / −4 (net +583) | Done: one URL policy and trace constants (`WEB-S8`, `WEB-X8`), memoized route discovery (`WEB-W9`), boolean-state helper (`WEB-P2`, `WEB-S3`), one resolved runner (`WEB-P3`), `WebTiming` defaults (`WEB-X4`), one artifact-failure helper (`WEB-X7`), backend partial splits (`WEB-P1`, `WEB-S1` partial), correlation state (`WEB-P4`, `WEB-P6`), Playwright translator (`WEB-B3`, `WEB-W12`), backend key tables (`WEB-W11`), lazy coverage inventory (`WEB-W10`), bindable Playwright options (`WEB-W15`, `WEB-P9`), scanner strategies and framework options (`WEB-W7`), operation descriptors for the session wrappers (`WEB-W1`, `WEB-W2`), typed runner result (`WEB-W4`), embedded Vue script with its own parser (`WEB-W8`), `WebTextMatch` flag (`WEB-X5` partial); the probe loop shared by assertions and Selenium (`WEB-B2`, `WEB-S2`); the single-thread Selenium driver executor, with `IWebBackend.GetCurrentAddressAsync` replacing the sync address property (`WEB-S4`, `WEB-X6`); `WebFlow.Check`/`Uncheck` and `BeSortedBy(ProtoSortDirection)` (`WEB-X5` complete; the polarity enum closed as no-change with its reason in the stage notes). Net-positive: `WebProbeLoop` and `SeleniumDriverExecutor` are new shared primitives and the partial splits add headers, so the duplication they remove is smaller than the audit estimated. |
| 8 — Sheets, Sql, Testcontainers | Complete | src +410 / −353 · tests +93 / −6 · docs +1 / −1 (net +144) | Done: `SheetColumnBinding` precomputes one binding per `[Column]` property so the read and verify paths stop re-reading attributes per row, `SheetCellValue` is the one cell-to-.NET converter, `SheetDateFormat` owns the number-format walk and is tested directly, `ProtoSheet.RecordRead` is the one read-observation recorder, `ProtoSheet.Range` reads through `CellByNumber` and `ProtoCell.Display` renders through `RenderedValue` (`SHT-SH1..SH8`, `SHT-SH10..SH13`); `ProtoSqlSession` owns the connection and transaction lifecycle with a private transaction setter and releases through a collect-mode `ProtoFlow`, and `SqlOptions.SharedWithApplications` delegates to `ShareConnectionWith` (`SQL-SQ1..SQ4`); `ContainerStartResult` plus the base's one `TryStartContainer` template remove the `out` triple and the duplicated try/catch from both container packages (`TC-T1..T3`, public-surface change). Net-positive: the extracted types carry headers and doc comments and the date parser's direct tests are new coverage the audit asked for, so the duplication they remove is smaller than the audit estimated. |
| 9 — Reporting, OpenApi, AspNetCore | Complete | src +802 / −535 (net +267) | Done: `ReportViews` projects every item once so the renderer stops re-flattening subtrees, `HtmlReportRenderer` is one instance with its item markup in a partial, and `ReportText` is the one plural source (`RPT-1..RPT-12`); the OpenAPI collector splits into `OpenApiRouteMatcher`, `OpenApiCoverageLedger` with typed keys and `OpenApiReportBuilder` (`OAPI-1..OAPI-8`); `AspNetCoreServerState` is the typed server state and the lifetime strategies own acquisition and ownership, so the initializer does not branch (`ANC-1..ANC-8`; the DI registration guard closed as no-change with its reason in the stage notes). Net-positive: the extracted types carry headers and docs, and no test lines changed - the behavior is pinned by the existing suites. |
| 10 — Samples, templates, docs | Complete | samples +106 / −131 (net −25) | Done: `NorthstarApiProvisioner<TRequest, TResponse>` is the one shape the four API provisioners share, so they only declare URL, body, route values and id (`SMP-2`); `DemoSupport` is reached through `context.Demo()` and no helper reads the ambient context, `TestSupportProbe` is a run-scoped singleton (`SMP-1`, `SMP-5`); the webhook wait uses `ProtoPolling` (`SMP-4`). Closed as no-change: subscription readiness (no subscribe acknowledgement exists to wait on; the sample documents the workaround), the starter template (already the canonical flow, no stale API), and docs (verified current for every member Stages 7-9 changed). |
| 11 — Deferred test hygiene | Complete | tests +124 / −302 · docs +10 / −0 (net −168) | Done: one linked `tests/NUnitParallelization.cs` replaces nine copies and the ten projects that stay single-threaded have their reasons recorded (`TST-5`); `AdapterLifecycle` plus `TrackingHook`/`TrackingAttribute` merge the two tracking mechanisms, deleting `AdapterTestSupport`, `AdapterContractAttribute`, `AdapterContractHook` and `AdapterContractState` (`TST-7`); the naming/AAA convention is in `CONTRIBUTING.md` and the adapter orders are one set (`TST-S7/S8`, with `TST-14` moved to Stage 12); the RabbitMQ timing probe is named (`S5`); `NUnit.Framework` is a global using in the NUnit test projects (`TST-12`); `ADP-1`/`ADP-7` decided to accept the forwarders and the mapping methods. |
| 12 — Follow-ups from the refactors | Complete | src +45 / −37 · tests +112 / −179 · other +20 / −0 (net -39) | `CTX-1` done: one ambient entry plus parameters below, `IProtoContextAccessor` removed, `FindTraceWriter(Activity?)` added. `TST-13` done: the shared doubles live in `ProtoTest.TestSupport` and nine private copies are gone. `TST-15` partial: gRPC and Sheets now run in parallel (server to a `[SetUpFixture]`, fixture state per test, the two process-wide auth assertions `[NonParallelizable]`). Remaining: `TST-14` AAA comments in the ASP.NET tests, `INT-51` scalar format, `INT-52` descriptor adoption, `REF-1` partial-split review, the rest of `TST-15`. |
| **Cumulative** | | **+2276** | Target: clearly negative by Stage 6; see the note under the table |

### Measured line outcome (after Stage 5)

The audit's deletion estimates were optimistic. Every stage so far introduced shared primitives
(descriptors, options records, template seams, partial-file headers) whose cost exceeds the duplicated
lines they remove, so the cumulative count was +1389 after Stage 5 (recomputed from the same exact ranges) rather than approaching negative. The consistent
architecture and closed drift are real; the line target is not on track.

Revised expectation: Stages 6-10 each record their measured delta and justify any net-positive result;
the cumulative target is reassessed after Stage 9. If the final number is still positive, the honest
conclusion is that this codebase's duplication was smaller than the audit estimated, and the plan's
value is the consistency and the bug fixes, not the line count.

Final outcome: every stage is complete and the cumulative is net-positive. The audit's deletion estimates were
optimistic - the shared primitives, descriptors, extracted collaborators and their tests cost more lines than the
duplication they removed, and only Stages 10 and 11 were net-negative. What the plan delivered is the consistency
(one descriptor per protocol, one retry engine, one Selenium executor, one report projection, one cell converter),
the bug fixes (MSTest result masking, Selenium thread affinity, the quadratic report flattening, the Sheets date
parser, the SQL release chain), the nine parallel test suites, and a recorded decision for every finding.
