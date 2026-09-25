# ProtoTest Internal Audit 4 — Findings and Plan

Audit date: 2026-09-25 · Baseline commit: `61220f5` (branch `version/1.1`, clean working tree at audit
start). Scope: `src/` (35 projects, 480 production files, ~33.4k lines), `tests/` (213 files, ~27.9k
lines), `eng/` scripts, `.github/`, `samples/`, the docs gates, plus the reference suite at
`C:\Development\OpenCsms` (audited here as R1a because plan-4's R1 is blocked on it).

Method: full read of `ProtoTest.Core` and the P1–P4f seams; deep passes over Hosting/Devices, the
HTTP/integration family, tests/adapters/tooling and the reference suite; every CRITICAL/HIGH finding
re-verified against source by a second read. Prior audits (`eng/audit-plan.md`, `audit-plan-2.md`,
`audit-plan-3.md`) and their recorded decisions were read first; findings are deduplicated against
them, and only the two items audit 3 carried into plan-4 (X1 template runner variants, X2 static trace
index) are referenced rather than re-reported.

Why this audit exists: the branch changed character after Audit 3. P1–P4f shipped worker hosting,
readiness, a test clock, conditional infrastructure and capabilities, an ASP.NET Core step-aside, a
device stack and a reference suite, and the user reports the sessions ran too long and quality
suffered. The question this audit answers is what that push bent — not whether the older model is
sound, which it is.

**Status: closed 2026-09-25.** Stages A0–A8 complete: A0–A7 on `version/1.1` (closing commit
`8382789`), A8 executed as `eng/plan-5.md` Phase 1/1b with the independent Phase 1 review. Every
finding is fixed, decided, or carried as a named residual in plan-5 (`A1R`–`A7R`). Do not reopen this
plan for new work — add it to `eng/plan-5.md` or the feature plan.

## Executive summary

- **Overall health:** the kernel (host, run/test lifecycle, resource ownership, evidence boundary,
  adapters) survived the push intact, and the convergence Audits 1–3 aimed for is real: one descriptor
  per protocol, one evidence policy, one retry/poll engine, one outcome vocabulary, nine parallel test
  suites. The weakest part is the **new declarative registration model** — conditional infrastructure
  and capabilities, multi-instance capabilities, and post-`Build` registration — which was built fast
  and has correctness holes, not just cosmetic ones. The second weakest part is **process**: the
  branch's packages carry the published `1.0.1` version, CI never sees the branch, "gates green" is
  prose, and the only engineering fact base (`assets/internal/docs-facts/`) is gitignored, stale at
  `db9d7aa`, and documents `0.1.0-alpha` APIs.
- **Strongest parts:** skip-before-ownership for conditional infrastructure (the skip set is computed
  in `Build` and the resource is removed before anything can own it, `ProtoHostBuilder.cs:120-131`);
  conditional capabilities reuse the existing descriptor and `[RequiresCapability]` path; the evidence
  boundary from Audit 3 still holds; the per-test clock design (seed, virtual `GetUtcNow`, real
  timers); readiness as ordinary infrastructure with trace evidence; the adapter contract tests; and
  the step-aside tests that prove which server answered, not which one is registered.
- **Weakest parts:** capability identity under multiplicity (`REG-1`, `REG-2`); the address-provider
  rule adopted by exactly one integration (`ADDR-1`); a worker's `Program.Main` cannot see the run's
  configuration (`CFG-1`, which the reference suite hit on day one); the device stack's in-process
  routing/options and connect races (`DEV-1`–`DEV-4`); and docs/gates that cannot see a dead API name
  (`VOC-3`, `TST-5`).
- **Systemic vs localized:** ~10 systemic findings (registration semantics, address authority,
  configuration timing, vocabulary ownership, evidence/process discipline); the rest are localized
  (device concurrency, dead surface, one bad message). No redesign is warranted: one coherent model
  change (registration semantics) plus finishing the address model, then hygiene.
- **Top five areas, in dependency order:**
  1. Registration semantics: per-instance capability identity, per-declaration condition precedence,
     one post-`Build` rule (`REG-1`–`REG-3`).
  2. Address authority and readiness: finish the P4f consumer rule, unify the readers, one timeout
     policy (`ADDR-1`–`ADDR-4`).
  3. Configuration timing and clock scoping: the worker `Main` seam; host-scoped clock lookup
     (`CFG-1`–`CFG-4`).
  4. Device stack correctness before R2 builds on it (`DEV-1`–`DEV-6`).
  5. Evidence and process: distinct branch version, CI on the branch, gate evidence files, a tracked
     fact base, and `AGENTS.md` (all part of Track A's exit and plan-5's Phase 0).

## Locked decisions (this audit)

- A descriptor that describes one **instance** must be distinguishable per instance. A shared
  descriptor may not be dropped while another declaration for it is unsatisfied: "unless configured"
  is decided per declaration, and its effect is per instance.
- "Unless configured" has **one** predicate and **one** precedence rule for infrastructure and
  capabilities (partial satisfaction keeps the piece; a plain declaration is a promise no environment
  can withdraw).
- `Build()` is terminal for every public registration entry, or the exception is documented per
  entry. Mixed signals between adjacent builder calls are not allowed.
- One address authority per application, resolved at use time; a missing address makes the integration
  inert and its capability absent, so tests skip via `[RequiresCapability]` (the P4f rule, completed).
- Registration-time configuration (a worker `Program.Main` read) is either a supported seam with a
  test that proves it, or a documented Limit with a test that pins the boundary. It is not left
  implicit.
- Host-unscoped ambient lookups are not introduced again: `ProtoHostRegistry.FindTraceWriter` already
  established that a lookup is keyed by host identity.
- The reference suite is part of the quality bar: its first journey green in both modes, with a
  documented coverage gap, is a gate for 1.1, not a demo.
- Evidence discipline: every stage records commit, diff and the gates that ran. `eng/plan-5.md` is
  the sequencing plan of record; `eng/facts/` is the tracked engineering fact base.

## Rules

1. Every behavior change lands with the characterization test that pins the new behavior; the old
   behavior is pinned first where it is being changed deliberately.
2. Every stage ends green: `eng/test.ps1` full suite plus `eng/lint.ps1`; docs changes also
   `eng/check-docs.ps1`; packaging changes also `eng/pack.ps1`.
3. Each stage records an evidence line (commit, `git diff --shortstat`, gates run and result). "Green"
   without evidence is not a claim.
4. Public surface changes are additive except accidental-public plumbing and the unreleased 1.1
   packages; each is called out in the changelog.
5. A stringly contract becomes a descriptor, enum or constant where it is introduced.
6. Nothing is deferred into prose: a postponed item gets a stage entry or a Decisions entry.
7. Work only from an audit or plan item; new work becomes a finding here before code is written.

Severity: CRITICAL / HIGH / MEDIUM / LOW / OBSERVATION. Confidence: H / M / L.
Compatibility: **I** internal · **B** behavioral source-compatible · **E** extension API · **C**
consumer API breaking.

## Findings register

| ID | Finding | Sev | Conf | Compat |
| --- | --- | --- | --- | --- |
| REG-1 | Condition precedence is per descriptor, not per declaration: satisfying one conditional drops a capability another unsatisfied declaration still promises | HIGH | H | B |
| REG-2 | Instance capabilities collapse: two named servers, or two web backends, share one descriptor; one configured address (or one losing registration) changes both | MEDIUM | H | B |
| REG-3 | Post-`Build` registration is inconsistent: `AddResource`/`ConfigureTracing` throw, every `ConfigureServices`-based entry (hook, gate, capability, clock) silently no-ops | LOW | H | E |
| REG-4 | `ProtoConditionalCapability` is a record with a collection member: reference equality defeats dedupe, so declarations accumulate and `Distinct()` never merges them | LOW | H | I |
| REG-5 | `AddInProcessWebSocketDevices` declares its device capability unconditionally while the server capability it depends on is conditional; a published run still advertises the in-process transport | LOW-MED | H | B/E |
| ADDR-1 | The P4f consumer rule is adopted only by `AddAspNetCoreServer`; five ranked sites still advertise a capability a missing address cannot serve (Sql fails in setup, Rabbit at setup/first use, REST/GraphQL/gRPC at use) | MEDIUM | H | B/E |
| ADDR-2 | Four address readers with three precedences: readiness follows settings-published addresses, HTTP clients read static configuration only, web/devices read settings first; `AddHttpReadiness` registered before its publisher silently records "runs in-process" for a published app | MEDIUM | H | B |
| ADDR-3 | Readiness timeout is owned twice: host probes share `ProtoReadinessOptions`, containers use a private `ReadinessTimeout`/`ReadinessInterval`, and `AddHttpReadiness` adds a third request timeout; a suite cannot tune the slow-image case | LOW-MED | H | E |
| ADDR-4 | gRPC's missing-address error prints the literal `{app}` instead of the application name (`ProtoGrpcClientInitializer.cs:85-89`) | LOW | H | B |
| CFG-1 | A worker's `Program.Main` configuration read before `Build()` never sees the run's settings or suite configuration; only hosted services at `StartAsync` do (the P1-gap; the reference suite worked around it) | HIGH | H | E |
| CFG-2 | The reference suite's product reads the broker address eagerly at registration, so its publisher is inert in container mode; POST /end fails and the worker cannot publish `invoice.issued` | HIGH | H | B |
| CFG-3 | `ProtoClockLocator` is process-global, keyed by test id alone: concurrent hosts with the same `RunPrefix` overwrite/remove each other's mapping, and a throw after registration leaks the entry | MEDIUM | H | B |
| CFG-4 | `ProtoClock.Advance` reads the time outside the lock then writes: concurrent advances on the run clock lose updates; the request-scoped ambient push can outlive its request (observation) | LOW | M-H | I |
| DEV-1 | The in-process device transport is registered behind one non-generic marker and `CanConnect` ignores its endpoint: a second `TProgram` is silently dropped, and a client can be routed through the wrong application's `TestServer` at the same path | HIGH | H | E |
| DEV-2 | The in-process transport ignores `WebSocketDeviceOptions` (constructs `new WebSocketDeviceOptions()`), while `AddInProcessWebSocketDevices(configure)` registers them: the `configure` parameter is dead and validation never runs | MEDIUM | H | E |
| DEV-3 | `DeviceSession` connect/disconnect is check-then-act with an ungated `_connection` clear: concurrent sends open two connections (one leaked, untraced) and a disconnect between connect-return and use can NRE | MEDIUM | H | E |
| DEV-4 | Device resource ids omit the device type (`device:{client}:{id}`): two device types with the same id on one client throw "resource already registered" even though the client registry is type-keyed | MEDIUM | H | E |
| DEV-5 | `device.connected` is never finalised: `DisconnectAsync` writes no entity state and `DisposeAsync` bypasses the disconnect trace, so every test that does not disconnect leaves the entity claiming connected at teardown | LOW | H | E |
| DEV-6 | Hosting registration silently accepts conflicts and shape surprises: a second `AddWorkerHost` with the same name but a different program is ignored; a parameterless entry point loses content root/application name; unrecognised builder shapes are skipped with no error; `ProtoWorkerOptions` null semantics contradict the XML | MEDIUM | H | E |
| VOC-1 | Messaging's XML promises coverage ("so coverage can aggregate destinations") and carries a dead `CoverageCategory`, but no Messaging collector exists; destinations are recorded and never consumed | LOW | H | I/E |
| VOC-2 | Producer/consumer drift: `web.page.verified`/`web.page.visited` and `graphql.failure` are literals while sibling kinds are constants; `sheets.workbook` is recorded and only `sheets.range` is consumed | LOW | H | I |
| VOC-3 | Dead public/observable surface: `IProtoReadinessProbe` and `ProtoReadinessResult.LastError` are never used; `ProtoProtocol` `CoverageCategory` (Messaging) never read | LOW | H | E |
| VOC-4 | `ProtoReadiness.WaitAsync` duplicates `ProtoPolling.PollAsync` (same stopwatch/delay loop, different exception policy) | LOW | H | I |
| TST-1 | The new features' tests are happy-path only: no Main-time config capture, no bad/unreachable `BaseUrl`, no readiness bad URL, no second in-process application, no options binding, no disconnect, no device concurrency, no same-id-two-types, no same-name-different-program worker | MEDIUM | H | — |
| TST-2 | Test-support drift despite `ProtoTest.TestSupport`: temp-trace and `FreePort` helpers are copied into six new suites; timing windows (150–200 ms) are assumptions, not invariants | LOW | H | — |
| TST-3 | Branch packages are version `1.0.1`, identical to the published `1.0.1`, 170 commits diverged; the NuGet global cache poisoning this caused is documented, and no `NuGet.config` in this repo prevents it | HIGH | H | — |
| TST-4 | `version/1.1` never meets CI (`ci.yml` triggers on `main` only, `verify.yml` has no dispatch) and no script records gate evidence; plan rows assert "full suite, format gate, docs check, docs build and pack green" as prose with no artifact | HIGH | H | — |
| TST-5 | Release/docs-gate debt: `[Unreleased]` is never rolled by `cut-release.ps1` and release notes silently fall back; `RELEASING.md` is gitignored; `check-docs` skips its config-key cross-check (the only fact sheet is gitignored and stale); the removed-symbol gate is a hand list; six packages opt out of package validation with no rollover; recipe traces accept a stale file after a zero-match filter; framework packages float `8.0.*`/`9.0.*` with no lock | MEDIUM | H | — |
| REF-1 | OpenCSMS tests use fixed tenant `"acme"` and fixed tariff/station names against unique `(TenantId, Name)` indexes: a second `dotnet test` against a persistent configured database collides | CRITICAL | H | — (suite) |
| REF-2 | OpenCSMS's broker publisher reads its address eagerly at registration in both `Program.Main`s, so container-provided settings are invisible; it is inert in container mode (same class as CFG-1, product side) | CRITICAL | H | — (product) |
| REF-3 | The suite polls REST for the invoice instead of composing `AddMessaging` and awaiting `invoice.issued`, and asserts through a local `PostAsync<T>` + raw `Assert.That(status)` instead of `Should.HaveHttpStatus`/`ShouldMatchShape`; the asynchronous contract is never actually asserted | HIGH | H | — (suite) |
| REF-4 | Redelivered `session.ended` acks without republishing `invoice.issued`, a failed publish is swallowed, and the invoice timestamp ignores the injected clock (`DateTimeOffset.UtcNow`) | HIGH | H | — (product) |
| REF-5 | The DLQ assertion R1 promises is not expressible with the current ProtoTest messaging model (taps bind destinations as exchanges; the product DLQ is a queue) — needs a decision, support helper or a framework `(exchange, routingKey)` addition | MEDIUM | H | E? |
| REF-6 | Suite/product hygiene: validation tests live in the journey class; no spec-shaped layout; no sinks/collectors/capture; no parallelism policy; no `COVERAGE.md`; README configured recipe misses one of the two RabbitMQ keys, overclaims modes and records no gap log; dead project refs; duplicated `Migrate()`; NUnit major drift between the two test projects | MEDIUM | H | — |

## A. Registration semantics (root cause: condition and identity collapse)

**REG-1 — Mixed conditional declarations silently drop a live capability.**
Evidence: `ProtoHostBuilder.cs:137-165` marks a descriptor skipped as soon as **one** satisfied
conditional exists and no plain declaration for it exists; `ProtoCapability.cs:31-38` decides
satisfaction per declaration. With descriptor D declared conditionally under keys A and B, A
configured and B not, the loop removes D's descriptor and skips it, while the integration behind B is
still registered and serving.
Why it matters: a silently skipped capability is the worst failure mode this system can have — tests
disappear from the run, `[RequiresCapability]` reports false, and the trace says "already configured"
for something the environment did not provide. Concrete instance: two named `AddAspNetCoreServer`
registrations share one descriptor (`ProtoTest.AspNetCore/ProtoHostBuilderExtensions.cs:60-62,91-93`);
with only server A's `BaseUrl` configured, server B still starts in-process but every
`[RequiresCapability("server")]`/`[RequiresInProcess]` test skips.
Pattern: precedence keyed on declaration *shape* (`Keys.Count == 0`) and descriptor *identity* rather
than per-declaration satisfaction.
Direction: skip D only when every conditional declaration for D is satisfied (or no live integration
needs it) and no plain declaration exists; add a per-instance name to capabilities that describe an
instance. Replace the single record with a decision that names the declaration that decided it.
Compat: B (behavioral correctness fix), E for the per-instance descriptor rule.
Tests: same descriptor declared twice, one key configured and one missing → capability stays; both
satisfied → drops; two named servers with one address → `HasCapability` and `TryServerFactory` agree
for each name.

**REG-2 — Instance capabilities collapse.**
Evidence: `ProtoCapabilityDescriptor` is a value record; `AddCapability` dedupes by value
(`ProtoCapability.cs:109-110`); a per-instance fact (which server, which browser backend) is not in
the value. Web: `AddWeb` is first-wins (`ProtoTest.Web/ProtoHostBuilderExtensions.cs:64-66`) but each
backend package declares its browser capability unconditionally
(`PlaywrightWebHostBuilderExtensions.cs:42-45,68-69`, `SeleniumWebHostBuilderExtensions.cs:35-38,56-57`),
so referencing both leaves one live backend and two browser capabilities: `[RequiresCapability(browser,
"Selenium")]` passes while `GetBackendAsync<SeleniumWebBackend>` throws.
Why it matters: capabilities are the skip contract; a capability that outlives its adapter turns a
missing-feature failure into a confusing runtime error (or a green test that never exercised the
backend).
Pattern: value-equality identity applied to multi-instance registrations.
Direction: declare a capability only in the call that actually wins the registration; if a capability
describes an instance, put the instance name in the descriptor.
Compat: B.
Tests: register Playwright then Selenium → one backend, one browser capability; two named servers as
in REG-1.

**REG-3 — Post-`Build` registration is inconsistent.**
Evidence: `ConfigureTracing` (`ProtoHostBuilder.cs:44-50`) and `AddResource` (`:81-87`) call
`ThrowIfBuilt`; `ConfigureServices` (`:21-26`) does not, and `AddCapability`, `AddCapabilityUnless­
Configured`, `ConfigureClock`, `AddTestHook`/`AddRunHook`/`AddRunGate` all go through it — silently
registering into a provider that was already built.
Why it matters: after `Build()` the capability/hook/clock is lost with no signal; a helper that
composes one call late changes skip behavior invisibly.
Pattern: guard applied per field-mutating method instead of at the composition entry points.
Direction: one rule — every public builder entry either throws after `Build` or participates in the
documented single-build model; `AddInfrastructure` already fails loudly only because `AddResource` runs
first.
Compat: E (only silent no-ops change).
Tests: each registration entry after `Build` throws the single builder message.

**REG-4 — Collection-member records defeat dedupe.**
Evidence: `ProtoCapability.cs:31-33,116-127`: each call constructs a fresh `[]`/`ToArray()` for
`Keys`; record equality uses the collection's reference equality, so `TryAdd` never dedupes conditional
or unconditional declarations and `satisfiedConditionals.Distinct()` merges nothing.
Why it matters: it is the mechanism that lets a "same capability declared twice" situation look fine
while the Build loop sees several declarations; it also makes the DI collection grow with every
helper call.
Pattern: record + mutable/reference collection used as a value key.
Direction: normalize keys to a canonical comparable form (sorted string, or custom equality) at the
registration boundary; sweep the codebase for records with collection members used in `Distinct`/`==`.
Compat: I.
Tests: registering the same conditional twice leaves one declaration; mixed conditional test from
REG-1 covers the consumer.

**REG-5 — In-process device capability is unconditional.**
Evidence: `InProcessWebSocketDeviceHostBuilderExtensions.cs:38-41` adds the capability with
`AddCapability` while its own `CanConnect` returns false in published mode
(`InProcessWebSocketDeviceTransport.cs:24-28`). The server capability it depends on steps aside for a
configured address; the transport capability does not.
Direction: declare the in-process transport capability with `AddCapabilityUnlessConfigured` on the
application's `BaseUrl`, or drop the capability entirely (the client falls back to the socket
transport by design, so the capability describes an enhancement, not a requirement — decide and
document which).
Compat: B/E.
Tests: published run with a configured `BaseUrl` → either the capability is absent and the socket
transport serves, or the capability stays and a test explains why.

