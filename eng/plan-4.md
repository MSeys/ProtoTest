# ProtoTest Plan 4 — the evaluator site and the reference demo

Sources: `assets/internal/prototest-website-recommendations.md` and
`assets/internal/prototest-reference-demo-spec.md` (2026-09-24 companions to the brief). Carries the two
items `eng/audit-plan-3.md` Stage 8 postponed (8.14 template runner variants, 8.15 static trace index).
Audit 3 is closed; this plan executes the companions in the order they rank themselves, and reorders
`eng/feature-plan.md` where the reference demo changes what is demanded.

**Goal:** first make the site answer evaluators — comparison, objections, support, roadmap — with content
that matches shipped reality; then prove ProtoTest on a product nobody wrote for the framework: one
satellite repository, three run modes, a real regression in the trace, and the framework gaps it exposes
closed in demand order.

## Locked decisions

- The website recommendations' rule 0 is binding: **never advertise what does not exist**. Compare, FAQ
  and roadmap describe shipped behavior only; Aspire, MCP, WireMock, MassTransit and per-test substitution
  stay "exploring" until their packages exist.
- **Website content ships before the demo.** The demo is announced only once the site already tells the
  truth about what it proves.
- The reference demo lives in **its own repository** (`opencsms`, MIT), created when R1 begins — never
  inside this repo, or it will be read as another demo.
- The demo's framework gaps move ahead of the feature plan in **demand-pulled order**: background-worker
  hosting and readiness first (R1 needs them), then TimeProvider and `ProtoTest.Devices.WebSocket` (R2),
  then WireMock, per-test substitution and the Aspire adapter (R4), then the demo-app benchmark (R5).
- **Docs versioning:** freeze a `1.0` snapshot at the 1.1 release (Docusaurus `docs:version`), keep 1.0
  reachable, and let patches update the current set in place. Do not cut versions before 1.1; a single
  current set is acceptable until then because there is no breakage history to consult.
- The demo keeps its warts (spec §2.2): a documented coverage gap, real error paths, retry/dead-letter
  semantics, a clock dependency, seeded volume and a multi-tenancy negative test. No curated "showcase"
  failures.

## Rules

1. Never advertise what does not exist; every Limits section is mandatory and honest, including numbers
   that are worse than the alternative.
2. Every behavior change lands with a test; docs-only work must pass `eng/check-docs.ps1`, code must pass
   `eng/test.ps1` and `eng/lint.ps1`.
3. A stage is the unit of commit and the unit of green; the Progress table records each stage's diff.
4. Decisions are recorded here, not in prose elsewhere; postponed work gets an entry or a decision.
5. The demo repository obeys the same evidence bar: its CI is its reason to exist, and its traces are real.

## Track W — evaluator content (in-repo docs, ships first)

Order is the recommendations' own priority (§2). W1–W5 are independent and small.

- [ ] **W1 Comparison page** `/docs/compare` (rec 2.1): one section per alternative — WAF+Testcontainers+
  Verify, Alba, Aspire, Playwright alone, building in-house — each with "where they win" and "where
  ProtoTest wins", closing with "when ProtoTest is not the right choice". Claims must cite shipped
  packages only; Aspire appears as complementary once P7 exists, "exploring" before.
- [ ] **W2 FAQ** `/docs/faq` (rec 2.2): the nine objections, each answered in a short paragraph with a
  link to the proof (coverage docs, runner matrix, AI-usage page, licensing).
- [ ] **W3 Sustainability page** `/docs/project/sustainability` (rec 2.4): maintainer model, funding
  reality, semver/deprecation/support policy, how to get help, and the "survives its author" argument.
- [ ] **W4 Roadmap page** `/docs/roadmap` (rec 2.5): **Next** (committed, rough timing), **Exploring**
  (Aspire, MCP, WireMock, MassTransit, substitution), **Not planned** (with reasons). Synced with
  `eng/feature-plan.md`, which keeps being the internal source of truth.
- [x] **W5 The viewer as the primary CTA** (rec 2.3): "See a failing test's trace" is the hero's primary
  CTA (with Get started secondary), and the old TraceView illustration became a faithful three-view panel -
  Run, Story and Check, drawn from the bundled demo trace with the real viewer's grammar and real values,
  switchable with the keyboard, theme-aware and asset-free. **Decision:** the 60-second screencast was
  dropped, and real screenshots were tried and dropped too - the replica keeps the viewer's text at full
  size, follows the light/dark theme, ships no raster assets and cannot drift into stale images.
