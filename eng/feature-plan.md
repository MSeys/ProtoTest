# ProtoTest Feature Plan — the brief's new capabilities

Source: `prototest-complete-brief.md` (2026-09-24, independent review + ecosystem + strategy).
Baseline: `09d2590` (same as `eng/audit-plan-3.md`).
Relationship: this plan covers **new capabilities**. `eng/audit-plan-3.md` is closed (2026-09-24) and
must land first where it is a dependency; `eng/plan-4.md` executes the brief's companion recommendations
(the evaluator site, then the OpenCSMS reference demo) and reorders the items below demand-pulled.
Feature IDs here (`A1`, `B2`, …) are feature IDs, distinct from the finding IDs in the audit plan.

**Goal:** ship the brief's missing capabilities on the model that exists — extension points first, one new
Core seam only (per-test substitution), and nothing advertised before it is real.

## Rules

1. **Never advertise what does not exist.** Docs, site and READMEs describe only shipped behavior; the
   `Limits` section is mandatory for every integration.
2. Public surface is additive in 1.x; accidental-public plumbing may change with a changelog entry.
3. Every feature ships as: implementation + failure-path tests + trace conventions documented +
   viewer/grouping verified + README and docs page with Limits + demo-suite coverage + changelog entry.
   Container-backed features ship their Testcontainers package.
4. Extension points first: a feature becomes a package unless it genuinely needs a Core concept.
5. One new Core seam is allowed by the audit: per-test service substitution. Everything else adapts to
   the existing client/resource/collector/hook model.
6. No feature starts before its dependency gate below; do not let breadth outrun the audit.

## Dependencies on `eng/audit-plan-3.md`

| Feature work | Needs from the audit plan |
| --- | --- |
| Tracks A–C, F | Stage 1 (evidence boundary) and Stage 2 (lifecycle) complete |
| Track B (substitution), Track C (bridges), Track F (devices) | Stage 3 (trace identity/vocabulary) complete |
| Track D (agentic) can start in parallel | Stage 3 complete (trace wire contents frozen) — the reader consumes the frozen contract |
| Benchmarks and any scale claims | Stage 6 (1–2k measurement) |
| Track B low-ceremony mode | Stage 4 (runner boundaries) complete |

## Execution order (set by `eng/plan-4.md`)

`eng/plan-4.md` executes the website recommendations first, then the reference demo (OpenCSMS), and the
demo pulls these items ahead of the rest in this order: background-worker hosting (**C5**) and readiness
waiting (**C6**), then the test clock (**A8**) and `ProtoTest.Devices.WebSocket` (**F2**, reordered),
then WireMock (**C1**), per-test substitution (**B1**) and the Aspire adapter (**C3**), then the demo-app
benchmark (**A7**, re-run by plan-4 P8). Everything else keeps its track and waits for demand; the
checkboxes below stay the source of truth for scope, and `eng/plan-4.md` is the source of truth for order.

## Track A — 1.1 polish and trust (small, user-visible)

- [x] **A1 Path-based single-value read** — `ReadAsJson<int>("$.id")` on responses, so one value does not
  need a wrapper record. Add to `ProtoTest.Http`/`Rest`/`GraphQL` where the response type already reads
  JSON. Tests: missing path, wrong type, JSON null, numeric precision. Effort: days. **Done in plan-5
  Phase 3b DX-05** (REST `ReadAsJson<T>(path)`, GraphQL `ReadDataAs<T>(path)`, shared `JsonPathResolver`
  subset `$`/dots/`[n]`; `ReadRequired<T>(path)` composes with it).
- [x] **A2 Clock sweep** (brief item): resolved by plan-4 P3 - no `IClock` or `TimeProvider` reference
  existed in the samples to migrate, and the framework now provides a test clock for the code that
  needs one.
- [ ] **A3 Failure-mode tests the brief asked for** (open questions 4): application throws during startup;
  a container dies mid-run. These are tests and documentation, not features; add them to the reliability
  suites and fix only what they prove broken.