## B. Address authority and readiness (root cause: the P4f rule is half-applied)

**ADDR-1 — The consumer rule is adopted by one integration.**
Evidence: the only `AddCapabilityUnlessConfigured` call sites are AspNetCore
(`ProtoHostBuilderExtensions.cs:60-62,91-93`). Ranked gap sites, verified:
1. **Sql — fails in setup:** capability is unconditional (`ProtoTest.Sql/ProtoHostBuilderExtensions.cs:30`)
   and the connection is opened during test setup (`SqlConnectionHook.cs:29`).
2. **Messaging/RabbitMQ — fails at setup or first use:** `UseRabbitMq` declares Broker unconditionally
   (`ProtoTest.Messaging/ProtoHostBuilderExtensions.cs:128-129`); the default address points at
   localhost (`RabbitMqOptions.cs:16`); connection happens at `PrepareAsync` (when destinations are
   configured) or first publish.
3. **REST/GraphQL/gRPC — fails at use:** capabilities are unconditional with no client or address;
   the failure surfaces on the first call (`ProtoHttpClientResolver.cs:82-87`).
4. **Web — fails at use, partly domain-led:** relative navigation needs an address, absolute-URL
   sessions legitimately do not — a genuine domain difference, documented.
5. **Sheets:** no address concept; honest.
Why it matters: this is the model the reference suite was built to prove ("no mode conditionals");
half-applied, a suite that registers an integration and runs without its address gets a setup failure
instead of a skip, which is the exact behavior the model exists to prevent.
Pattern: a cross-cutting rule adopted at the first consumer instead of at the capability boundary.
Direction: for each gap site, make the capability conditional on the integration's address keys and
make the failure path inert (no client/connection) until first use, so `[RequiresCapability]` skips.
Keep Web's absolute-URL case documented as a domain exception.
Compat: B/E (public skip behavior changes for missing-address runs).
Tests: one inert-path test per site (address unset → capability absent → run skips; address set →
capability present → works).