- [x] **W6 Overhead benchmark vs raw `WebApplicationFactory`** (rec 2.6, executes feature-plan A7):
  `tests/ProtoTest.AspNetCore.Tests/OverheadBenchmarkTests.cs` measures the same in-process request through
  a full ProtoTest test cycle (start, REST call, complete) with tracing on and off against the raw
  `WebApplicationFactory` equivalent, with per-phase splits, allocations and suite startup through the
  first completed request; the numbers are published with methodology and the slower-where-it-is-slower
  reading on the [benchmarks page](../docs/docs/benchmarks.md).
- [x] **W7 Versioned docs at 1.1** (rec 2.5): the released 1.0 docs were cut from `main` into
  `versioned_docs/version-1.0` (78 pages, with `versions.json`), `/docs/` now serves 1.0 through
  `lastVersion` while `docs/` carries the 1.1 work under `/docs/next/`, the navbar gained the version
  dropdown and the announcement bar states the split instead of claiming the release matches. One
  mechanical fix: the 1.0 snapshot's repo file links gained one `../` because a versioned page sits one
  directory deeper. Verified: the build is green (broken file links throw), `/docs/`, `/docs/next/`,
  `/docs/next/roadmap` and old paths all serve, and the 1.0 hooks page lacks the 1.1 section while the
  next page has it. At the 1.1 release: `npm run docusaurus docs:version 1.1`, move `lastVersion` and
  refresh the bar.
- [x] **W8 Small wins** (rec 2.7): homepage release feed (`docs/src/data/releases.ts` + `ReleaseFeed`,
  cross-checked against `CHANGELOG.md` by the docs gate), GitHub Discussions enabled and linked from the
  sustainability page, the FAQ, the footer and `SUPPORT.md`, the AI-usage page surfaced from Why
  ProtoTest, and a docs-search check that "integration testing .NET", "trace" and "coverage" land on the
  right pages. The screencast was dropped in W5 (recorded in Decisions).
- [x] **W9 One changelog source** (follow-up): the repository `CHANGELOG.md` is now the only file a
  release edits. `docs/scripts/generate-changelog.mjs` generates the docs changelog page and the homepage
  feed (`docs/src/data/changelog.generated.ts`; `releases.ts` is gone), the docs build runs it, and
  `eng/check-docs.ps1` runs its `--check` mode so a stale generated file fails the docs gate (verified by
  deliberate drift). GitHub Release notes already derive from the same file; the announcement bar stays a
  release-checklist item because it names the docs-version split, not a release.

## Track R — the reference demo (OpenCSMS, separate repository)

Milestones are the spec's (§8); each is stage-sized and leaves the demo runnable.

- [ ] **R0 Repository discipline** — created at `opencsms` on its own git history when R1 starts; README
  states what it is, the three run modes and an honest "what this proves"; MIT; linked from the docs as
  *the reference suite* (never as a sample). One command: `docker compose up` or `dotnet test`.
- [ ] **R1 CSMS API + Postgres + billing worker + REST suite + one journey** (M1, 2–3 weeks; needs P1, P2):
  REST/OpenAPI surface, tariff rules, `AddWorker<Program>`-hosted billing worker consuming RabbitMQ events
  with retry and dead-letter, Testcontainers Postgres/RabbitMQ, first end-to-end journey, and the
  documented coverage gap.
- [ ] **R2 OCPP gateway + charge-point simulator + device journey** (M2, 2 weeks; needs P3, P4): OCPP 1.6J
  core subset (BootNotification, Heartbeat, StatusNotification, Start/StopTransaction, MeterValues,
  RemoteStart/Stop), the simulator built on `ProtoTest.Devices.WebSocket`, the idle-fee journey with
  `Proto.Context.Clock()`, and the duplicate-StopTransaction and malformed-MeterValues error paths.
- [ ] **R3 Dashboard + public page + Playwright journeys + monthly export** (M3, 2 weeks): operator and
  viewer roles, session timeline, invoice view, the .xlsx export through Sheets, and the multi-tenancy
  negative test.
- [ ] **R4 Container topology + deployed mode + mocks + fault injection + nightly CI** (M4, 1–2 weeks;
  needs P5, P6, P7): the same suite against containers (Aspire AppHost) and a staging `BaseUrl` with
  skip conditions, PSP/email/webhook fakes via WireMock, PSP-down fault injection, three CI jobs
  (PR fast, nightly topology, optional staging smoke).
- [ ] **R5 Benchmarks + docs page + trace showpiece + launch post** (M5, 1 week; needs P8): the seeded
  1,000-journey run on this app produces the published overhead numbers; a real regression (idle fee not
  applied after a tariff change) yields the linked failing `.prototrace`; the docs page and the launch
  post only after W1/W4/W5 and P8 exist.

## Track P — framework prerequisites the demo pulls

IDs below are `eng/feature-plan.md` items; the demo is their demand proof, the feature plan is their
scope. Plan-4 fixes their order; done means the feature-plan acceptance criteria.