- [ ] **A4 Cross-run correlation** (open question 6): record CI build/run metadata (environment variables
  such as `GITHUB_RUN_ID`, an explicit option) on the run wire so shared-environment results can be
  attributed. Additive wire content; documented. Effort: 1–2 days.
- [ ] **A5 Analyzers package** (`ProtoTest.Analyzers`) — design first: the useful rules are the
  intent-dependent ones (a `[ProtoTest]` suite with a plain `[Test]` next to it, `context.Rest()`/`Web()`
  used in a test with no application/client registered, a `[RequiresCapability]` kind that no registration
  contributes). Scope conservatively; a noisy analyzer is worse than none. Effort: 1–2 weeks after a
  one-page design.
- [x] **A6 Compatibility and deprecation policy** (brief item 22): done in `eng/plan-4.md` W3 - the
  stated 1.x policy (semver, the accidental-public exception list, `Obsolete` at least one minor before
  removal where feasible, fixes on the newest line) lives on the support and sustainability page.
- [x] **A7 Benchmark page** (brief item 17 / open question 2): done in `eng/plan-4.md` W6 - the
  benchmarks page carries the trace-size table and the `WebApplicationFactory` overhead comparison
  (per-test lifecycle with tracing on/off, startup, allocations). Re-run on the reference demo by P8.
- [x] **A8 Test clock** (plan-4 P3): done - `ProtoClock` in Core, `ConfigureClock(seed)`, per-test
  `Proto.Context.Clock` with `Advance`/`SetUtcNow`, automatic `TimeProvider` injection into in-process
  applications (linked per request) and worker hosts, run clock through `ProtoHost.CurrentHost.Clock`,
  and `clock.advance` trace evidence. A2 is resolved with it: no `IClock`/`TimeProvider` reference
  existed in the samples to migrate.

## Track B — Alba-parity release (1.2)

- [ ] **B1 Per-test service substitution and fault injection** (the brief's #1 functional gap; Audit 2 E4
  deferred the seam deliberately). Design constraints: the test scope already exists; the
  per-run/per-test ASP.NET Core server lifetime strategies own server creation; substitution must apply
  before the server is built, reset per test, be parallel-safe, and be traced
  (`service.substitute`/`service.fail` operations, server state on the entity). API shape:
  `context.Override<TService>(…)` and `[ReplaceService<TService>]`; `[FailDependency<TService>]` for
  error paths. Acceptance: an override is visible to the application under test; a per-run server does not
  leak one test's override into the next; a `RequiresCapability`-style skip applies when the application
  is not in process. Limits: closed-box harnesses (Aspire) cannot be substituted.
- [ ] **B2 Expected shape in the call and an exact mode** — REST/HTTP gains GraphQL's
  `.PostAsync(…).ExpectAsync(new { … })`; `.ShouldMatchShape(…, exact: true)`/Verify-style exhaustive mode
  is one decision shared with Track E2 (pick one implementation).
- [ ] **B3 `Should` vocabulary parity** (brief item 2): content type, headers, cookies, redirect location.
  Additive facade members; keep naming consistent with the existing assertion facades.
- [ ] **B4 Low-ceremony mode** (brief item 3): opt-in auto-wrap of plain `[Test]`/`[Fact]` per adapter;
  `[Application]` inference when exactly one is registered; `ProtoTestHost.For<Program>()` one-liner.
  Feasibility must be verified per runner (outcome detection, async flow, skip semantics) before any
  default changes; existing `[ProtoTest]` behavior is untouched.
- [ ] **B5 Built-in test user** (brief item 4): move the sample's `[SignedInAs]` into the shipped
  AspNetCore/HTTP surface with claims/roles, traced as an auth entity like the existing auth lifecycle.

## Track C — Bridges (1.x platform)

- [ ] **C1 WireMock.Net integration** (brief item 8): a package that starts/stops a fake HTTP service per
  test (or per run, opt-in), exposes stubbing that reads like the scenario, records matched requests in
  the trace with the same observation shapes as HTTP responses, and contributes coverage. Ownership of
  the server must be explicit; a Testcontainers variant is optional. Decision needed: package name and
  whether it depends on `ProtoTest.Rest` observation types or a new Core shape contract (the audit's D4
  decision feeds this).