**ADDR-2 — Four readers, three precedences.**
Evidence: `ApplicationReadinessInfrastructure.cs:52-53` reads settings then configuration;
`WebSession` and `ProtoDeviceAddress` read settings first; `ProtoHttpClientInitializer.cs:30,53-58`
and `ProtoGrpcClientInitializer.cs:31,59-61` read static configuration only; `AddHttpReadiness`
registered before the settings piece that publishes `BaseUrl` waits for nothing and records
"the application runs in-process" for an application that does not
(`ApplicationReadinessInfrastructure.cs:46-61`; `ProtoReadiness.cs:47-56` documents the registration
order the probe silently depends on).
Why it matters: with a settings-published address and an in-process server still registered (the
"one registration everywhere" goal), readiness gates the run on a published process while HTTP tests
exercise the in-process server; and the trace actively misleads about the mode.
Pattern: one application address concept, four discovery orders, no single authority.
Direction: one named resolver/precedence (`settings → configuration`, transport last) used by
readiness, HTTP clients, web, devices and gRPC; or an `AddHttpReadiness` overload that names the
source it probes. At minimum, the probe must re-resolve after infrastructure starts before it claims
"in-process", and the API remarks must state the ordering requirement.
Compat: B.
Tests: settings-published address + `AddHttpReadiness` before the publisher → the wait happens (or
the evidence names the ordering problem); REST/GraphQL/gRPC resolve the same address readiness
waited for.