- [x] **P1 Background-worker hosting** (spec item 1) → feature-plan **C5**: shipped as the
  `ProtoTest.Hosting` package. `AddWorkerHost<TProgram>()` hosts a worker's real entry point once per
  run - started after the infrastructure registered before it, given the run's settings (connection
  strings and settings infrastructure values, plus suite options), recorded as a `worker` run entity
  and capability, and stopped with the run; tests reach it through `Proto.Context.Host<TProgram>()`
  and `HostService<TProgram, TService>()`. One small Core seam was added
  (`IProtoConfiguredInfrastructure`); the host factory is Microsoft's vendored, MIT-licensed
  `HostFactoryResolver`. 9 tests cover start/stop, settings and precedence, start and stop failures,
  duplicate registration, ambiguity and the missing-worker messages. Readiness waiting is P2.
- [x] **P2 Readiness waiting** (spec item 2) → feature-plan **C6**: readiness lives in Core. One
  `ProtoReadinessOptions` (`Timeout` 30 s, `Interval` 100 ms) governs `AddReadinessProbe(name, check)`,
  which registers a run-scoped piece awaited at its registration position, records attempts and wait as
  a `readiness` run entity, and fails the run naming the probe, attempts and last error.
  `ProtoReadiness.Tcp`/`.Http` are the common checks; `AddHttpReadiness(application)` waits for a
  published `BaseUrl` (settings first, then configuration) and skips in-process applications with
  evidence. `ProtoContainerResource` owns `ReadyWhen`/`ReadyWhenTcp`/`ReadyOn(port)`; the PostgreSQL
  and RabbitMQ containers wait for their standard ports. `IProtoConfiguredInfrastructure` now carries
  the suite's configuration too, so workers read it and probes can resolve published addresses. 6 Core
  readiness tests plus container-base tests (including a real TCP listener proving `ReadyOn` override)
  and a worker configuration-precedence test; full suite green.
- [x] **P3 Clock control** (spec item 5) → feature-plan **A8**: `ProtoClock : TimeProvider` in Core -
  `ConfigureClock(new ProtoClock(seed))` seeds the run; each test gets its own clock seeded from the
  run, so `Proto.Context.Clock.Advance(...)`/`SetUtcNow(...)` never leaks across parallel tests.
  In-process ASP.NET Core applications receive the clock as their `TimeProvider`, linked per request by
  the test-id header the client already carries (the pipeline does not inherit the test's ambient
  context), and worker hosts receive it too (background flows fall back to the run clock, which
  `ProtoHost.CurrentHost.Clock` advances). Every move records a `clock.advance` event and updates a
  `clock` entity; 3 Core clock tests, 2 host-level trace/isolation tests and an application test prove
  it. A2 is resolved: no `IClock` or `TimeProvider` existed in the samples to migrate.
- [x] **P4a `ProtoTest.Devices` (feature-plan F1)** (spec item 4, first half): shipped as an internal
  spike (`IsPackable=false`, no README/docs/changelog until P4b has a real transport). The model:
  a suite derives `ProtoDevice` classes and gets one instance per (type, id) per test through
  `Proto.Context.Devices().For<TDevice>("id")`; the protected primitives (`ConnectAsync`, `SendAsync`,
  `ReceiveAsync`, `ExpectAsync`) record `device.connect/send/receive/command` operations and a `device`
  entity (transport, address, connected). Transports implement `IProtoDeviceTransport`; address and
  transport are overrideable per environment through `ProtoTest:Devices:{id}`. A protocol catalog
  (`IProtoDeviceProtocol`) drives `DeviceCoverageCollector`: asserted kinds count, catalog kinds no test
  asserted report as gaps. `ProtoCapabilityKinds.Device` and `[RequiresDevice<TDevice>]` let a suite
  skip what the environment cannot provide. 5 tests prove the slice against an in-memory transport:
  per-test instances, configuration override, trace entity and operations, a timeout failure carrying
  the description and frames, coverage gaps, and the capability skip. Replay (`device.replay`) is
  designed but deferred.
- [x] **P4b `ProtoTest.Devices.WebSocket` (feature-plan F2)** (spec item 4, second half): the real
  WebSocket backend shipped and the pair became public (`ProtoTest.Devices` flipped to `IsPackable`,
  both with READMEs, a docs page with limits and changelog entries). `AddWebSocketDevices(configure)`
  follows the `AddWeb` shape: it registers the transport, contributes the `WebSocket` device
  capability and inserts the code defaults as the first configuration source;
  `WebSocketDeviceOptions : IProtoConfigurableOptions` binds `ProtoTest:Devices:WebSocket` over them
  and validates. Endpoint overrides moved to `ProtoTest:Devices:Endpoints:{deviceId}` via
  `ProtoDeviceEndpoints`, mirroring `ProtoApplication`. Text and binary frames round-trip, close
  frames end the exchange with a clear error, and connect failures name the address. 4 tests run
  against a real Kestrel WebSocket server; full suite, format gate, docs check, docs build and pack
  green. MQTT remains demand-gated.