- [ ] **C2 MassTransit bridge** (brief item 12): wrap `ITestHarness` as a capability and expose the
  existing messaging client surface (`PublishAsync`/`AwaitAsync`) over it, with the same trace vocabulary.
  The bridge is the model: adapter wraps an existing harness rather than reimplementing it.
- [ ] **C3 Aspire adapter** (brief item 25): `ProtoTest.Aspire`, run-scoped AppHost registered through
  `AddInfrastructure`, resources exposed as application targets, per-test contexts and trace on top.
  Pin the Aspire version; target `net10.0` only; document the closed-box limits (no DI substitution, no
  in-process assertions).
- [ ] **C4 Wolverine.Tracking bridge** (optional, ecosystem adjacency to Alba): same shape as C2; only if
  a user asks.
- [x] **C5 Background-worker hosting** (plan-4 P1): done - `ProtoTest.Hosting` with
  `AddWorkerHost<TProgram>()`, run-scoped lifecycle after the infrastructure registered before it,
  run settings and suite options into the worker's configuration, `context.Host<TProgram>()` /
  `HostService<TProgram, TService>()` accessors, `worker` trace entity and capability, and a docs page
  with limits. The demo's R1 proves the acceptance criterion (its billing worker runs in-process).
- [x] **C6 Readiness waiting** (plan-4 P2): done - `AddReadinessProbe` awaited at run start with
  timeout, interval and trace evidence; `ProtoReadiness.Tcp`/`.Http`; `AddHttpReadiness(application)`
  for published addresses; `ProtoContainerResource.ReadyWhen`/`ReadyOn(port)` with the PostgreSQL and
  RabbitMQ containers probing their standard ports. Acceptance for the demo (no `Task.Delay` in setup)
  is proven by R1's setup.

## Track D — Agentic evidence layer (highest strategic leverage; parallel after Stage 3)

- [x] **D1 `ProtoTest.Traces` reader library** — done in `eng/audit-plan-3.md` Stage 8: reads
  `.prototrace` (manifest, spans) with no dependency on `ProtoTest.Core`, plus `ProtoTraceSummaryText`.
  The `ProtoTest.Cli` tool ships `prototest trace summary` on top of it.