**ADDR-3 — Readiness timeout is owned twice.**
Evidence: host probes share `ProtoReadinessOptions` (`ProtoReadinessExtensions.cs:14-22,43,68`);
`ProtoContainerResource` uses its own `ReadinessTimeout`/`ReadinessInterval`
(`ProtoContainerResource.cs:54-57,223-241`) with no public setter; `ProtoReadiness.Http` takes another
`requestTimeout`; the doc says one instance governs every probe of the host
(`ProtoReadinessProbe.cs:19-22`).
Why it matters: the most common slow wait — image boot — is the one case `ConfigureReadiness` cannot
reach; a suite raising the timeout still fails at the container's fixed 30 s.
Direction: pass the run's `ProtoReadinessOptions` into container waits, or expose the container's
timeout through the same options object; bind `ProtoReadinessOptions` from configuration while
touching it.
Compat: E.
Tests: a container-backed probe with a raised `ConfigureReadiness` timeout uses the raised value.

**ADDR-4 — gRPC missing-address message.** Evidence: `ProtoGrpcClientInitializer.cs:85-89` prints the
literal `{app}`; the resolver interpolates elsewhere. Fix the string, assert it in the existing
message test. Compat: B (message only).

## C. Configuration timing and clock identity (root cause: the run's state arrives after the code that needs it)

