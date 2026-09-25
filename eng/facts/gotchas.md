# ProtoTest engineering facts — gotchas

Verified traps, each with the action to take. Items marked **→ AUDIT** are open findings in
`eng/audit-plan-4.md`; once fixed, replace the entry with the new behavior and keep the history in the
audit plan.

## Configuration timing

- **A worker's `Program.Main` cannot read the run's configuration before `Build()`.** The overlay is
  applied at the `HostBuilding` event, which fires inside `Build()`; only hosted services at
  `StartAsync` see it. Read connection strings from `IConfiguration` inside an options factory, or
  wait for the audit CFG-1 args-overlay fix. **→ AUDIT CFG-1.**
- **Eager configuration reads at registration are wrong by construction.** `AddX(configuration[...])`
  in a `Program.Main` captures the value before the run's settings exist. Resolve the address at use
  time (the P4f consumer rule) — this is what made the reference product's publisher inert.
  **→ AUDIT CFG-2 / REF-2.**
- **Static configuration decides `AddAspNetCoreServer`'s step-aside, not settings.** A settings-published
  address does not step the in-process server aside (decision, Northstar compatibility). Do not assume
  the two agree. **→ AUDIT ADDR-2.**
- **A suite's configuration has no environment source unless it adds one.** `ConfigureAppConfiguration`
  composes exactly the sources it is passed; a configured-mode recipe's exported keys are invisible
  until the setup adds `.AddEnvironmentVariables()`. Without it the containers still start and the
  product still reads the exported addresses, so a green run proves nothing about the mode. (Found by
  the OpenCSMS run, fixed in its `2cb949f`; R1a 1.7.)

## Capabilities and registration

- **A capability is dropped when *one* conditional declaration is satisfied, even if another remains
  unsatisfied.** Two named servers sharing the `("ASP.NET Core", server, ...)` descriptor: configure
  one `BaseUrl` and `[RequiresInProcess]` skips tests against both. **→ AUDIT REG-1.**
- **Capabilities that describe an instance must carry the instance.** `AddCapability` dedupes by value,
  so two servers/backends collapse into one capability. **→ AUDIT REG-2.**
- **`Build()` is terminal, but only some entries say so.** `ConfigureServices`-based entries
  (hooks, gates, capabilities, clock) silently no-op after `Build`. **→ AUDIT REG-3.**
- **First-wins guards can hide a conflict.** `AddWorkerHost` with the same name and a different program
  is ignored; the second in-process device transport is dropped by one non-generic marker.
  **→ AUDIT DEV-1 / DEV-6.**
- **A conditional declaration with a `Keys` list never dedupes** (record equality on a collection).
  **→ AUDIT REG-4.**

## Addresses and readiness

- **Address precedence differs by integration:** readiness/Web/devices read settings then
  configuration; REST/GraphQL/gRPC read static configuration only; transport fallback comes last.
  Do not assume one reader sees what another resolved. **→ AUDIT ADDR-2.**
- **`AddHttpReadiness` registered before the piece that publishes the address waits for nothing and
  records "runs in-process"** for an application that does not. **→ AUDIT ADDR-2.**
- **Container readiness ignores `ConfigureReadiness`.** `ProtoContainerResource` owns a private
  30 s timeout; a slow image cannot be tuned. **→ AUDIT ADDR-3.**
- **The consumer rule is adopted only by `AddAspNetCoreServer`.** Sql fails in setup; RabbitMQ at
  setup/first use; HTTP at first use; a missing address must mean "inert + capability absent", not a
  setup failure. **→ AUDIT ADDR-1.**
- **Containers must declare every key they fill.** `AddInfrastructure` skips only when *all* declared
  keys are configured; a missing one starts the container anyway (a configured CI without Docker then
  fails). Check the README recipes for all keys.

## Messaging

- **A tap misses messages published before its destination is prepared.** `UseRabbitMq` declares the
  destinations listed in `ProtoTest:Messaging:Destinations:<n>` during test setup; any other destination
  is declared at the first `AwaitAsync`, so an act-then-await flow loses a message the act published.
  Pre-bind every destination the act publishes to. (Canonical:
  `tests/ProtoTest.Messaging.RabbitMq.Tests/RabbitMqTests.cs`; used by OpenCSMS `Setup.cs`.)
- **`UseRabbitMq` declares the `Broker` capability unconditionally, and a pre-bound destination connects
  at test setup.** A missing broker address cannot be expressed as a skip yet, and even tests that never
  touch messaging fail setup rather than skip. **→ AUDIT ADDR-1.**
- **`ProtoMessage` carries the exchange as `Destination` and drops the routing key.** Taps bind
  destinations as exchanges, so a queue (a dead-letter queue) or an `(exchange, routingKey)` pair cannot
  be awaited through the framework. Use a raw `RabbitMQ.Client` helper until the recorded REF-5 addition
  ships (canonical: OpenCSMS `tests/OpenCsms.Suite/Support/RabbitMqRawClient.cs`).

## Clock and time

