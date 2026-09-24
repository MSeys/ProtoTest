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
- [ ] **W5 The viewer as the primary CTA** (rec 2.3): a "See what a failed integration test actually did"
  action beside the hero CTAs pointing at the demo trace, plus a ~60-second screencast committed to the
  docs assets and reusable for launch.
- [ ] **W6 Overhead benchmark vs raw `WebApplicationFactory`** (rec 2.6, completes feature-plan A7):
  per-test overhead (lifecycle + context + trace), suite startup with tracing on/off, trace write cost and
  size at 100/1,000 tests, methodology and hardware, including where ProtoTest is slower.
- [ ] **W7 Versioned docs at 1.1** (rec 2.5): cut the 1.0 snapshot at the 1.1 release, point the release
  banner at the matching version, verify old links resolve. Executes last on this track.
- [ ] **W8 Small wins** (rec 2.7): homepage release feed (blog-as-release-notes), GitHub Discussions
  enabled and linked, the AI-usage page surfaced from Why ProtoTest, and a docs-search check that
  "integration testing .NET", "trace" and "coverage" land on the right pages.

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

- [ ] **P1 Background-worker hosting** (spec item 1) → feature-plan **C5**: `AddWorker<Program>` or
  generic-host support so the billing worker is hostable the way the API is, with lifecycle, readiness
  and trace.
- [ ] **P2 Readiness waiting** (spec item 2) → feature-plan **C6**: `AwaitReady`/health probes on
  resources and applications instead of sleeps.
- [ ] **P3 Clock control** (spec item 5) → feature-plan **A8**: `TimeProvider` integration
  (`Proto.Context.Clock()`, `FakeTimeProvider` in tests) for tariff and expiry behavior.
- [ ] **P4 `ProtoTest.Devices.WebSocket`** (spec item 4) → feature-plan **F2** reordered: WebSocket is the
  first device backend, earned by OCPP; MQTT follows a real MQTT user.
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

## Decisions taken

- Website content first (user decision 2026-09-24): the site must be honest and evaluator-complete before
  the demo is announced.
- The demo lives in its own repository, created when R1 begins; nothing is created until then.
- The demo's framework demands reorder the feature plan rather than waiting for it: worker hosting,
  readiness, TimeProvider, Devices.WebSocket, WireMock, substitution, Aspire, demo benchmark.
- Docs versioning happens at 1.1; 1.0 is frozen then and patches update the current set.
- Track D2 MCP stays feature-plan work; its docs page and any hero line wait for D2–D4.

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