**CFG-1 — The worker `Program.Main` gap (P1-gap).**
Evidence: `ProtoWorkerHost.StartCoreAsync` merges the overlay at start
(`ProtoWorkerHost.cs:58-62`) but applies it from the `HostBuilding` diagnostic event
(`ConfigureBuilder`, `:123-148`), which fires inside `Build()`
(`HostFactoryResolver.cs:336-339`). The arguments handed to the entry point carry only
`--contentRoot`/`--applicationName` (`ProtoWorkerHost.cs:68-72`). Any `builder.Configuration[...]`
read between `CreateApplicationBuilder(args)` and `Build()` sees the worker's own sources only; a
hosted service at `StartAsync` sees the overlay, which is why the Hosting tests (and the docs'
precedence table) were satisfied.
Why it matters: production-shaped workers read configuration in `Main` (connection string into
`UseNpgsql`); the reference suite did exactly that, hit the gap, and worked around it — a framework
trap that costs every new user an afternoon.
Direction (smallest correct fix): append the merged overlay to the `string[] args` the resolver hands
the entry point as `--{key}={value}` pairs, keeping the `HostBuilding` overlay as the fallback for
entry points that ignore args. `Host.CreateApplicationBuilder(args)` and
`Host.CreateDefaultBuilder(args)` both register a command-line source at construction, so `Main` sees
final-precedence values. A parameterless or arg-ignoring entry point stays a documented Limit.
Compat: E (additive behavior), B for the reference suite's deferred reads (they can then be removed).
Tests: `TestWorker/Program.cs` captures `GetConnectionString("WorkerProbe")` and two precedence keys
**inside `Main`** into a parallel-safe probe; assert options > settings > suite config there as well
as at `StartAsync`; add a parameterless-entry-point boundary test.

**CFG-2 — The reference suite's eager broker read.** Evidence:
`OpenCsms/src/OpenCsms.Messaging/MessagingServiceCollectionExtensions.cs:12-19` evaluates
`configuration["Messaging:RabbitMq:ConnectionString"]` when `AddRabbitMqEventPublisher` is called in
`Api/Program.cs` and `Worker/Program.cs` before `Build`; the run's container settings arrive later, so
the publisher holds `null` and throws at first publish; POST /end cannot complete in container mode.
Direction: product-side fix — resolve `IConfiguration`/a bound options object at first use (the P4f
consumer rule applied to the product), exactly as ProtoTest's `UseRabbitMq` does lazily. This is
required for R1 green and is independent of CFG-1. Compat: B (repository-local).
Tests: container-mode journey returns 200 from `/end` and the suite sees `invoice.issued` on a tap.

**CFG-3 — `ProtoClockLocator` is host-unscoped.**
Evidence: `ProtoRequestClock.cs:43-52` keys a process-global map by test id; `Add` overwrites and
`Remove` is unconditional; removal happens at context disposal
(`ProtoExecutionContext.cs:462-468`). `RunPrefix` is public and two hosts can share it; the repo's own
fixtures already reuse literal ids, so overwrite/cross-removal happens silently (only the absence of a
time assertion hides it). A throw between registration and context construction also leaks the entry.
Why it matters: this is the exact failure `ProtoHostRegistry.FindTraceWriter` was introduced to fix
for spans; the clock reintroduced an unscoped ambient lookup.
Direction: key the locator by host identity plus test id (or clear it in `ProtoHost.DisposeAsync`),
and make the registration/removal failure-safe. Compat: I/B.
Tests: two hosts with the same fixed prefix and one active test each → each finds its own clock; a
failed start leaves no entry.

**CFG-4 — Clock arithmetic and ambient lifetime.**
Evidence: `ProtoClock.Advance` performs a read-modify-write outside the lock; concurrent advances on
the run clock lose updates. `ProtoRequestClock`'s AsyncLocal is restored on scope dispose but not
revoked at test end, so a fire-and-forget task started inside a request keeps the finished test's
clock.
Why it matters: both are silent — the first loses trace deltas, the second shows stale time to
background work. Practical impact is low but the design promised isolation.
Direction: move the whole advance under the lock; either carry the test id in the pushed value and
have `ProtoTestTimeProvider` ignore dead tests, or document the flow limitation in `ProtoClock`'s
Limits. Compat: I/B. Tests: concurrent run-clock advances → final time equals the sum and one trace
event per delta; (observation) stale-ambient test if the guard is built.

## D. Device stack (root cause: two redesigns shipped the model but not the identity/concurrency rules)

**DEV-1 — In-process routing collapses to one transport.**
Evidence: `InProcessWebSocketDeviceHostBuilderExtensions.cs:31` guards on the non-generic
`TryRegisterOnce<InProcessWebSocketRegistration>`; `ProtoDeviceClient.cs:48-54` picks the first
in-process transport whose `CanConnect` is true; `CanConnect` ignores the endpoint and only checks its
own application (`InProcessWebSocketDeviceTransport.cs:24-28`); the client registration carries no
application. So the second `TProgram` is silently dropped, and a client for application B can be
routed through application A's `TestServer` at the same path (both routes exist).
Why it matters: multi-application suites are a first-class model (`AddApplication`); this produces a
silently wrong server, not a failure.
Direction: key the marker by `(typeof(TProgram), applicationName)`, carry the application on the
device client registration, select the transport that matches the client's application, and fail
naming the application when a client expects in-process but none matches. Drop or use the `CanConnect`
endpoint parameter.
Compat: E. Tests: two applications, one client each, assert each device's `device.address`/server is
its own; published fallback still uses the socket.

**DEV-2 — In-process options are dead.** Evidence: the extension registers `WebSocketDeviceOptions`
(`InProcessWebSocketDeviceHostBuilderExtensions.cs:23,30`) but the transport constructs
`new WebSocketDeviceOptions()` (`InProcessWebSocketDeviceTransport.cs:46`), so `ConnectTimeout`,
`ReceiveBufferBytes` and `KeepAliveInterval` apply to the socket path only and validation never runs.
Direction: inject the registered options into the in-process transport (or remove `configure` and say
options are fixed in-process). Compat: E. Tests: a tiny `ConnectTimeout` is observed in-process;
`Validate` rejects a bad value.