- [ ] **P5 WireMock integration** (spec item 6 prerequisite) → feature-plan **C1**.
- [ ] **P6 Per-test substitution and fault injection** (spec item 6) → feature-plan **B1**.
- [ ] **P7 Aspire adapter** (spec item 3) → feature-plan **C3**.
- [ ] **P8 Demo-app benchmark** (spec item 7) → feature-plan **A7** (W6 publishes the first half from the
  harness; P8 re-runs it on OpenCSMS).

## Track X — carried from audit 3

- [ ] **X1 Template `--runner` variants** (audit-3 8.14): `dotnet new prototest --runner xunit|tunit|mstest`;
  depends on feature-plan B4/B5 for what the generated suite should look like, so it lands with or after
  them.
- [ ] **X2 Static trace index for sharing** (audit-3 8.15): a folder of traces plus a generated static
  index page, no server; feeds W5's showpiece and R5.

Feature-plan **D2 (MCP)** and its dependent docs page stay in the feature plan; the agents page is
published only when D2–D4 exist (never-advertise rule). The recommendations' §3 wording is the gate.

## Progress

Record `git diff --shortstat` per stage split by `src`, `tests`, `docs`, with this file excluded.

| Stage | Status | Net lines | Notes |
| --- | --- | --- | --- |
| — (planning) | Complete | docs +0 | Audit 3 closed; companions stored under `assets/internal/`; plan 4 written; feature plan reordered demand-pulled. |
| W1 — Comparison page | Complete | docs +114 / −0 · src 0 · tests 0 | `/docs/compare` published: five alternatives (WAF+Testcontainers+Verify+Shouldly, Alba, Aspire, Playwright alone, in-house), each with where they win and where ProtoTest wins, closing with when ProtoTest is not the right choice. Claims limited to shipped packages; the service-substitution gap and the unshipped Aspire adapter are stated. Verified in the built site: `check-docs`, `npm run typecheck` and `npm run build` green. |
| W2 — Objections FAQ | Complete | docs +67 / −1 · src 0 · tests 0 | `/docs/faq` published: the nine recommended objections plus licensing, telemetry, target runtimes and where-to-start, each answer linking its proof (coverage, runners, clients, AI usage, nothing-phones-home). Benchmarks moved to `sidebar_position: 4` so FAQ sits beside the comparison page. Verified in the built site: `check-docs` (99 files) and `npm run build` green, anchors checked against the built HTML. |
| W3 — Sustainability page | Complete | docs +50 / −0 · src 0 · tests 0 | `/docs/project/sustainability` published with the user's answers: no funding and personal time; semver with the accidental-public exception list, `Obsolete` at least one minor before removal where feasible, patch releases never break, the pack-time package-validation gate named as enforcement; fixes on the newest line only; help via docs, issues and SECURITY.md; the survives-its-author argument; and the solo-project caveats. This also executes feature-plan A6 (compatibility and deprecation policy). Verified in the built site: `check-docs` (100 files) and `npm run build` green. |
| W4 — Roadmap page | Complete | docs +64 / −0 · src 0 · tests 0 | `/docs/roadmap` published with three sections: Next (1.1 polish and trust, 1.2 hosting and the reference demo, 1.x platform line), Exploring (MCP, MassTransit, Wolverine, analyzers, traffic coverage, exhaustive mode, Allure/ReportPortal, static trace index, MQTT) and Not planned (hosted service, commercial CSMS, pre-.NET 8, reimplementing the libraries, coverage conflation, renaming, further device transports) - each with its reason. Effort-ordered, explicitly not date-ordered; unshipped items phrased as plans. Verified in the built site: `check-docs` (101 files) and `npm run build` green, page rendered and read. |
| W5 — Viewer CTA and demo panel | Complete | docs +690 / −242 · src 0 · tests 0 | The hero's primary CTA is now "See a failing test's trace" (opens trace.prototest.dev/?demo=1), Get started is secondary, and the stale TraceView card is replaced by `ViewerWalkthrough`: Run (outcome line, phase bar, "what this run could see", needs-attention list), Story (failure card and lifecycle) and Check (exception, recorded location, source), switchable by click and arrow keys, with every value copied from the bundled demo trace. Verified in the built site at desktop/mobile in both themes: `check-docs` (104 files), `npm run typecheck`, stylelint and the Docusaurus build green. Screenshots and the screencast were dropped (recorded in Decisions). |
| W6 — Overhead benchmark | Complete | docs +62 / −1 · tests +545 / −0 · src 0 | `OverheadBenchmarkTests` (Category `Benchmark`) leads with a real short test - POST an order, read it back, assert both bodies - written once with ProtoTest (tracing on/off) and once with the raw stack, and keeps the micro comparison that explains it: single request, lifecycle-only mode, suite startup through the first completed request. `TestApi` gained a `/benchmark/orders` create/read pair so both versions send identical requests. Published on the benchmarks page: the same short test is ~3x the raw stack without tracing and ~8x with it; the bare lifecycle is 0.06 ms, the REST client adds ~0.15 ms over a raw request and tracing adds ~1.6 ms and ~1 MB per test. Closes feature-plan A7. Full suite, format gate and docs check green. |
| W8 — Small wins | Complete | docs +105 / −2 · eng +61 / −0 · src 0 · tests 0 | The homepage gains a release feed (`releases.ts` + `ReleaseFeed`) showing the latest version, date and summary plus the release before it; `eng/check-docs.ps1` cross-checks versions and dates against `CHANGELOG.md` and the drift was verified to fail the gate. The AI-usage page is surfaced from Why ProtoTest; the docs search was checked for "integration testing .NET" (64 hits, intro and integrations first), "trace" (100) and "coverage" (98) and lands on the right pages. GitHub Discussions is enabled and linked from the sustainability page, the FAQ, the footer and `SUPPORT.md`; the screencast was dropped in W5. |
| W7 — Versioned docs | Complete | docs +9,106 / −1 · src 0 · tests 0 | The released 1.0 documentation is frozen as `versioned_docs/version-1.0` (78 pages cut from `main`) with `versions.json`; `lastVersion: '1.0'` serves it at `/docs/`, `docs/` moves to `/docs/next/` as "1.1 (in progress)", the navbar shows the version dropdown and the announcement bar points at 1.0 and the 1.1 roadmap. The 1.0 snapshot's repo file links gained one `../` (a versioned page is one directory deeper); everything else is untouched. Verified by the throwing build and by serving `/docs/`, `/docs/next/`, `/docs/next/roadmap` and old paths, and by content checks that split the versions. |
| W9 — One changelog source | Complete | docs +281 / −44 · eng +23 / −36 · src 0 · tests 0 | `docs/scripts/generate-changelog.mjs` parses the repository `CHANGELOG.md` into the changelog page and `changelog.generated.ts` (summary = the release's opening paragraph, or its first sentence as a bullet; `Unreleased` and the link-reference tail stay in the repository file). `Releases.ts` is deleted, `ReleaseFeed` imports the generated data, the docs build and start run the generator, and `check-docs` runs `--check` with deliberate drift verified to fail the gate. The page now carries the repository wording and gains `Fixed`/`Added`/`Changed` navigation; `RELEASING.md` says one edit per release. |
| P1 — Background-worker hosting | Complete | src +856 / −1 · tests +425 / −0 · docs +73 / −0 | New `ProtoTest.Hosting` package (net8/9/10, packed, README, docs page with limits): `AddWorkerHost<TProgram>()` resolves the worker's own program through Microsoft's vendored `HostFactoryResolver` (MIT, attributed), builds and starts its `IHost` once per run after the infrastructure registered before it, feeds it the run's settings plus `ProtoWorkerOptions` (options win), records a `worker` entity and capability, and stops/disposes it with the run; `context.Host<TProgram>()` and `HostService<TProgram, TService>()` reach it from tests, with clear errors for missing/ambiguous workers. One additive Core seam (`IProtoConfiguredInfrastructure`) passes the run's settings to infrastructure at start; `ProtoCapabilityKinds.Worker` is the new kind. 9 tests cover lifecycle, settings precedence, start/stop failures, duplicate registration, ambiguity and diagnostics. Full suite, format gate, docs check, docs build and pack green. |
| P2 — Readiness waiting | Complete | src +561 / −25 · tests +379 / −3 · docs +49 / −6 | Readiness in Core: `IProtoReadinessProbe` + `ProtoReadinessOptions` (30 s / 100 ms), `AddReadinessProbe(name, check)` awaited at its registration position with attempts and wait recorded as a `readiness` run entity, and a timeout failure naming the probe, attempts and last error; `ProtoReadiness.Tcp`/`.Http` helpers; `AddHttpReadiness(application)` waits for a published `BaseUrl` (settings first) and skips in-process applications with evidence. `ProtoContainerResource` owns `ReadyWhen`/`ReadyWhenTcp`/`ReadyOn(port)`; the PostgreSQL and RabbitMQ containers probe their standard ports (5432/5672). `IProtoConfiguredInfrastructure` now takes a `ProtoInfrastructureContext` (settings + suite configuration) with a default plain `StartAsync`, which also gives P1's worker the suite configuration in its precedence chain. 6 Core readiness tests, 4 container-base tests (including a real TCP listener proving the `ReadyOn` override) and the updated worker precedence test. Full suite, format gate, docs check, docs build and pack green. |
| P3 — Clock control | Complete | src +366 / −14 · tests +214 / −3 · docs +65 / −0 | `ProtoClock` in Core with `ConfigureClock(seed)`: each test gets its own clock seeded from the run's, exposed as `Proto.Context.Clock` (`Advance`/`SetUtcNow`), so advancing time never leaks into parallel tests. In-process ASP.NET Core applications receive it as their `TimeProvider`; since the pipeline does not inherit the test's ambient context, the client's test-id header plus an injected startup filter push the clock for the request's duration (verified by a test that failed before the fix). Workers receive the clock too, falling back to the run clock on background flows (`ProtoHost.CurrentHost.Clock` advances run time). Moves record a `clock` entity and a `clock.advance` event with delta and instants; the entity appears only once time moves. 3 clock unit tests, 2 host-level isolation/trace tests, 1 application test and 1 worker-clock test. Full suite, format gate, docs check, docs build and pack green. |
| P4a — `ProtoTest.Devices` spike | Complete | src +873 / −0 · tests +348 / −0 · docs 0 | The device package and its vertical slice, kept internal (`IsPackable=false`, no README/docs/changelog until P4b). Typed `ProtoDevice` classes are created per (type, id) and test through `Proto.Context.Devices().For<TDevice>("id")`; the protected primitives record `device.connect/send/receive/command` operations and a `device` entity (transport, address, connected), and configuration overrides address and transport per environment (`ProtoTest:Devices:{id}`). Backends implement `IProtoDeviceTransport`; `IProtoDeviceProtocol` catalogs drive `DeviceCoverageCollector`, which reports asserted kinds as covered and catalog kinds no test asserted as gaps. `ProtoCapabilityKinds.Device` and `[RequiresDevice<TDevice>]` gate tests the environment cannot run. 5 slice tests (per-test instances, config override, trace entity/operations, timeout failure with frames, coverage gaps, capability skip) against an in-memory transport; full suite and format gate green. |
| P4b — WebSocket backend | Complete | src +429 / −36 · tests +278 / −1 · docs +96 / −0 | `ProtoTest.Devices.WebSocket` and the public device pair: `AddWebSocketDevices(configure)` (the `AddWeb` shape) registers the transport, contributes the `WebSocket` capability and inserts code defaults as the first configuration source; `WebSocketDeviceOptions : IProtoConfigurableOptions` binds `ProtoTest:Devices:WebSocket` over them and validates. Endpoint overrides live in `ProtoDeviceEndpoints` (`ProtoTest:Devices:Endpoints:{deviceId}`), mirroring `ProtoApplication`; `DeviceAssertionException` derives `ProtoAssertionException`; registrations use `ProtoRegistrationGuard`; the source is one constant. 4 tests against a real Kestrel WebSocket server cover text and binary round-trips, connect failures naming the address, and close frames; READMEs, the docs page with limits, the devices fact sheet and changelog entries land with it. Full suite, format gate, docs check, docs build and pack green. |
| P4c — Device clients (design revision) | Complete | src +501 / −395 · tests +148 / −69 · docs +21 / −10 | Redesigned at the user's request: per-id registration and `ProtoTest:Devices:Endpoints` are removed. Named clients (`AddWebSocketClient("Chargers", path: "/ocpp/{deviceId}").AddDevice<AcCharger>()`) and `Proto.Context.Devices("Chargers")` mirror REST/GraphQL; address resolvers (`ProtoDeviceAddress.Template`/`.FromApplication`, application-relative with http→ws) replace per-device configuration; WebSocket options use `ProtoOptionsRegistration.Configure`. 8 device tests and 4 WebSocket tests updated; docs, READMEs, fact sheet and changelog rewritten. Full suite, format gate, docs check, docs build and pack green. |
| P4d — In-process WebSocket client (R2 prerequisite) | Complete | src +131 / −8 · tests +165 / −0 · docs +13 / −2 | `ProtoTest.Devices.WebSocket.AspNetCore` reaches an application's WebSocket endpoint through its `TestServer` with `AddInProcessWebSocketClient<TProgram>("Chargers", path: "/ocpp/{deviceId}")`, so an in-process gateway (R2's OCPP server) needs no listening socket. `WebSocketDeviceConnection` is now public over a plain `WebSocket`, so both transports share the frame handling; the socket transport stays for published addresses. TestApi gained a `/ws/{deviceId}` echo endpoint and 2 in-process tests (text and binary) prove the round-trip and the trace entity; docs, READMEs, fact sheet and changelog updated. Full suite, format gate, docs check, docs build and pack green. (Superseded by P4e: the separate client method became one registration with transport precedence.) |
| P4e — One device registration per mode | Complete | src +151 / −61 · tests +141 / −21 · docs +9 / −7 | Devices now follow the REST model exactly: `IProtoInProcessDeviceTransport.CanConnect` lets the client prefer an in-process transport when the application is hosted, so `AddWebSocketClient("Chargers", path: "/ocpp/{deviceId}")` is the only registration and `AddInProcessWebSocketDevices<TProgram>("Api")` is called once per host (harmless when published). Address resolvers gained an optional transform (`http(s)`→`ws(s)`), `TryServerFactory<TProgram>` was added to AspNetCore, and the separate in-process client API is gone. 3 AspNetCore-for-devices tests include the acceptance pair: the same client goes through the `TestServer` when the app is hosted and over the socket when `AddAspNetCoreServer` is absent. Core device tests (8) and socket tests (4) updated; docs, READMEs, fact sheet and changelog describe one registration. Full suite, format gate, docs check, docs build and pack green. |

