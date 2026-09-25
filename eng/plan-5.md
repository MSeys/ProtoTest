# ProtoTest Plan 5 — taking over plan 4: audit first, reference suite green, then the demo

Written 2026-09-25 · Baseline commit `61220f5` (branch `version/1.1`). Sources: `eng/audit-plan-4.md`
(this plan's finding list), `eng/plan-4.md` (scope and decisions), `eng/feature-plan.md` (feature
scope), `assets/internal/handoff-r1.md` (R1 state), `assets/internal/prototest-reference-demo-spec.md`.

Relationship: plan-4 remains the source of **scope** — its Track W is complete, Track R is at R1,
Track P is missing P4f.3 and P5–P8, Track X is untouched. Plan-5 **replaces plan-4's sequencing** for
everything still open and folds the audit-4 remediation in front of it. Where plan-5 and plan-4
disagree about order, plan-5 wins; where plan-4 and the audit disagree about a finding, the audit
wins. Plan-4's Decisions and "Things NOT to change" stay binding.

**Goal:** stop the drift that turned a feature push into a quality problem. Land the audit-4
correctness fixes before any new feature; make the reference suite a real product test (green in two
modes, honest gaps); only then resume the demo and the remaining framework work — each stage small,
evidenced, and handed off rather than absorbed.

## Why this plan is shaped this way

The audit found the kernel sound and the failures in three places: (1) fast feature work bend the
registration/address/config-timing contracts; (2) the only engineering fact base was gitignored and
stale, so sessions re-derived the model and copied; (3) the branch could not be verified — packages
carried the published version, CI never saw it, and "gates green" was prose. Plan-5 maps its phases
to those causes: Phase 0 fixes evidence and facts, Phase 1 makes the product proof real, Phases 2–3
fix the contracts, Phase 4 resumes features on top.

## Locked decisions

- **Correctness before features.** No new package, endpoint or capability work starts while Phase 2 is
  open; the only exceptions are fixes required to keep the reference suite green.
- **The reference suite is a gate, not a demo.** R1's exit is `dotnet test` green in container and
  configured modes with no code change between them, a documented coverage gap, and an honest README.
- **Audit-4 remediation is sequenced here.** Audit stages A0–A7 are adopted verbatim; the audit's
  findings are the work items.
- **One tracked fact base.** `eng/facts/` is the engineering source of truth; `assets/internal/` is a
  local scratch space (gitignored) and its `docs-facts/` set is archived input, not guidance. Any
  commit that changes the model updates the owning fact in the same commit.
- **Evidence or it did not happen.** Every stage records commit, diff shortstat and the output of
  `eng/verify.ps1`; plan rows quote that line.
- **One session, one stage.** A session that cannot finish a stage writes the handoff from
  `eng/handoff-template.md` and stops; the next session starts there. Long absorbing sessions are the
  failure mode this plan exists to end.
- **Distinct branch version.** Branch packages are `1.1.0-alpha.<n>`; `eng/pack.ps1` refuses to pack
  the published version. The OpenCSMS local-feed mapping stays until 1.1 ships.
- **No unplanned work.** A new idea becomes an audit finding, a feature-plan checkbox or a plan
  decision before code.

## Rules

1. Plan-4's rules stand: never advertise what does not exist; every behavior change lands with a test;
   docs-only work passes `eng/check-docs.ps1`; code passes `eng/test.ps1` and `eng/lint.ps1`.
2. Every stage is the unit of commit and the unit of green; record its evidence in this plan.
3. Phase 2/3 stages use audit-4's stage checklists; a finding not fixed carries a Decisions entry.
4. Facts, README, docs and changelog are updated in the same commit as the behavior they describe.
5. Container-backed changes ship their Testcontainers package and an inert path (audit ADDR-1).
6. Nothing is deferred into prose: a postponed item gets a phase entry or a decision.

## Phase 0a — Evidence, facts and guardrails (no behavior change) — done

Goal: make the branch verifiable and the model written down before touching it.

- [x] **0.1 Checkpoint OpenCSMS.** The product/suite state was already committed as `75b157b`; the gap
  log and the qualified mode claim are committed in OpenCSMS as `c55e056` (README +26/−3).
- [x] **0.2 Adopt the fact base.** `AGENTS.md`, `eng/facts/`, `eng/handoff-template.md`,
  `eng/quality-ledger.md` and this plan are tracked (see the P0a commits). The design skill points at
  `eng/facts/`; note `.opencode/` is gitignored, so that edit is local-only.