**DEV-3 — Connect/disconnect races.** Evidence: `DeviceSession.cs:52-57` checks then assigns
`_connection`; `:94-97` clears it ungated; `:116-117,139-140` snapshot it after `ConnectAsync`.
Concurrent sends open two connections (one leaked, untraced); a disconnect between connect-return and
use can NRE; concurrent readers race for frames.
Direction: one single-flight gate per session (semaphore or task cell) for connect/send/receive, or
explicitly document single-reader/single-writer and fail fast on overlap. Compat: E/I. Tests:
concurrent sends open one connection; disconnect racing send fails with a named device error.

**DEV-4 — Resource id omits the device type.** Evidence: `ProtoDeviceClient.cs:76-80` registers
`device:{client}:{id}` while `For<TDevice>` is keyed per type; the client registry is type-keyed
(`ProtoClientRegistry.cs:20`) but the resource registry throws on the duplicate id
(`ProtoResourceRegistry.cs:24-28`). Direction: include the device type in the resource id (and decide
whether the trace entity should carry it). Compat: E. Test: two device types, one id, one client.

**DEV-5 — Connection state not finalised.** Evidence: `DeviceSession.cs:94-112` writes no entity state
on disconnect; `DisposeAsync` (`:214-222`) bypasses the trace; the tests assert
`device.connected == "true"` at teardown. Direction: set state and emit the disconnect from the
session's release path; assert the final state is false. Compat: E.

**DEV-6 — Hosting registration accepts conflicts and shapes silently.** Evidence:
`ProtoTest.Hosting/ProtoHostBuilderExtensions.cs:34-38` returns early on a name collision without
comparing `TProgram`; `HostFactoryResolver.cs:240-248` invokes the entry point without args when it
declares none, so `--contentRoot`/`--applicationName` are lost;
`ProtoWorkerHost.ConfigureBuilder` (`:137-147`) switches on two builder shapes with no default (a
`WebApplication.CreateBuilder` worker is documented but untested); `ProtoWorkerOptions`'s XML says a
null value stays an empty setting while `ConfigureBuilder` filters nulls out (`:133-135`).
Direction: throw when a name is reused with a different program; validate the entry-point shape at
registration (or document the Limit and test it); match `IHostApplicationBuilder` and throw on an
unknown shape; fix the null semantics. Compat: E. Tests: same name/different program throws; a
`Host.CreateDefaultBuilder` worker and a `WebApplication` worker both get config and clock; a
parameterless entry point is an explicit boundary.

## E. Vocabulary and dead surface (root cause: kinds exist where constants should, promises without consumers)

**VOC-1 — Messaging coverage promise.** `ProtoMessageClient.cs:9-10` documents observations "so
coverage can aggregate destinations", `ProtoMessagingProtocol.Protocol.CoverageCategory` is stored and
never read (`ProtoMessagingProtocol.cs:13-18`), and no Messaging collector exists. Either ship the
collector or delete the promise and the dead property.

**VOC-2 — Literal kinds where constants exist.** `web.page.verified`/`web.page.visited` are literals at
producer and consumer (`WebAssertionPoller.cs:183`, `WebSession.cs:130`,
`WebCoverageCollector.cs:85,99`) while `web.page.available` is a constant
(`WebPageInventory.cs:13`); `graphql.failure` is a literal where REST/gRPC keep internal consts. Give
each protocol one internal/public kind constants type and reference it from both sides; the drift
class is exactly what Audit 3's D4 opened.

**VOC-3 — Dead readiness surface.** `IProtoReadinessProbe` (`ProtoReadinessProbe.cs:10-17`) has no
implementation or consumer; `ProtoReadinessResult.LastError` is populated but every caller records
only attempts/waited. Delete the interface or make it the registration type (not both), and drop or
surface `LastError`.

**VOC-4 — Duplicate poll loop.** `ProtoReadiness.WaitAsync` (`ProtoReadiness.cs:86-123`) reimplements
`ProtoPolling.PollAsync` (`ProtoPolling.cs:31-49`). Implement one over the other; readiness is the
hottest loop and the two will diverge.

## F. Tests, tooling and the release path (root cause: gates that cannot fail)

**TST-1 — Happy-path-only new features.** Named gaps: Main-time worker configuration; bad or
unreachable `BaseUrl` (malformed should fail naming the key, reachable-looking-but-dead should fail
the request naming the address); `AddHttpReadiness` with a bad URL; a second in-process application
(DEV-1); in-process options binding (DEV-2); `DisconnectAsync` and connection-state finality (DEV-5);
concurrent device operations (DEV-3); same-id-two-types (DEV-4); same-name-different-program worker
(DEV-6); container readiness with a raised `ConfigureReadiness` (ADDR-3); two concurrent tests
asserting each sees its own clock (CFG-3). These are the invariants the push skipped.

**TST-2 — Support drift.** Temp-trace create/delete and `FreePort` are copied into six suites
(`TestClockTests.cs`, `ConditionalInfrastructureTests.cs`, `ConditionalCapabilityTests.cs`,
`PublishedApplicationTests.cs`, `DeviceTests.cs`, `ReadinessTests.cs`, `WebSocketDeviceTests.cs`,
`ProtoContainerResourceTests.cs`), and 150–200 ms timing windows are assumptions. Move them into
`tests/ProtoTest.TestSupport` and add a grep check to the lint gate.

**TST-3 — Version collision.** `Directory.Build.props:14` is `1.0.1`, the same as the published
package; 170 commits have diverged. The NuGet global cache is keyed by id/version and already poisoned
one restore (`handoff-r1.md` §5). Direction: give branch builds a distinct version (for example
`1.1.0-alpha.<n>`), make `pack.ps1` refuse to pack the exact published version, and add the repo
`NuGet.config` package-source mapping guidance next to the consumer step.