## Decisions taken

- Website content first (user decision 2026-09-24): the site must be honest and evaluator-complete before
  the demo is announced.
- The demo lives in its own repository, created when R1 begins; nothing is created until then.
- The demo's framework demands reorder the feature plan rather than waiting for it: worker hosting,
  readiness, TimeProvider, Devices.WebSocket, WireMock, substitution, Aspire, demo benchmark.
- Docs versioning happens at 1.1; 1.0 is frozen then and patches update the current set.
- Track D2 MCP stays feature-plan work; its docs page and any hero line wait for D2–D4.
- W5: the screencast is dropped (a video ages and cannot be interacted with), and real screenshots were
  tried and dropped after reading them: at panel size the viewer's text shrank, the theme was fixed, and
  they added ~560 KB of drift-prone raster assets. The faithful `ViewerWalkthrough` replica carries the
  same three views with full-size text, theme awareness and zero assets.
- W8: GitHub Discussions is enabled and linked from the sustainability page, the FAQ, the footer and
  `SUPPORT.md`; the release feed is checked, not hand-trusted, because a release that skips
  `docs/src/data/releases.ts` fails `eng/check-docs.ps1`.
- W9: the changelog page is generated rather than curated, so it carries the repository `CHANGELOG.md`
  wording (the hand-written copy is gone); the homepage feed's summary is the release's opening
  paragraph, falling back to its first bullet. One edit per release; the announcement bar stays manual
  because it names the docs-version split.
