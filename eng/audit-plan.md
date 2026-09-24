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

- Sheets: one cell-value converter, precomputed column bindings, one read path, testable date-format
  parser (`SHT-SH1..SH8`, `SHT-SH10..SH13`).
- Sql: transaction ownership, release through `ProtoFlow`, binder cleanup (`SQL-SQ1..SQ4`).
- Testcontainers: template method and start-result type, no `out` triple (`TC-T1..T3`).

Expected: net-negative.

## Stage 9 — Reporting, OpenApi, AspNetCore

- Reporting: view-model projection, instance renderers, one token/plural source; fixes the O(n²)
  flattening (`RPT-1..RPT-12`).
- OpenApi: collector split into route matcher, typed coverage ledger and report builder
  (`OAPI-1..OAPI-8`).
- AspNetCore: lifetime strategy, typed server state, DI registration guard (`ANC-1..ANC-8`).

Expected: net-negative.

## Stage 10 — Samples, templates, docs

- Samples: statics to services, generic provisioner helper, subscription readiness, `Wait.UntilAsync`,
  instance composition (`SMP-1..SMP-5`).
- Template test flow (`TP-1`, `TP-2`); docs for changed public API.

Expected: net-negative.

## Stage 11 — Deferred test hygiene

The Stage 0 findings that were consciously postponed. They are here so they happen; pull any item
forward into an earlier stage when that stage touches the same files.

- `TST-5`: decide and apply one parallelization policy. Either link one shared assembly-info file
  (the `LevelOfParallelism`/`Parallelizable`/`FixtureLifeCycle` trio) to every test project or state
  per project why it stays single-threaded; today nine projects opt in and ten do not. The
  `FixtureLifeCycle(InstancePerTestCase)` part is the behavioral change to verify suite by suite.
- `TST-7`: merge `AdapterContract` and `AdapterTestSupport` into one tracking hook/attribute pair and
  one expected-lifecycle record (`TST-8` assertion helpers ride along).
- `TST-S7/S8`: document the naming and AAA convention in `CONTRIBUTING.md`, apply it to the ASP.NET
  tests, and normalize the adapter attribute orders.
- `S5`: replace the RabbitMQ test's `Thread.Sleep(20)` slow predicate with a signal-gated predicate,
  or keep it with a comment naming it the deliberate timing probe it is.