- [x] **0.3 Distinct branch version + pack guard (audit TST-3).** `Directory.Build.props` is
  `1.1.0-alpha.1`; `eng/pack.ps1` refuses to pack the `PackageValidationBaselineVersion`. The first run
  proved the finding: pack failed with "unnecessary suppressions" because the NuGet global cache held
  a locally-packed `prototest.* 1.0.1`; after `~/.nuget/packages/prototest.*` was cleared it passed
  against the real published baseline.
- [x] **0.4 CI and evidence (audit TST-4).** `ci.yml` runs `main` and `version/**` and takes a manual
  dispatch; `verify.yml` is dispatchable; `eng/verify.ps1 -Stage <name> [-Pack]` writes
  `artifacts/gates/<name>.json`.

Evidence (P0a, commit `4bafa6d`): `verify P0-guardrails 61220f5* (1.1.0-alpha.1): lint=PASS(43.5s)
docs=PASS(1.7s) test=PASS(64.4s) pack=PASS(48.2s) -> artifacts/gates/P0-guardrails.json`

## Phase 0b — Audit Stage A0 characterization tests (ProtoTest projects)

No production change. Execute `eng/audit-plan-4.md` Stage A0 for registration, configuration timing,
the clock and the device stack; the reference-suite collision (REF-1) is pinned by its R1a fix test
instead of a separate red characterization against a persistent database.

- [x] **0.5 A0 characterizations.** REG-1/REG-2 (mixed conditionals, two named servers, backend pair),
  REG-3 (post-`Build` entries), CFG-1 (configuration captured inside the worker's `Main`), CFG-3 (two
  hosts, one test id), DEV-1..DEV-4 (two in-process applications, dead in-process options, concurrent
  sends, two device types with one id). Every test passes while pinning today's behavior and names the
  audit finding it will flip.

Exit met: all characterizations pass and name their finding. Evidence: `verify A0-core`
(lint/docs/test PASS, stage tree `244eee9*`) and `verify A0-devices` (lint/docs/test PASS, stage tree
`586ed00*`) at `1.1.0-alpha.1`; commits `586ed00` and the A0-devices commit. A1/A3/A4 can now flip them.

## Phase 1 — R1a: the reference suite becomes a real test

Finding list: `REF-1`..`REF-6`; product fixes do not change ProtoTest source.

- [x] **1.1 Product correctness first.** Resolve the broker address at use time (CFG-2/REF-2);
  re-publish `invoice.issued` on redelivery and stop swallowing publish failures (REF-4); inject
  `TimeProvider` into the billing consumer. Done in OpenCSMS `8bf57ce`; the existing journey is green
  in container mode: `dotnet test tests/OpenCsms.Suite` → 3/3, twice, fresh PostgreSQL and RabbitMQ.
  The publisher now reads the address on first use, the consumer republishes on every delivery and
  fails the handler when the publish fails, and the invoice is stamped with the run clock.
- [x] **1.2 Repeatability (REF-1).** A `[CsmsOperator]` `ProtoAttribute` provisions tenant, tariff and
  station from `context.TestId`; every fixed identifier is gone (`grep` clean). Done in OpenCSMS
  `8c0ab21`; the persistent-database proof (second configured-mode run) is 1.7's acceptance.
- [x] **1.3 The ProtoTest way (REF-3).** Compose `AddMessaging(m => m.CaptureAttachments().UseRabbitMq())`
  after the application; the journey awaits the product's `csms.events` exchange (pre-bound tap,
  session-id + invoice-id predicate, 30 s timeout) with the REST read as a side-check; status/shape
  assertions are fluent; worker/broker capability gates are on the class. Done in OpenCSMS `8c0ab21`.
  Worker-absent experiment (anecdotal, R1a-14; not pinned by a committed test): with the gate in
  place the journey skips; without the worker and gate, the await fails with the expected
  `TimeoutException` rather than passing.
- [x] **1.4 Structure and evidence (REF-6).** Split `Api/` contract tests from `Journeys/`; register
  JSON/HTML sinks and the REST coverage collector; add the parallelism policy file; align NUnit
  majors; drop dead refs; extract `MigrateCsmsData()`. Done in OpenCSMS `a63be86`; two consecutive
  container-mode runs green with `LevelOfParallelism(8)` and `ParallelScope.All`, and the suite writes
  trace, JSON and HTML evidence under `TestResults/OpenCsms/`.