- P1: `ProtoTest.Hosting` runs a worker's own entry point through Microsoft's **vendored**
  `HostFactoryResolver` (MIT, attributed, re-sync note) because the runtime ships it as source only -
  ASP.NET Core's `WebApplicationFactory` and EF Core tooling compile the same file. The one Core seam is
  additive: `IProtoConfiguredInfrastructure` lets infrastructure receive the run's state at start,
  which is how a worker reads a broker a container just started. Workers are run-scoped (no per-test
  lifetime), read infrastructure settings plus `ProtoWorkerOptions` (options win) and explicitly do not
  merge the suite's own configuration; `ProtoCapabilityKinds.Worker` is the new capability kind.
- P2: readiness is run-start only - no per-test `AwaitReady` - because that is where the sleeps were.
  A probe is ordinary infrastructure, so its position is its ordering; exceptions mean "not ready yet"
  and the last one rides in the timeout failure. `IProtoConfiguredInfrastructure` now takes a
  `ProtoInfrastructureContext` (settings + configuration) instead of settings alone, and provides a
  default implementation of the plain `StartAsync`, so P1's worker gained the suite's configuration in
  its precedence chain (worker sources < suite config < infrastructure settings < options) and
  implementers write one method. The container base owns the TCP-check plumbing (`ReadyWhenTcp`,
  `ReadyOn`); the shipped containers probe their standard ports (5432, 5672) and `ReadyOn` covers
  custom images. `AddHttpReadiness` accepts any HTTP response as "up" by default and takes a predicate
  for health endpoints.