- **`ProtoClockLocator` is process-global and keyed by test id alone.** Two hosts with the same
  `RunPrefix` overwrite/remove each other's mapping; use distinct prefixes or wait for the host-scoped
  fix. **→ AUDIT CFG-3.**
- **`ProtoRequestClock`'s ambient is restored, not revoked.** A fire-and-forget task started inside a
  request keeps the finished test's clock; long-lived background work should read the run clock.
  **→ AUDIT CFG-4.**
- **Only `GetUtcNow` is virtual in `ProtoClock`; timers are real, and `DateTime.UtcNow` is unaffected.**
  Application code that calls `DateTime.UtcNow` directly will not see the test clock; use
  `TimeProvider`.

## Devices

- **One in-process device transport registers, ever.** A second `AddInProcessWebSocketDevices<TProgram>`
  is silently dropped, and `CanConnect` ignores the endpoint, so a client for application B can be
  routed through A's `TestServer` when both expose the same path. **→ AUDIT DEV-1.**
- **The in-process path ignores `WebSocketDeviceOptions`.** `configure` is registered and never read;
  validation never runs. **→ AUDIT DEV-2.**
- **`DeviceSession` connect/disconnect is not concurrency-safe.** Concurrent sends can open two
  connections (one leaked, untraced); a disconnect racing a send can NRE. Serialize per session or
  wait for the fix. **→ AUDIT DEV-3.**
- **Device resource ids omit the device type**, so two device types with the same id collide in one
  test. **→ AUDIT DEV-4.**
- **`device.connected` is not finalised at test end** unless the test calls `DisconnectAsync`.
  **→ AUDIT DEV-5.**

## Tests and parallelism

- NUnit test projects that already run in parallel link `tests/NUnitParallelization.cs`; the projects
  that stay single-threaded do so because they share run-scoped resources (gRPC state was fixed; Sql,
  Testcontainers and RabbitMQ share containers; Web shares browser pools; SampleApp.Domain shares
  in-memory state). A new parallel suite needs an isolation story before it links the file.
- Adapter tests must go through the runner (`dotnet test`). Tests that call lifecycle hooks directly
  hide runner-integration breaks (Audit 3 class 7).
- Registration-shape and report-markup tests are labelled `[Category("Characterization")]`; keep the
  label, they are deliberate refactoring brakes.
- Shared doubles live in `tests/ProtoTest.TestSupport`. Do not copy temp-trace or `FreePort` helpers
  into a new suite (audit TST-2).
- **Parallel safety rests on per-test ownership, not on the runner policy.** Provisioning names from
  `context.TestId` (the `[CsmsOperator]` pattern) and predicates on test-owned ids are what make
  `ParallelScope.All` safe; the default id generator's random six-digit run prefix also keeps reruns
  against a persistent database collision-free. A shared fixture or a fixed identifier reintroduces the
  repeatability bug (REF-1).

## Versioning, feeds and gates

- **Branch packages must never reuse the published version.** `Directory.Build.props` carries
  `1.1.0-alpha.<n>` and `eng/pack.ps1` refuses to pack the `PackageValidationBaselineVersion`
  (`4bafa6d`). The old collision (a locally-packed `prototest.* 1.0.1` in the global cache made package
  validation compare the package against itself) is the reason: local consumers use package-source
  mapping and clear `~/.nuget/packages/prototest.*` after a repack.
- **The branch never meets CI.** `ci.yml` now includes `version/**` and a manual dispatch, but a
  locally run stage still needs `eng/verify.ps1 -Stage <name>` (add `-Pack` when packaging changed)
  so the evidence is recorded. **→ AUDIT TST-4.**
- `eng/pack.ps1` is the per-stage pack gate; it verifies the packable set, READMEs, dependency edges
  and PDB/DLL pairs. Run it whenever packaging changes, then re-pack for consumers before re-running
  their restore.
- `eng/check-docs.ps1`'s config-key cross-check depends on the gitignored `assets/internal/docs-facts`
  and skips itself in CI; it cannot see whether a method name exists. Do not trust it for API
  existence. **→ AUDIT TST-5.**
- Six packages (`Hosting`, `Devices*`, `Web.Pages`) opt out of package validation with no rollover plan;
  at 1.1 they flip to a new baseline. **→ AUDIT TST-5.**
- `RELEASING.md` is gitignored; `cut-release.ps1` does not roll `[Unreleased]`. **→ AUDIT TST-5.**

## Repo hygiene

- `assets/internal/` is gitignored (`.gitignore:488`). Its `docs-facts/` set was written at `db9d7aa`
  against `0.1.0-alpha` and is **archived input, not guidance**. The `.opencode/skills` file references
  several `assets/internal/*` files that no longer exist for that reason. Engineering facts now live in
  `eng/facts/` (tracked).
- The viewer must not be started from an agent shell (a detached dev server keeps the session
  attached and looks like a hang). See `.opencode/skills/prototrace-design/SKILL.md`.
- The docs site and viewer have their own stylelint/typecheck gates; run them for UI changes.