- [x] **1.5 Honesty (REF-6).** Write `COVERAGE.md` (OCPP, dashboard, published mode, multi-tenancy
  negative, DLQ, payments) and link it; correct the configured-mode recipe to all three keys; qualify
  the mode claims; add the gap log with CFG-1/CFG-2 as entries until A3 lands. Done in OpenCSMS
  `a63be86`: `COVERAGE.md` lists tested vs untested honestly, the README recipe names all three keys
  and the mode table says what runs today; CFG-2 is gone from the gap log, CFG-1 stays until A3.
- [x] **1.6 DLQ decision (REF-5).** **Decision:** implement the DLQ assertion with a suite-side support
  helper (raw `RabbitMQ.Client` publisher/consumer) rather than changing ProtoTest now. The framework
  gap is recorded for a future `ProtoTest.Messaging` addition: `ProtoMessage` drops the routing key
  and the client cannot address an `(exchange, routingKey)` pair, so a tap cannot filter by routing
  key and a poisonous event cannot be published through it. Revisit when a second consumer needs it.
  Implemented in OpenCSMS `2cb949f`: `Support/RabbitMqRawClient.cs` publishes the poison on the
  product exchange and routing key and polls `billing.session-ended.dlq`; `Billing/DeadLetterTests.cs`
  asserts the session id and `x-opencsms-attempts: 3`.
- [x] **1.7 Green in both modes.** Container mode with Docker; configured mode with the three keys and
  no code change; record both evidence lines; update `eng/plan-4.md`'s R1 rows and commit both repos.
  Done in OpenCSMS `2cb949f`: container 4/4 twice (fresh Testcontainers) and configured 4/4 twice
  against one PostgreSQL with the three keys exported; plan-4's R1 rows and the quality ledger are
  updated. The worker also found and fixed a real configured-mode blocker: `Setup` had no
  environment-variable configuration source, so the documented recipe could not have skipped the
  containers at all.

Exit: R1 green, documented, evidenced; plan-4's R1 row updated.

## Phase 1b — R1a review follow-ups (from `assets/internal/review-phase1.md`) — done

An independent review of Phase 1 found no blockers and seven should-fixes; all are fixed and the facts
updates (R1a-06) landed in `eng/facts/`. Done in OpenCSMS `e3cf209` + `92a6d0d`.

- [x] **R1a-01 — republish-on-redelivery test.** `Billing/RedeliveryTests.cs` delivers the same
  `session.ended` twice through the raw helper, awaits the second `invoice.issued`, and asserts one
  REST row and one database row. Red-proven: reverting the republish branch makes it time out.
- [x] **R1a-03 — the publisher must not cache a dead channel.** `RabbitMqEventPublisher` now requires
  `IsOpen`, disposes and resets a stale or half-created pair inside the gate, and retries from scratch;
  `Messaging/RabbitMqEventPublisherTests.cs` proves a closed channel is replaced.
- [x] **R1a-04 — the clock is observable.** `SuiteClock` pins the run clock at `2030-06-15T12:00:00Z`
  and the journey asserts the event's and the stored invoice's `IssuedAtUtc`; reverting to
  `DateTimeOffset.UtcNow` fails.
- [x] **R1a-02 — post-commit publish loss (decision).** At-most-once is accepted for M1 and recorded in
  the OpenCSMS README and `COVERAGE.md`; a transactional outbox is scheduled for R4 fault injection.
- [x] **R1a-07 — migrations run from the API only (decision).** EF Core 8 has no migration lock; the
  worker no longer migrates, the API does, and M4 revisits with an advisory lock or a designated
  migrator.
- [x] **R1a-05 — OpenCSMS run evidence.** `eng/run-suite.ps1 -Mode container|configured` writes
  `artifacts/gates/opencsms-<mode>-<timestamp>.log`; container and configured runs both green 6/6
  twice (`opencsms-container-20260925-145104/145156.log`,
  `opencsms-configured-20260925-145233/145242.log`).
- [x] **Nice-to-haves R1a-08..15.** Handoff corrected; `x-opencsms-retries` naming; per-method
  `[CsmsOperator]`; one `CsmsTargets.Api` constant; duplicate journey assertions removed; the tap
  pre-bind consequence recorded in the README/`COVERAGE.md`; the helper's cancel guard added. R1a-14
  (the worker-absent experiment) is marked anecdotal in Phase 1.3 rather than pinned.

## Phase 2 — Audit correctness fixes (plan-4 P4f.3 and the bent contracts)

