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

- `ProtoProtocol` descriptor: name, trace source, observation kinds, capability label, coverage
  category, consumed by registration, coverage and tracing (`INT-F3`, `INT-F9`, `INT-F32`).
- `AddProtocol` helper and shared registration idiom (`INT-F17`).
- Shared client registration/initializer base for HTTP and gRPC (`INT-F8`); one transport resolver
  (`INT-F13`); one application resolver (`INT-F48`).
- Shared per-protocol context state (`INT-F6`); shared HTTP/gRPC auth applier (`INT-F7`).
- gRPC builder inherits the protocol builder (`INT-F8`).
- Assertions: generic facade base (`INT-F5`); instance-based gRPC/messaging assertions (`INT-F29`);
  one status assertion (`INT-F40`); `ProtoHttpResponseContext` (`INT-F41`, `INT-F42`).
- Coverage collectors consume the descriptor (`INT-F9`).

Expected: net-negative.

## Stage 4 — HTTP execution (REST and GraphQL)

- `ProtoHttpRequestBuilder<TResponse,TBuilder>` template base; deletes the duplicated auth/header/
  failure code and the 230-line `SendAsync` (`INT-F1`, `INT-F21`, `INT-F4`).
- GraphQL: operation-type enum (`INT-F30`); request-plan type instead of `_simple*` hidden state
  (`INT-F26`); observation factory (`INT-F15`); shape assertion through `ProtoShapeAssertion`
  (`INT-F16`); drop the dead parsed document (`INT-F44`); builder split (`INT-F19`); filter builder
  core (`INT-F47`).
- `ProtoHttpExchange` single decode (`INT-F45`); `ReadAsAnonymous` honesty (`INT-F43`).

Expected: net-negative, the largest single reduction in the plan.

## Stage 5 — gRPC and Messaging

- `ExecuteCallAsync` template and message formatter split (`INT-F14`, `INT-F20`); `Blocking` adapter
  (`INT-F35`); protocol-neutral attachment options (`INT-F46`).
- Messaging registration object, no closure/descriptor introspection (`INT-F27`); in-memory broker
  over channels (`INT-F37`); RabbitMQ async consumer instead of polling (`INT-F38`); shared naming
  constants (`INT-F50`).

Expected: net-negative.

## Stage 6 — Data and diagnostics

- `ProtoDataObjectBuilder` split into planner and resolver (`INT-F22`); redaction policy extracted
  from `ProtoDataRegistry` (`INT-F24`); `JsonDiagnosticSanitizer` redactor strategies (`INT-F23`);
  remove `Proto.Context` from builders and Sheets assertions (`INT-F39`, `SHT-SH9`).

Expected: net-negative.

## Stage 7 — Web

- **7a behavior:** shared actionability/poll engine, Selenium adopts it (`WEB-S2`, `WEB-B2`);
  honest Selenium driver executor (`WEB-S4`, `WEB-X6`); per-operation correlation state
  (`WEB-P4`, `WEB-P6`); one URL sanitizer (`WEB-S8`); route-discovery race (`WEB-W9`).
- **7b dispatch/translation:** operation descriptor table (`WEB-W1`, `WEB-W2`, `WEB-W4`, `WEB-B1`);
  translator contract plus `PlaywrightLocatorTranslator` (`WEB-B3`, `WEB-W11`, `WEB-W12`).
- **7c structure/options:** Playwright and Selenium class splits (`WEB-P1..P3`, `WEB-P7`,
  `WEB-S1`, `WEB-S3`, `WEB-S5..S7`); scanner strategy + DI + framework enum (`WEB-W7`, `WEB-W8`,
  `WEB-W10`); options without leaked Playwright types (`WEB-W15`, `WEB-P9`); enums for behavior
  flags (`WEB-X5`); trace constants and one artifact-failure event (`WEB-X7`, `WEB-X8`).

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

Expected: net-negative.

## Progress

Record `git diff --shortstat` for the stage's commits after the suite is green.

| Stage | Status | Net lines | Notes |
| --- | --- | --- | --- |
| 0 — Guardrails and test hygiene | Complete | +5314 / −4843 (net +471) | Net-positive by design: the guardrails and the shared support are the investment every later stage spends. Done: analyzers + `EnforceCodeStyleInBuild` + `TreatWarningsAsErrors`, `.editorconfig` + format gate (`eng/lint.ps1`), central package management, `global.json`, `.gitattributes`, CI runs the full suite on Windows; `tests/ProtoTest.TestSupport`; 34 `TestMethod()` reflection helpers and 81 `GetCurrentMethod()` casts replaced by `TestMethods.Placeholder`; sink fakes deduplicated; `ProbeTestService` replaces five copies; `SheetsTests` disposes its hosts; the giant fixtures are partial classes split by concern; gRPC cancellation fix with a regression test. Deferred to Stage 11: `AssemblyInfo` parallelization (changing the semantics is behavioural and belongs in its own pass), `AdapterContract`/`AdapterTestSupport` merge and the naming/AAA convention (cosmetic), the intentional RabbitMQ slow-predicate sleep. |
| 1 — Core primitives | Complete | +915 / −403 (net +512) | Net-positive by design: the primitives every later stage spends. Done: `ProtoRunStateMachine`; `ProtoFlow` with named steps, timeout, retry and fail-fast/collect plus `ProtoTraceScope.RunAsync` adoption; `ProtoPolling` (WebPolling deleted); `JsonScalarTypes`, `ProtoJsonDefaults`, `ProtoJsonPropertyProjection`, `JsonDiagnosticSanitizer.LooksLikeJson`; `ProtoAttachmentCapture`; `ProtoRegistrationGuard`; `ProtoClientOwnership`. `ProtoProtocol` moved to Stage 3 where its readers land. |
| 2 — Lifecycle and adapters | Complete | +307 / −137 (net +170) | Net-positive by design: `ProtoTestScope`, `ProtoTestAsync` and the attachment publisher base are new Core primitives, plus two tests pinning the shared policy. The five adapters now share one teardown policy (fixes MSTest replacing the reported result), one publisher shape, one sync bridge and one xUnit v3 before/after; the `lifecycleStarted` guards and per-adapter completion try/catch are gone. `ADP-1` and `ADP-7` moved to Stage 11 as decisions. |
| 3 — Integration contract | Not started | | |
| 4 — HTTP execution | Not started | | |
| 5 — gRPC and Messaging | Not started | | |
| 6 — Data and diagnostics | Not started | | |
| 7 — Web | Not started | | |
| 8 — Sheets, Sql, Testcontainers | Not started | | |
| 9 — Reporting, OpenApi, AspNetCore | Not started | | |
| 10 — Samples, templates, docs | Not started | | |
| 11 — Deferred test hygiene | Not started | | |
| **Cumulative** | | **+1153** | Target: clearly negative by Stage 6 |