- [ ] **D2 `ProtoTest.Mcp` .NET tool**: transport-agnostic tools library + a stdio host (the reader is
  ready). Tools in priority order: `run_tests`, `get_failure`, `get_coverage`, `get_trace`, `get_state`,
  `compare_runs`, `list_runs`; resources (`prototest://runs/{id}/…`) and prompts ("diagnose this
  failure", "fill these coverage gaps"). Token discipline is acceptance criteria: summaries first,
  selectors, paging, hard caps; never dump a full trace by default.
- [ ] **D3 Demo-only hosted MCP** (read-only over the fixed demo trace): free-tier host, no accounts, no
  retention. Ship only after D1/D2 are usable; roughly a day.
- [ ] **D4 GitHub Action**: runs the suite, posts the failure digest and trace link on the PR, uploads
  the trace as an artifact. Depends on D1.
- [ ] **D5 "Using ProtoTest with coding agents" docs page and skills package** — published only when
  D2–D4 exist (the never-advertise rule); the hero line is the brief's when true.
- [ ] **D6 Configuration and discovery**: `.mcp.json` args (`--project`), env var, auto-discovery of the
  newest trace.

Gates: D1/D2 dogfooded on the demo suite; record the session; external launch post only after that.

## Track E — Coverage and reporting depth (demand-gated)

- [ ] **E1 Traffic coverage** (brief item 9): mark fields that arrived in a response as *observed but
  unasserted*, in a separate report section. Design must protect the assertion-level thesis — observed is
  never counted as covered. Opt-in.
- [ ] **E2 Exhaustive/Verify-style mode** (brief item 10): one implementation shared with B2, not two.
- [ ] **E3 Allure/ReportPortal sinks** (brief's comparables table): community-owned, built on
  `IProtoSink`; the integration contract and template (Track G) are prerequisites.
- [ ] **E4 Viewer performance at 1,000+ tests** (open question 5): measure the viewer on the Stage 6
  trace; optimize reader/build if needed.

## Track F — Device/IoT (strategic bet; explicit decision required)

- [x] **F1 Design spike for `ProtoTest.Devices`**: done in `eng/plan-4.md` P4a - typed device
  classes with per-test instances, the transport abstraction backends implement, `device.*` trace
  operations and the `device` entity, protocol-catalog coverage with gaps,
  `ProtoCapabilityKinds.Device` and `[RequiresDevice<TDevice>]`, proven by a five-test slice against
  an in-memory transport. Design: `assets/internal/devices-design.md` (internal). Packing and docs wait
  for P4b's WebSocket backend.
- [x] **F2 WebSocket backend first** (plan-4 P4b): `ProtoTest.Devices.WebSocket` shipped with
  `AddDevices(devices => devices.AddWebSocketClient(...))`, options bound from
  `ProtoTest:Devices:WebSocket`, and tests against a real
  Kestrel WebSocket server. MQTT against a Mosquitto container follows a real MQTT user;
  `[RequiresDevice<T>]` gating is in place.
- [ ] **F3 Whole-journey scenario** as the acceptance demo: device frame → platform processing → command
  back → report, one `.prototrace` (the reference demo's R2 is this scenario).
- [ ] **F4 TCP/Serial/Sigfox** only after a real transport user exists.
- [ ] **F5 Commercial angle** (telematics/regulatory reporting): decide separately from the OSS plan;
  do not gate the package on it.

## Track G — Sustainability mechanics (non-code, needed before contributors arrive)

- [ ] **G1 Integration contract** written down: required options, tracing, redaction, tests, docs with
  Limits, container package when applicable.
- [ ] **G2 Integration template** and package ownership levels (core vs community-maintained).
- [ ] **G3 Abandonment policy** and an integration-support statement; GitHub Discussions.
- [ ] **G4 First external suite in anger**; recruit one co-maintainer from the first heavy users.

## Version mapping

| Version | Theme | Contains | Gate |
| --- | --- | --- | --- |
| 1.1 | Polish and trust | audit-plan-3 (complete) + Track A incl. A8 + `eng/plan-4.md` Track W | demo suite green; overhead numbers published; docs versioned |
| 1.2 | Alba parity and hosting | Track B (B1 first) + C5/C6 worker hosting and readiness + plan-4 R1 | the reference demo runs its billing worker in-process under the suite |
| 1.x | Platform | Track C (C1–C3, C6), Track D, Track F (WebSocket first), plan-4 R2–R5 | one external suite; CI evidence in use |
| Later | Bets | Track E, Track G, plan-4 X1/X2 as demand allows | explicit demand for each |

## Open questions from the brief, mapped

| Brief open question | Where it lands |
| --- | --- |
| Retry trace semantics | Pinned by Audit 2 A8; keep the one-record-per-attempt rule and document it |
| Trace size/memory at 1–2k | audit-plan-3 Stage 6 + A7 |
| Adversarial redaction guarantee | audit-plan-3 Stage 1 |
| Startup throw / container death mid-run | A3 |
| Viewer performance at 1k | E4 |
| Cross-run correlation | A4 |
| Auto-wrap feasibility | B4 |

## Non-goals

- Hosted team MCP server (brief: explicit demand only).
- Device transports beyond MQTT, or any device work before F1 shows a real use.
- Renaming ProtoTest.
- Traffic coverage before assertion coverage is proven in a real suite.
- Code-coverage conflation (docs contrast only).
- Adding architecture: no new managers/providers/factories beyond the one Core seam in B1.

## Launch checklist (per feature)

- [ ] README + docs page with Limits
- [ ] Failure-path tests and trace assertions
- [ ] Trace conventions documented; viewer grouping verified without a viewer release
- [ ] Demo suite covers it (or a documented sample)
- [ ] Changelog entry; compatibility classified
- [ ] Website says nothing that the code does not do