Execute `eng/audit-plan-4.md` stages **A1–A4** in order; this is also where plan-4's P4f.3 consumer
rule lands (audit ADDR-1), with the reference suite as its first external proof.

- [x] **A1 Registration semantics** (REG-1..REG-5): per-declaration conditions, per-instance
  capabilities (`ProtoCapabilityDescriptor.Instance`), one post-`Build` rule, canonical key equality,
  winner-only Web capability and the conditional in-process device transport. Independently reviewed
  (`assets/internal/review-a1.md`, 0 blockers). Gate: `verify A1 935c2e0* (1.1.0-alpha.1): lint PASS ·
  docs PASS · test PASS · pack PASS`.
  - **A1 residuals (from review-a1.md, nice-to-have):** A1R-02 application-builder `Services` direct
    mutation bypasses the guard (land with A2/A4); A1R-04 pin `AddApplication`/`RegisterClient`
    post-`Build`; A1R-05 pin `HasCapability(kind, instance)` as negative; A1R-07 a direct-DI
    `IWebBackendFactory` loses its browser capability (Web/DX); A1R-01 pin the repeated same-name
    no-op. A1R-03/A1R-06 are fixed in this commit.
- [x] **A2 Address authority** (ADDR-1..ADDR-4). Complete; both halves independently reviewed
  (`assets/internal/review-a2a.md`, `review-a2b.md`).
  - **A2a** (commit `f8da0c2`): one settings → configuration precedence shared by
    readiness/HTTP/REST/GraphQL/gRPC/Web/devices with transport last; the before-publisher probe
    records an honest ordering reason; `ProtoReadinessOptions` binds `ProtoTest:Readiness`, reaches
    containers through `ProtoInfrastructureContext.Readiness`, and owns the timeout; the gRPC message
    names the application.
  - **A2b** (this commit): `AddCapabilityWhenProvided` (drop when no declared key is provided by
    configuration or a registered infrastructure piece); `UseRabbitMq` declares `Broker` through it;
    `SqlOptions.AddressKeys` (code-declared, non-bindable) makes SQL inert with the `Store` capability
    absent, including `AddEntityFrameworkCore` when it follows `AddSql`; `UseBroker(factory,
    addressKeys)` is the adapter seam. **HTTP/gRPC deliberately not adopted** (audit site 3): one
    protocol capability cannot express per-client keys, and an application-scoped client is
    legitimately served in-process; the failure stays at first use with the A2a resolver message
    (recorded in `architecture.md`/`gotchas.md`). External proof: OpenCSMS 6/6 in container and
    configured modes after a repack.
  - **Residuals:** A2aR-01 intermittent demo teardown NRE (watch; investigate if it recurs);
    A2aR-03/04/05 and A2bR-04/05/06 wording/pins (nice-to-have); `AddEntityFrameworkCore` must follow
    `AddSql` to inherit the address rule (documented order requirement); A2bR-06 (host `SqlOptions`
    after `AddSql`) recorded.
- [ ] **A3 Configuration timing and clock identity** (CFG-1, CFG-3, CFG-4): the worker `Main` args
  overlay with its capture test and documented limit; host-scoped clock locator. Update
  `hosting.md`, changelog and `eng/facts/gotchas.md`. OpenCSMS may then delete its deferred reads.
- [ ] **A4 Device stack correctness** (DEV-1..DEV-6): routing identity, options, concurrency, resource
  ids, disconnect state, worker registration conflicts — then freeze the device surface until R2.

⛳ Checkpoint P2 — after A2, verify the reference suite still runs with no conditionals and that its
capability gates behave; after A4, re-read `eng/facts/architecture.md` against the code and correct
it before starting A5.

## Phase 3 — Hygiene and the gates that let drift through

Execute audit stages **A5–A7**.

- [ ] **A5 Vocabulary and docs** (VOC-1..VOC-4, dead names): kind constants, the Messaging decision,
  the readiness surface, one poll loop; a docs-gate check that named `Add*` symbols exist.
- [ ] **A6 Tests and support** (TST-1, TST-2): the missing negative/concurrency tests; shared
  temp-trace/`FreePort`; deterministic waits; the lint grep.
- [ ] **A7 Release and evidence hygiene** (TST-5 remainder): changelog rollover, docs deny list from
  suppressions, package-validation rollover plan, recipe-trace guard, framework patch pins.

⛳ Checkpoint P3 — every audit finding is fixed or carries a recorded decision; refresh
`eng/facts/` to the post-audit model and mark the audit closed. Do not reopen it for new work.