**TST-4 — The branch never meets CI.** `ci.yml` triggers on `main` only; `verify.yml` is
`workflow_call` with no dispatch; the branch has no remote; no script writes gate evidence. Direction:
allow `version/**` on `ci.yml`/`verify.yml` (or a manual dispatch), add `eng/verify.ps1` that runs
test + lint + check-docs (+ pack when the change touches packaging) and writes
`artifacts/gates/<stage>.json` (HEAD sha, version, counts, exit codes), and quote that in every plan
row. Evidence, not prose.

**TST-5 — Release and docs-gate debt.** `cut-release.ps1:39-44,70-93` never rolls `[Unreleased]`;
`release.yml:81-96` silently falls back to generated notes; `RELEASING.md` is gitignored
(`.gitignore:490`); `check-docs.ps1:14,26-28,130-147` depends on gitignored `assets/internal/docs-facts`
and reports itself skipped, and the removed-symbol gate is a hand list (`:42-64`) while
`CompatibilitySuppressions.xml` already enumerates the deliberate removals; six packages opt out of
package validation with no rollover; `generate-recipe-traces.ps1:30-34` accepts a stale trace after a
zero-match filter; framework packages float `8.0.*`/`9.0.*` with no lock. Direction: roll the
changelog in `cut-release.ps1` and fail when `[Unreleased]` is empty; track `RELEASING.md`; derive the
docs deny list from suppressions or assert against source constants; require each packable project to
validate or carry a recorded reason; guard the recipe-trace script; pin framework package patches.

## G. Reference suite (OpenCSMS) — executed by `eng/plan-5.md` Track R

**REF-1 — Fixed tenant breaks configured mode.** `ChargingSessionsBecomeInvoices.cs:26-39,75-87` uses
`"acme"`, `"Standard"`, `"Single"`, `"Depot 1"`, `"Kiosk"`; the schema has unique `(TenantId, Name)`
indexes for tariffs and stations (`CsmsDbContext.cs:26,32`; migration
`20260925090140_Initial.cs:102-112`). A second run against a persistent database collides.
Direction: a `[CsmsOperator]` `ProtoAttribute` provisioning a tenant/tariff/station per
`context.TestId` (the Northstar pattern), so any test can run repeatedly against any environment.

**REF-2 — Eager broker read (product).** See CFG-2. Fix before any suite rerun: the committed product
cannot pass container mode as-is.

**REF-3 — The asynchronous contract and the framework idioms.** `Setup.cs:24-37` never calls
`AddMessaging`; `AwaitInvoiceAsync` (`ChargingSessionsBecomeInvoices.cs:113-128`) polls REST with
`Task.Delay`; `PostAsync<T>` (`:104-111`) and `:51,93,101` assert with raw `Assert.That` instead of
`Should.HaveHttpStatus`/`ShouldMatchShape`. Direction: compose
`AddMessaging(m => m.CaptureAttachments().UseRabbitMq())` after the application, await
`invoice.issued` on a destination, keep the REST read as a durable side-check, gate with
`[RequiresCapability(ProtoCapabilityKinds.Broker)]` and `[RequiresCapability(ProtoCapabilityKinds.Worker)]`,
and use the fluent assertions so the shape observations exist.

**REF-4 — Product event semantics.** `SessionEndedConsumer.cs:106-110` acks a redelivery without
republishing `invoice.issued`; a failed publish is swallowed; the invoice timestamp uses
`DateTimeOffset.UtcNow` although the worker receives the run clock. Direction: make the publish
idempotent-but-mandatory on redelivery (or use an outbox) and inject `TimeProvider`; add the
duplicate-stop test.

**REF-5 — DLQ assertion needs a decision.** ProtoTest's RabbitMQ tap treats destinations as exchanges
(`RabbitMqProtoMessageConsumer.cs:180-202`) while the product DLQ is a queue
(`RabbitMqTopology.cs:51-63`), so `AwaitAsync("billing.session-ended.dlq")` fails with "exchange does
not exist". Decision needed in plan-5: a support helper consuming the queue, or a framework addition
of `(exchange, routingKey)` addressing. Record the choice; do not leave the R1 promise unowned.

**REF-6 — Suite/product hygiene and README honesty.** Validation tests sit in the journey class; the
layout is flat instead of the spec's `Journeys/Api/Billing/Support`; `Setup` has no sinks, coverage
collector or attachment capture; there is no `COVERAGE.md` and no parallelism policy file; the README
configured recipe omits `Messaging__RabbitMq__ConnectionString` (all declared keys must be configured
for the container to skip), overclaims the modes that exist, and records no gap log; `OpenCsms.Suite`
references Contracts/Domain it does not use; `Migrate()` is duplicated in both mains; the two test
projects use different NUnit majors.

## Remediation plan

Order is dependency and risk: pin first, fix the model, then the consumers, then hygiene. The
reference-suite work is sequenced in `eng/plan-5.md` (Track R) and is listed here so no finding is
unowned.

### Stage A0 — Characterization tests (no production change)

- REG-1/REG-2: two named servers, one configured address; two conditional declarations of one
  descriptor, one satisfied; register Playwright + Selenium and observe the capability set.
- REG-3: each registration entry after `Build()` — assert today's mixed behavior before changing it.
- CFG-1: capture configuration inside `TestWorker`'s `Main` (the test that should fail today).
- CFG-3: two hosts with a fixed `RunPrefix` and one active test each.
- DEV-1..DEV-4: two in-process applications; two device types with one id; concurrent sends; option
  binding in-process.
- REF-1: second `dotnet test` against one configured database (or the equivalent fixed-name collision
  test) before the suite is rewritten.

⛳ Checkpoint A0 — every characterization test fails (or pins) as the register says; correct the
register if not. No production changes.

### Stage A1 — Registration semantics (Core + capability owners)

- REG-1: per-declaration precedence; skip a descriptor only when every declaration is satisfied or
  the integration is inert; name the deciding declaration in the skipped-capability record.
- REG-2/REG-5: per-instance descriptors (server name, device transport) and capability declared only
  by the winning registration; fix the browser-backend pair.
- REG-3: one post-`Build` rule across all public entries.
- REG-4: canonical key equality at the registration boundary.
- Flip A0's characterizations; add the per-site tests.

### Stage A2 — Address authority and readiness

- ADDR-1: the consumer rule for Sql, Messaging, REST/GraphQL/gRPC (Web documented as a domain
  exception); inert paths and conditional capabilities.