- P3: the clock is per test seeded from the run clock, and it reaches in-process applications by
  **linking the request to the test** - the client already carried a test-id header; a startup filter
  pushes that test's clock for the request's duration - because ASP.NET Core's pipeline does not
  inherit the test's ambient `AsyncLocal` (proven by a failing test before the fix). Workers see the
  run clock (`ProtoHost.CurrentHost.Clock` moves it). `ProtoClock` is ours, not Microsoft's
  `FakeTimeProvider`, to keep Core dependency-free; only `GetUtcNow` is virtual, so timers stay real,
  and direct `DateTime.UtcNow` calls are unaffected - both documented limits.
- P4a: the device package stays `IsPackable=false` with no README, docs page or changelog entry until
  P4b ships a real transport - nothing unusable gets published or advertised. `AddTransport` and
  `AddProtocol` accept instances as well as types so tests and suite-configured backends do not need a
  DI factory. The frame log is bounded and only feeds failure messages; replay is designed
  (`device.replay`) but deferred rather than half-built. Device coverage is assertion-level: only a
  matched expectation records an observation, and the catalog supplies the gaps.
- P4b (consistency pass, user-requested): device options implement `IProtoConfigurableOptions` and
  bind through `BindFromConfiguration`; the backend exposes `AddWebSocketDevices(configure)` and
  inserts its code defaults as the first configuration source, mirroring `AddWeb` and
  `PlaywrightWebDefaults`; endpoint overrides moved to `ProtoDeviceEndpoints` with a `SectionPath`,
  mirroring `ProtoApplication`; `DeviceAssertionException` derives from `ProtoAssertionException`;
  registrations use `ProtoRegistrationGuard`; the trace source is one constant
  (`ProtoDeviceDiagnostics.TraceSource`); the skip attribute carries the same `AttributeUsage` as
  `RequiresPlaywrightBrowser`. `ProtoTest:Devices:Endpoints` stays a reserved section so a device id
  can never collide with a backend's option section. (The client/address shape below supersedes the endpoint-section part.)