## Phase 3b — DX and API consistency (`eng/dx-review.md`)

The register turns the reference suite's friction and a cross-integration API review into 18 findings
(P1 = 5, P2 = 7, P3 = 6), with the deliberate idioms listed so they are not "fixed". The P1s are part of
the 1.1 experience bar; P2/P3 land by demand, each as its own stage with the worker-cycle review.

- [ ] **DX-01 — one assertion surface.** Every assertable subject exposes `Should`/`ShouldNot` and
  chainable members; `ShouldX` extensions remain only where C# forbids a facade (gRPC messages and
  exceptions, generic model rows). Add facade members to Sheets/Web and the GraphQL error assertions;
  deprecate the old names first.
- [ ] **DX-02 — shape failures name their subject.** The protocol producer prefixes the identifier
  (route/destination/operation) to the mismatch message; trace attributes unchanged.
- [ ] **DX-03 — a code API for a messaging tap.** `UseRabbitMq().Tap("invoice.issued")` (or
  `Destinations(...)`) so the reliability declaration lives in the test, with the config key staying as
  the environment override.
- [ ] **DX-05 — required reads.** `ReadRequired<T>()` (and `ReadRequired<T>(path)` composing with A1)
  throws a protocol exception naming the identifier when the body is missing; the nullable reads stay.
- [ ] **DX-04** is already a plan-5 decision (routing key); promote it when DX-03/DX-10 need it.

P2/P3: the register is the order; each becomes a stage when scheduled, and its "Deliberate idioms —
do not change" section is binding.

## Phase 4 — Resume plan-4 on a clean base

Order is plan-4's demand order; each item is its own stage with the feature-plan launch checklist
(README + docs with Limits, failure-path tests, trace conventions, demo coverage, changelog).

- [x] **R1b Finish M1** — accepted in Phase 1: first journey green in both modes, DLQ asserted,
  `COVERAGE.md` linked, README honest. The trace showpiece link belongs to R5.
- [ ] **R2 OCPP gateway + simulator** (needs A3 clock, A4 devices, P4b/P4e). This is the first real
  device workload: it must not need device code changes to land — if it does, that is a finding, not
  a silent extension.
- [ ] **R3 Dashboard + export** (needs Web/Playwright, Sheets).
- [ ] **P5 WireMock, P6 per-test substitution, P7 Aspire, P8 demo benchmark** (R4/R5 prerequisites).
- [ ] **R4 topology + published mode + fault injection + nightly CI** (needs P5–P7).
- [ ] **R5 benchmarks + trace showpiece + launch** (needs P8, W1/W4/W5 exist).
- [ ] **X1 template `--runner` variants, X2 static trace index** (audit-3 carry-overs; after B4/B5 and
  W5 respectively).
- [ ] Everything else in `eng/feature-plan.md` waits for demand; D2–D4 (MCP) and its docs page are
  explicitly not 1.1.

## Progress

| Phase | Status | Evidence | Notes |
| --- | --- | --- | --- |
| 0a — Evidence, facts, guardrails | Complete | `4bafa6d` · verify P0-guardrails (lint/docs/test/pack PASS, stage tree 61220f5*, 1.1.0-alpha.1) | Facts + contract + ledger tracked; version/CI/verify landed; the pack run proved the TST-3 cache collision before passing |
| 0b — Audit A0 characterization | Complete | `A0-core` 586ed00 · `A0-devices` (lint/docs/test PASS, stage trees 244eee9*/586ed00*, 1.1.0-alpha.1) | All characterizations pass pinning current behavior: mixed conditionals, two named servers, Web backend pair, post-`Build`, clock locator, worker `Main`, in-process transport marker/endpoint/options, connect race, device resource id |
| 1 — R1a reference suite | Complete | 1.1 `8bf57ce` · 1.2/1.3 `8c0ab21` · 1.4/1.5 `a63be86` · 1.6/1.7 `2cb949f` — container 4/4 ×2, configured 4/4 ×2 (one DB) | Journey, DLQ, both modes, honest README/COVERAGE; Phase 2 (audit A1–A4) next |
| 1b — R1a review follow-ups | Complete | OpenCSMS `e3cf209` + `92a6d0d`; logs under `artifacts/gates/` | All seven should-fixes done: redelivery test, dead-channel reset, observable clock, at-most-once recorded, API-only migrations, evidence runner, suite polish; R1a-14 anecdotal |
| 2 — Audit correctness A1–A4 | In progress | A1 `2e84e65` · A2a `f8da0c2` · A2b (this commit), all independently reviewed; gates PASS; OpenCSMS 6/6 both modes | A3 (config timing/clock) next |
| 3 — Hygiene A5–A7 | Pending | — | Vocab/docs, tests/support, release evidence |
| 3b — DX and API consistency | Pending | `eng/dx-review.md`: 5 P1, 7 P2, 6 P3 | DX-01/02/03/05 accepted (DX-04 recorded); P2/P3 by demand; deliberate idioms binding |
| 4 — Plan-4 resume | Pending | — | R2–R5, P5–P8, X1/X2 |