- ADDR-2: one address precedence used by readiness, HTTP clients, gRPC, web and devices; fix the
  `AddHttpReadiness` ordering evidence.
- ADDR-3: one readiness timeout owner; ADDR-4: message fix.

⛳ Checkpoint A2 — the reference suite's "resolve at use time" claim can be written truthfully.

### Stage A3 — Configuration timing and clock identity

- CFG-1: worker args overlay + Main-capture test + documented no-args limit; update `hosting.md`,
  changelog and the fact sheet.
- CFG-3/CFG-4: host-scoped clock locator, failure-safe registration, locked advance.
- Decision recorded: the reference suite may then delete its deferred reads (or keep them and the
  Limit stays documented for `Program.Main` shapes the args fix cannot reach).

### Stage A4 — Device stack correctness

- DEV-1..DEV-6 with their tests; then freeze the device surface until R2 needs more (no new device
  features while these are open).

### Stage A5 — Vocabulary and doc drift

- VOC-1..VOC-4; fix the dead names in `docs/integrations/devices.md`, the WebSocket README and
  `eng/feature-plan.md`; add a docs-gate check that named API symbols exist in source (start with an
  `Add*` method-name check, since it catches the whole class).

### Stage A6 — Test gaps and support consolidation

- TST-1 per-feature missing tests; TST-2 shared helpers and a lint grep; timing windows replaced with
  deterministic waits where possible.

⛳ Checkpoint A5 — re-assess the register: any finding not fixed must carry a Decisions entry.

### Stage A7 — Release and evidence hygiene

- TST-3: branch version distinct from published; `pack.ps1` guard; consumer guidance.
- TST-4: CI on `version/**` (or dispatch) and `eng/verify.ps1` writing gate evidence; protocol for
  quoting it in plan rows.
- TST-5: changelog rollover, tracked `RELEASING.md`, docs deny list from suppressions, package
  validation rollover plan, recipe-trace guard, framework patch pins.

### Stage A8 — Reference suite R1a/R1

Executed by `eng/plan-5.md` Track R, using REF-1..REF-6 as the finding list. Exit: the first journey
green in container and configured modes with no code change between them, `COVERAGE.md` linked, README
honest, plan-4 rows committed.

## Decisions taken

- The two audit-3 carry-overs (X1 template runner variants, X2 static trace index) stay in plan-4 and
  are sequenced by plan-5; this audit does not reopen them.
- REG-1 is fixed as a correctness bug, not by weakening the capability contract; if the per-instance
  descriptor rule is rejected during implementation, the alternative must make multi-instance
  conditionals impossible to register.
- CFG-1 is fixed (args overlay) rather than documented, because the reference suite proved the cost;
  the parameterless-entry-point residue is the only documented limit.
- ADDR-1 is adopted site by site as plan-4's P4f consumer rule already demands; Web's absolute-URL
  case is the one documented exception.
- TST-3 changes the branch version; this is a repository-local change, not a package API change.
- The engineering fact base moves to a tracked location (`eng/facts/`) because `assets/internal/` is
  gitignored; the old `docs-facts/` set stays as an archived docs-rework input, not guidance.

## Things I would NOT change

- The run/test state machines, `ProtoResourceRegistry`'s shape, `ProtoTestScope`'s teardown swallow,
  `ProtoExecutionContext` as one facade, the ambient `Proto.Context` model, attributes as the
  lifecycle mechanism, per-adapter `MapResult`, the trace wire schema and viewer contract,
  `AdapterContract` and the shared doubles (all Audit 2/3 decisions; nothing in this audit gives a
  reason to reopen them).
- Skip-before-ownership in `Build` (`ProtoHostBuilder.cs:120-131`) — it is the right shape; only the
  capability side needs the same per-declaration care.
- The `IProtoConfiguredInfrastructure` default-interface seam — additive, and the only way an
  existing `IProtoInfrastructure` keeps compiling (the fabricated empty context for non-host callers
  is a documentation fix, not a redesign).
- The per-test clock design, readiness-as-infrastructure, the vendored `HostFactoryResolver`
  (MIT-attributed), and the one-registration/two-mode device model from P4e — all three survived this
  audit; they are the direction, executed incompletely.
- The reference suite's separate repository and the "no mode conditionals" goal; the problems are
  execution, not shape.

## New problem classes discovered

1. **Declarative registration semantics as a class:** condition precedence, instance identity and
   post-build guards were each solved locally and each differently; the model needs one rule set
   (conditions are per declaration, identity is per instance, `Build` is terminal).
2. **Identity collapse through value equality:** records that represent multi-instance registrations
   lose the instance; the fix is a per-instance key, not a bigger record.
3. **Registration-time configuration visibility as a lifecycle contract:** "when does the run's state
   become visible to the code it owns" was never written down; `Main` vs `StartAsync` vs `Build`
   differ silently.
4. **Host-unscoped ambient lookups reappeared** (the clock locator) after `FindTraceWriter` had
   already solved the same problem for spans — a design-memory problem, which is what the fact base
   exists to fix.
5. **Local-only fact bases and prose gate claims:** the only engineering facts were gitignored and
   stale, so every session re-derived the model; plan rows assert gates that no artifact backs.
6. **Docs that cannot fail:** a deleted method name, a wrong coverage target and a stale README
   shipped and stayed green because `check-docs` cannot see API names or skipped its fact check.

## Stop criteria

- Multi-instance and mixed-conditional registrations behave per the locked decisions, proven by tests;
  no path can silently skip a capability while its integration is live.
- The consumer rule is adopted wherever an integration can be inert; missing addresses produce skips,
  not setup failures (Web's absolute-URL case excepted and documented).
- A worker reading configuration in `Main` sees the run's settings, or the Limit is documented and
  pinned by a test; the clock lookup is host-scoped.
- The device stack passes its identity, routing, options and concurrency tests; the device surface is
  frozen until R2.
- No user-facing artifact teaches a symbol that does not exist; the docs gate can catch the class.
- Branch packages are distinguishable from published ones; CI sees the branch; each plan row quotes
  gate evidence.
- The reference suite's first journey is green in both modes with no code change, `COVERAGE.md` is
  linked, and the README's claims match what runs.
- Then stop. Do not refactor further because another design exists.