- P4c (user design review): per-id registration and the `ProtoTest:Devices:Endpoints` section were the
  wrong shape and are gone. Devices now mirror REST/GraphQL clients - a named client declares its
  transport and address resolver once, typed devices and protocol catalogs hang off it
  (`devices.AddWebSocketClient("Chargers", path: "/ocpp/{deviceId}").AddDevice<AcCharger>()`) - and
  `Proto.Context.Devices("Chargers").For<AcCharger>("CP-001")` mirrors `Rest("client")`. The resolver
  helpers (`ProtoDeviceAddress.Template`/`.FromApplication`) replace per-id config: inside
  `AddApplication` a client uses the application's address (`http(s)` -> `ws(s)`), so one suite covers
  every environment. No Data-style defaults or provisioners were adopted (devices are not test-data
  objects; provisioning stays with ProtoTest.Data), and the in-process WebSocket gap is documented as
  a limit until R2.
- P4d (scheduled for R2): the socket transport cannot reach an in-process endpoint, so
  `ProtoTest.Devices.WebSocket.AspNetCore` adds `AddInProcessWebSocketClient<TProgram>`, which connects
  through the application's `TestServer`. The two are separate registrations on purpose - in-process
  vs published - and the shared frame handling lives in the public `WebSocketDeviceConnection` over a
  plain `WebSocket`, so future transports (MQTT aside) reuse it rather than re-implementing framing.
- P4e (user design review): the separate in-process client was the wrong shape, so devices were brought
  to the REST model. `IProtoInProcessDeviceTransport.CanConnect` lets the client prefer an in-process
  transport at connect time; `AddInProcessWebSocketDevices<TProgram>(application)` registers it once
  and the same `AddWebSocketClient(name, path)` works in every mode. **The rest of the swap-ability
  direction:** run-owned pieces should start only when the environment did not provide their endpoint
  (a Core `AddInfrastructureUnlessConfigured`-style helper), and tests branch only through
  capabilities/skips; no Core environment enum is planned, because real setups mix modes. That helper
  lands with R1's Setup where the demand is concrete.

## Stop criteria

- `/docs/compare`, `/docs/faq`, `/docs/project/sustainability` and `/docs/roadmap` exist, match shipped
  behavior and pass the docs check; no "coming soon" integration tile anywhere on the site.
- The overhead numbers vs raw `WebApplicationFactory` are published with methodology and an honest
  slower-or-equal case.
- Docs are versioned at the 1.1 release and the release banner points at the matching version.
- OpenCSMS runs from `dotnet test` and `docker compose up` in its own repo, in three modes, with the
  documented coverage gap and the real regression trace linked from its README.
- The framework gaps the demo pulled are shipped with tests, docs and changelog entries, or recorded as
  decisions here.

## Non-goals

- Not a commercial CSMS; no hardware certification, real PSP onboarding or production operations.
- No marketing that outruns the gap list (§7 of the recommendations): no testimonials, no coming-soon
  tiles, no landing-page redesign.
- No second demo inside this repository, and no Northstar rewrite — it stays the first-run sandbox.
- No MCP/agents content before the MCP server exists.

## Metrics to watch (rec §6)

- Demo trace opens; later, MCP endpoint hits.
- NuGet installs beyond the author's own CI (Core package trend).
- Issues filed by strangers and time-to-first-response.
- Docs search terms that find nothing.