## How this plan keeps sessions from drifting

1. **One plan of record, one stage at a time.** `AGENTS.md` tells every session to read this plan and
   `eng/facts/` first, pick the next unchecked item, and not add scope. A stage that grows past its
   checklist is stopped and handed off, not absorbed.
2. **The model is written down where the work happens.** `eng/facts/architecture.md` names the one of
   everything (host, builder, context, resource registry, infrastructure registration, capability,
   client resolution, options registration, evidence boundary, adapter boundary) and the registration
   semantics; `recipes.md` says which existing pattern to copy for each kind of change; `gotchas.md`
   records the traps this audit found. A behavior change updates the owning fact in the same commit.
3. **Evidence replaces assertion.** `eng/verify.ps1` writes machine-readable gate results; plan rows
   quote them. A stage that cannot show its evidence is not done. The gate is scoped to the change:
   docs-only stages run the docs check only, code stages format-check the projects they touched, and
   pack stays opt-in — `-Full` restores the CI shape for release or shared-boundary stages.
4. **The branch is verifiable.** A distinct `1.1.0-alpha.<n>` version, CI on the branch, pack
   self-checks, and a demand-pulled plan mean a session can stop at any point without leaving a
   half-verifiable tree.
5. **Handoffs are a deliverable.** `eng/handoff-template.md`: state, commits, evidence, decisions,
   open items with next action, gotchas. Written before a session stops, not after.
6. **Features have a definition of done.** The feature-plan launch checklist is enforced at the stage
   boundary: no README, docs Limits, failure tests, trace conventions, demo coverage, changelog — no
   "done".
7. **Every worker gets a review step.** The controller verifies the gate line and the diff, then checks
   the change against `eng/facts/recipes.md` and `eng/dx-review.md` (a public surface follows the
   recorded idiom, or the register is updated with why). A public-surface stage gets an independent
   review worker before it is committed; Phase 1's review is the template
   (`assets/internal/review-phase1.md`).

## Decisions taken

- Plan-5 replaces plan-4 sequencing; plan-4 scope and decisions stand.
- CFG-1 is fixed, not documented, because R1 proved the cost; the parameterless entry point is the
  only documented limit.
- P4f.3 (the consumer rule) is not a standalone stage; it is audit A2 with the reference suite as its
  acceptance case.
- The DLQ question (REF-5): decided — suite-side support helper now (raw RabbitMQ.Client); the
  framework `(exchange, routingKey)` addition is a recorded future gap, not a 1.1 change. Revisit when
  a second consumer needs routing-key addressing.
- Phase 1b: at-most-once publish is accepted for M1 (recorded in OpenCSMS `COVERAGE.md` and the README
  gap log); the transactional outbox is R4 work, where fault injection can test it.
- Phase 1b: migrations run from the API only. EF Core 8 does not serialize concurrent migrations; M4
  revisits concurrency with an advisory lock or a designated migrator.
- RELEASING.md, the fact base and the handoff template become tracked; `assets/internal/` stays local
  scratch.
- R2 does not start before A4; if R2 needs device changes, those are findings first.

## Non-goals

- No second demo in this repository, no Northstar rewrite (plan-4 non-goal stands).
- No MCP/agents content before D2–D4 exist.
- No device transports beyond WebSocket until a real user asks (plan-4 F4).
- No new architecture: the audit's direction is delete/simplify/consolidate/explicitize; the only
  structural change is per-instance capability identity.

## Stop criteria

- The audit is closed: every finding fixed or carrying a decision, `eng/facts/` current, audit-4
  marked closed.
- The reference suite is green in both modes with no code change, has `COVERAGE.md` and an honest
  README, and plan-4's R1 rows are committed.
- Branch packages are distinguishable and CI-verified; every 1.1 stage row carries evidence.
- No user-facing artifact teaches a symbol or mode that does not exist.
- Then feature work resumes from plan-4's demand order, one stage at a time.