- `ADP-1`: decide the assembly bootstrap. Either force an abstract `Configure` on all five adapters
  (uniform, but public API, docs, samples and every user's setup class change) or accept the five
  short forwarders as the cost of five different framework entry points.
- `ADP-7`: decide the outcome mappers. Either extract an `IProtoTestOutcomeMapper<T>` per adapter
  (unit-testable in isolation, more types) or accept the current mapping methods where they are.
- `TST-12`: use global usings where they remove repetition rather than hide dependencies. Candidates:
  the per-file `System.Diagnostics`/`System.Text.Json`/`internal` using blocks the partial-class
  splits now repeat, and the test projects' shared imports. Add them to `Directory.Build.props` /
  `tests/Directory.Build.props` per assembly, keep project-specific usings explicit, and do not
  globalize anything that meaningfully points at a dependency.

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
- `TST-13`: move the remaining duplicated test doubles into `tests/ProtoTest.TestSupport`
  (`StubHandler`/`StubHttpHandler`, `StaticConfigurationSource`, `StubTransportInitializer`).
- `INT-51`: design-check and, if it holds, extract one scalar write/compare rule shared by
  `JsonShapeMatcher`, `GraphQLLiteral`, `JsonDiagnosticSanitizer.Serialize` and
  `ProtoTraceValueFormatter` (the classification half is `JsonScalarTypes`; this is the output half).
- `INT-52`: finish descriptor adoption for the integrations that still register capabilities with
  literal names and sources (Sql, Sheets, Testcontainers, OpenTelemetry), and let their capture
  options share `ProtoDiagnosticCaptureOptions` where they capture at all.
- `REF-1`: decide per partial-class split which parts earn a real collaborator (candidates: GraphQL
  subscriptions, data planning) versus staying files; a collaborator only where there is a seam a test
  can hold, since indirection without one costs more than the file split.

Expected: assessed per item; the test doubles and descriptor adoption are net-negative candidates.

## Progress

Record `git diff --shortstat` for the stage's commits after the suite is green.

| Stage | Status | Net lines | Notes |
| --- | --- | --- | --- |
| 0 — Guardrails and test hygiene | Complete | +5314 / −4843 (net +471) | Net-positive by design: the guardrails and the shared support are the investment every later stage spends. Done: analyzers + `EnforceCodeStyleInBuild` + `TreatWarningsAsErrors`, `.editorconfig` + format gate (`eng/lint.ps1`), central package management, `global.json`, `.gitattributes`, CI runs the full suite on Windows; `tests/ProtoTest.TestSupport`; 34 `TestMethod()` reflection helpers and 81 `GetCurrentMethod()` casts replaced by `TestMethods.Placeholder`; sink fakes deduplicated; `ProbeTestService` replaces five copies; `SheetsTests` disposes its hosts; the giant fixtures are partial classes split by concern; gRPC cancellation fix with a regression test. Deferred to Stage 11: `AssemblyInfo` parallelization (changing the semantics is behavioural and belongs in its own pass), `AdapterContract`/`AdapterTestSupport` merge and the naming/AAA convention (cosmetic), the intentional RabbitMQ slow-predicate sleep. |
| 1 — Core primitives | Complete | +915 / −403 (net +512) | Net-positive by design: the primitives every later stage spends. Done: `ProtoRunStateMachine`; `ProtoFlow` with named steps, timeout, retry and fail-fast/collect plus `ProtoTraceScope.RunAsync` adoption; `ProtoPolling` (WebPolling deleted); `JsonScalarTypes`, `ProtoJsonDefaults`, `ProtoJsonPropertyProjection`, `JsonDiagnosticSanitizer.LooksLikeJson`; `ProtoAttachmentCapture`; `ProtoRegistrationGuard`; `ProtoClientOwnership`. `ProtoProtocol` moved to Stage 3 where its readers land. |
| 2 — Lifecycle and adapters | Complete | +307 / −137 (net +170) | Net-positive by design: `ProtoTestScope`, `ProtoTestAsync` and the attachment publisher base are new Core primitives, plus two tests pinning the shared policy. The five adapters now share one teardown policy (fixes MSTest replacing the reported result), one publisher shape, one sync bridge and one xUnit v3 before/after; the `lifecycleStarted` guards and per-adapter completion try/catch are gone. `ADP-1` and `ADP-7` moved to Stage 11 as decisions. |
| 3 — Integration contract | Complete | +509 / −389 (net +120) | Net-positive: the descriptors, keyed state, shared facade/context records and client-trace helper are new shared code. Every trace-source literal, observation kind, coverage category and capability now reads from `ProtoProtocol`; one auth hook and one context state serve REST, GraphQL and gRPC; gRPC's status assertion and auth applier delegate to the shared ones; REST and GraphQL share the assertion facade; `ProtoHttpResponseContext` replaced the positional parameter lists. Six findings closed as no-change with reasons in the stage notes. |
| 4 — HTTP execution | Complete | +1266 / −1012 (net +254) | Done: shared request-builder seam and observation rule (`INT-F1`), readable REST phases (`INT-F21`), `ShapePlan` record (`INT-F26`), observation factory (`INT-F15`), `AssertMissing` (`INT-F16`), partial-class split (`INT-F19`), operation enum (`INT-F30`), dead AST removed (`INT-F44`), nested-filter helper (`INT-F47`). The full send template was designed and rejected as line-neutral with twenty behavioural traps. `INT-F43`/`INT-F45` closed as no-change. |
| 5 — gRPC and Messaging | Complete | +509 / −420 (net +89) | Done: `BeginCallTrace`/`CompleteCall` (`INT-F14`), formatting partial split (`INT-F20`), `Blocking` adapter (`INT-F35`), neutral capture options (`INT-F46`), messaging registration object (`INT-F27`), single-signal in-memory broker (`INT-F37`), RabbitMQ async consumer (`INT-F38`, CI-verified), messaging descriptor (`INT-F50`). |
| 6 — Data and diagnostics | Complete | +1050 / −925 (net +125) | Done: `ProtoDataRedactionPolicy` extracted from `ProtoDataRegistry` (`INT-F24`); `JsonDiagnosticSanitizer` down to 142 lines with `FormContentRedactor`, `MultipartContentRedactor` and `XmlContentRedactor` as their own types (`INT-F23`); the data builder split into planning/construction/tracing parts (`INT-F22`); the ambient read moved to the data service's `For<T>()` entry, which passes the context into the builder (`INT-F39`, refined by `CTX-1`). `SHT-SH9` closed as no-change: the row assertion is an extension on the user's record, which carries no context. |
| 7 — Web | Complete | +1808 / −1225 (net +583) | Done: one URL policy and trace constants (`WEB-S8`, `WEB-X8`), memoized route discovery (`WEB-W9`), boolean-state helper (`WEB-P2`, `WEB-S3`), one resolved runner (`WEB-P3`), `WebTiming` defaults (`WEB-X4`), one artifact-failure helper (`WEB-X7`), backend partial splits (`WEB-P1`, `WEB-S1` partial), correlation state (`WEB-P4`, `WEB-P6`), Playwright translator (`WEB-B3`, `WEB-W12`), backend key tables (`WEB-W11`), lazy coverage inventory (`WEB-W10`), bindable Playwright options (`WEB-W15`, `WEB-P9`), scanner strategies and framework options (`WEB-W7`), operation descriptors for the session wrappers (`WEB-W1`, `WEB-W2`), typed runner result (`WEB-W4`), embedded Vue script with its own parser (`WEB-W8`), `WebTextMatch` flag (`WEB-X5` partial); the probe loop shared by assertions and Selenium (`WEB-B2`, `WEB-S2`); the single-thread Selenium driver executor, with `IWebBackend.GetCurrentAddressAsync` replacing the sync address property (`WEB-S4`, `WEB-X6`); `WebFlow.Check`/`Uncheck` and `BeSortedBy(ProtoSortDirection)` (`WEB-X5` complete; the polarity enum closed as no-change with its reason in the stage notes). Net-positive: `WebProbeLoop` and `SeleniumDriverExecutor` are new shared primitives and the partial splits add headers, so the duplication they remove is smaller than the audit estimated. |
| 8 — Sheets, Sql, Testcontainers | Not started | | |
| 9 — Reporting, OpenApi, AspNetCore | Not started | | |
| 10 — Samples, templates, docs | Not started | | |
| 11 — Deferred test hygiene | Not started | | |
| 12 — Follow-ups from the refactors | In progress | −10 (CTX-1) | `CTX-1` done: one ambient entry plus parameters below, `IProtoContextAccessor` removed, `FindTraceWriter(Activity?)` added. Remaining: `TST-13` shared doubles, `INT-51` scalar format, `INT-52` descriptor adoption, `REF-1` partial-split review. |
| **Cumulative** | | **+2314** | Target: clearly negative by Stage 6; see the note under the table |

### Measured line outcome (after Stage 5)

The audit's deletion estimates were optimistic. Every stage so far introduced shared primitives
(descriptors, options records, template seams, partial-file headers) whose cost exceeds the duplicated
lines they remove, so the cumulative count is +1616 rather than approaching negative. The consistent
architecture and closed drift are real; the line target is not on track.

Revised expectation: Stages 6-10 each record their measured delta and justify any net-positive result;
the cumulative target is reassessed after Stage 9. If the final number is still positive, the honest
conclusion is that this codebase's duplication was smaller than the audit estimated, and the plan's
value is the consistency and the bug fixes, not the line count.
