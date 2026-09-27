# ProtoTest engineering facts — gotchas

Current at branch `version/1.1`, HEAD `7d1a484`.

Verified traps, each with the action to take. The entries describe the code as it stands; the plans and
audits carry the finding history.

## Configuration timing

- **A worker's `Program.Main` sees the run's configuration as arguments.** `AddWorkerHost` hands the
  merged overlay (options over infrastructure settings over suite configuration) to the entry point as
  `--{key}={value}` pairs, so `Host.CreateApplicationBuilder(args)`/`Host.CreateDefaultBuilder(args)`
  see final-precedence values before their `Main` code runs; the `HostBuilding` in-memory overlay stays
  as the fallback at `Build()`. A parameterless `Main` or one that does not pass its args to the
  builder cannot see the overlay before `Build()` - an options factory or hosted service still does -
  and it also does not receive `--contentRoot`/`--applicationName`, so it reads its own appsettings
  from the test process's content root. Build the host from `args` when `Main` itself reads
  configuration. The two identity switches are reserved: an overlay key named `contentRoot` or
  `applicationName` (any casing) produces no argument, so the generated pair is never replaced.
- **`ProtoWorkerOptions.Set(key, null)` is an empty setting, not a dropped key.** `Set` stores an
  empty value, so the worker sees the key as `""` in `Main` (command line) and at `Build` (in-memory
  overlay) and a suite can deliberately clear a value the run provides. Nulls from the run's own
  configuration are still not passed: the overlay carries no setting for them.
- **A worker builder shape ProtoTest does not recognise fails loudly.** `Host.CreateApplicationBuilder`
  and `WebApplication.CreateBuilder` (both `IHostApplicationBuilder`) and `Host.CreateDefaultBuilder`
  (`IHostBuilder`) get the overlay and the run's clock; any other builder throws naming its type
  instead of silently keeping its own configuration and `TimeProvider`.
- **Eager configuration reads at registration are wrong for addresses the run can provide later.** A
  worker's `Main` sees the run's static overlay, but a started piece's published settings are resolved
  at use time, and a process that also runs standalone reads its own environment. Resolve the address
  at use time (the consumer rule): a product that captures it at registration stays inert in container
  and configured modes.
- **Static configuration decides `AddAspNetCoreServer`'s step-aside, not settings.** A settings-published
  address does not step the in-process server aside (decided asymmetry); the address readers still
  follow the published address, so give a published process its own application name when both must
  coexist (the demo's standalone console is its own application).
- **A suite's configuration has no environment source unless it adds one.** `ConfigureAppConfiguration`
  composes exactly the sources it is passed; a configured-mode recipe's exported keys are invisible
  until the setup adds `.AddEnvironmentVariables()`. Without it the containers still start and the
  product still reads the exported addresses, so a green run proves nothing about the mode. (Found by
  the OpenCSMS run, fixed in its `2cb949f`; R1a 1.7.)
- **Run metadata is captured at `Build()` straight from the process environment, not from the suite's
  configuration.** `ProtoTraceOptions.RunMetadata`/`RunMetadataEnvironmentVariables` (A5.15) read once
  when the host is built; a named variable that is unset or empty contributes nothing, an explicit value
  wins, a built-in `environment.*` key is rejected, and values are recorded as-is - list only variables
  safe to carry in a trace.

## Capabilities and registration

- **Conditions are per declaration, not per descriptor.** A capability drops only when every
  conditional declaration for it drops and no plain (unconditional) declaration exists. Two named
  servers with one `BaseUrl` configured: the configured server steps aside, the other stays in-process
  and its capability stays. The `capability.skipped` event names the deciding keys (`capability.keys`)
  and the reason (`capability.reason`).
- **`AddCapabilityWhenProvided` is the missing-address half of the rule.** It drops when none of its
  keys is provided, where provided means a configured value or a key a registered infrastructure piece
  declares - including a piece the build skips because configuration already fills its keys. Use it
  when an address must exist for the integration to serve; `AddCapabilityUnlessConfigured` is the
  environment-provides-it-elsewhere half.
- **Capabilities that describe an instance carry it** (`ProtoCapabilityDescriptor.Instance`). Two
  named servers are two capabilities and two run entities (`server:ASP.NET Core:A`), so configuring
  one address drops only that server's capability. `HasCapability(kind)` matches any instance;
  `HasCapability(kind, name)` matches the descriptor `Name`, not the instance.
- **Only the web backend that wins the first-wins registration declares a browser capability.** The
  losing Playwright/Selenium backend declares nothing, so `[RequiresCapability(browser, "Selenium")]`
  cannot pass while Selenium never registered.
- **`AddInProcessWebSocketDevices<TProgram>(application)` declares its transport capability
  conditionally on the application's `BaseUrl`.** A published application drops the capability (the
  socket transport serves); the capability only exists while the application is hosted in-process.
- **`Build()` is terminal for every public registration entry that would mutate composition.** Hooks,
  gates, capabilities, the clock, sinks, infrastructure and application entries throw the same
  single-build message after `Build`; a captured application builder throws too. Internal registration
  during `Build` uses the fields directly, so it stays composable. A repeated registration that is a
  no-op by design before `Build` (the same server or worker name) stays a no-op.
- **A conditional declaration's key set compares by content** (ordinal, duplicates removed,
  order-independent), so registering the same declaration twice in any order leaves one declaration.
- **First-wins guards can hide a conflict.** `AddWorkerHost` now compares the program: a repeated name
  with a different program throws instead of being ignored. `AddAspNetCoreServer(name)` still wins by
  name alone (A1R-01 residual): a different `TProgram` under one server name is a silent no-op.
  The in-process device transport compares `(TProgram, application)` (audit DEV-1 fixed).

## Addresses and readiness

- **One address precedence: published settings → configuration, transport last.**
  `ProtoApplication.ResolveSetting` (public forms `BaseUrl(context, app)` / `GrpcAddress(context, app)`)
  is the only resolver readiness, REST/GraphQL/gRPC clients, web sessions and device clients use.
  Configuring `BaseUrl` and starting a piece that publishes it are different decisions: the published
  address wins at use time.
- **`AddHttpReadiness` must be registered after the piece that publishes the address.** Probes are
  awaited at their registration position; a probe registered first records `readiness.skipped` naming
  the ordering and the later key instead of claiming "in-process". A truly in-process application
  records "in-process"; an application with neither an address nor an in-process server records both
  gaps. A configured address that is not an absolute HTTP/HTTPS URL fails the run start naming
  `ProtoTest:Applications:{app}:BaseUrl` instead of being probed into a timeout.
- **One readiness policy owns every wait.** `ConfigureReadiness`/`ProtoTest:Readiness` set
  `ProtoReadinessOptions`, which governs host probes and every container the run starts through
  `ProtoInfrastructureContext.Readiness`; a container started outside a host keeps its own
  `ReadinessTimeout`/`ReadinessInterval`. `ProtoReadiness.WaitAsync` itself rides
  `ProtoPolling.PollAsync`, so readiness shares the one interval/deadline loop with every other wait
  (VOC-4 fixed); exceptions still mean "not ready yet" and the timeout message names the probed URL and the
last error.
- **The consumer rule is adopted by `AddAspNetCoreServer`, the in-process device transport,
  `UseRabbitMq`, `AddSql` (with `SqlOptions.AddressKeys`) and `AddEntityFrameworkCore` (same keys).**
  A missing address means "inert +
  capability absent" and tests skip. The HTTP family is the recorded A2b exception: one protocol
  capability covers every client and an application-scoped client is legitimately served in-process,
  so a missing address still fails at first use with the resolver message. Web's absolute-URL sessions
  stay the documented domain exception.
- **`SqlOptions.AddressKeys` is code-declared, not configuration-bindable.** `SqlAddressKeys.Add` (or a
  `SqlOptions` instance registered before `AddSql`) is the only way in; the `ProtoTest:Sql:AddressKeys`
  configuration section is ignored, because the Build-time capability decision committed the run to the
  code-declared keys, and a key configuration adds later would make the runtime rule disagree with it.
  `AddEntityFrameworkCore` follows the keys `AddSql` recorded when it is called after `AddSql`; called
  first, it keeps the unconditional capability and the SQL keys are not part of its decision.
- **Containers must declare every key they fill.** `AddInfrastructure` skips only when *all* declared
  keys are configured; a missing one starts the container anyway (a configured CI without Docker then
  fails). Check the README recipes for all keys. A started piece whose keys are only partly configured
  must not publish the configured ones: `AddAspireAppHost` publishes only the declared resource keys
  configuration does not fill, because a published setting wins over configuration at use time (a
  multi-resource AppHost in a mixed run would otherwise mask the environment's address).
- **A containerized application declares no `server` capability.** `ApplicationContainer` publishes an
  address, not an in-process server: `[RequiresInProcess]` skips with its default reason, and a suite
  needing the test host gives the published instance its own application name.

## Messaging

- **A tap misses messages published before its destination is prepared.** The adapter declares the
  destinations registered during test setup: `ProtoMessagingBuilder.Tap(...)` in code, plus the
  `ProtoTest:Messaging:Destinations:<n>` configuration entries (configuration binds after code and the
  prepared set is deduped). Any other destination is declared at the first `AwaitAsync`, so an
  act-then-await flow loses a message the act published. Pre-bind every destination the act publishes
  to. (Canonical: `tests/ProtoTest.Messaging.RabbitMq.Tests/RabbitMqTests.cs`, the
  `Tap_ShouldPreBind...` round trip and `tests/ProtoTest.Messaging.Tests/MessagingTapTests.cs`.)
- **Awaits on one consumer serialize, and an unmatched delivery is kept.** The shared concurrency
  contract: concurrent awaits on one consumer run in call order, and a delivery matching no awaited
  predicate stays buffered for a later await, so nothing is lost or stolen. Both adapters follow it
  (canonical: `tests/ProtoTest.TestSupport/MessagingConcurrencyContract.cs`, called by the in-memory
  and RabbitMQ suites).
- **`UseRabbitMq` declares the `Broker` capability conditionally on
  `ProtoTest:Messaging:RabbitMq:ConnectionString`.** A run with a configured key or a broker container
  that declares it keeps the capability; with neither it is absent and gated tests skip instead of
  failing setup/first publish. A callback that sets `RabbitMqOptions.ConnectionString` in code provides
  the address without a key and keeps the capability unconditional. An adapter registered with the
  key-less `UseBroker(factory)` overload keeps the unconditional declaration too.
- **`Declare` creates the destinations the suite owns; `Tap` only binds them.** `Declare(...)` on the
  `AddMessaging` chain (or `ProtoTest:Messaging:DeclaredDestinations`) is read by the messaging client
  initializer before any tap is prepared; RabbitMQ declares each as a fanout, durable, non-auto-delete
  exchange once per run (an existing one is left as is, a repeat is a no-op), and the in-memory broker
  treats a declaration as a no-op. A refused declaration fails setup with the destination named; a tap
  nobody declares still fails only the tests that await it. `IProtoMessageBroker.DeclareAsync` is the
  adapter seam; its default throws `NotSupportedException` naming the adapter. (Canonical:
  `RabbitMqTests.Declare_Should...`, `tests/ProtoTest.Messaging.Tests/MessagingDeclareTests.cs`.)
- **A pre-bound destination still connects at test setup when the capability is present.** A destination
  whose exchange cannot be declared fails only the tests that await it, with the named error — not the
  whole class at setup — because each tap owns its channel.
- **`ProtoMessage` carries the exchange as `Destination` and drops the routing key.** Taps bind
  destinations as exchanges, so a queue (a dead-letter queue) or an `(exchange, routingKey)` pair cannot
  be awaited through the framework. Use a raw `RabbitMQ.Client` helper until the recorded REF-5 addition
  ships (canonical: OpenCSMS `tests/OpenCsms.Suite/Support/RabbitMqRawClient.cs`).
- **Messaging observations are evidence, not coverage.** The package ships no collector and the
  protocol descriptor carries no coverage category (A5 VOC-1 decision): the `messaging.published`,
  `messaging.receive` and `messaging.contract.shape` observations reach a report only through a collector
  a suite registers, and destinations are never aggregated by ProtoTest itself. (`messaging.publish`
  remains the operation name.)
- **Message reads are typed but JSON-only.** `message.ReadAsJson<T>()` reuses
  `ProtoJsonDefaults.Reader` and stays nullable; `ReadRequired<T>()`/`ReadRequired<T>(path)` throw
  `MessagingAssertionException` naming the destination for an empty payload, JSON `null` or a missing
  path, and a wrong type still throws the deserializer's `JsonException`. `Payload` stays for raw
  inspection, and the UTF-8-string limit is unchanged. Unlike REST/GraphQL required reads, a messaging
  read records no deserialize trace event (no new messaging vocabulary; the await already traces the
  payload section).

## Clock and time

- **One `ProtoClockRegistry` belongs to each host.** `ProtoHost.FindClock(testId)` is an instance
  lookup on the owning host, and the in-process ASP.NET request linking pushes the owning host's
  clock; two hosts that share a `RunPrefix`/test id each resolve their own clock, a failed test start
  removes its entry, context disposal removes it, and host disposal clears the registry. There is no
  process-global clock map (CFG-3 fixed).
- **`ProtoClock.Advance` moves under the clock's lock.** Concurrent advances on the run clock sum
  instead of losing updates, and each advance records its own `clock.advance` (CFG-4 fixed).
- **`ProtoRequestClock`'s ambient is restored, not revoked** (documented, CFG-4). A task started
  inside a request captures the pushed clock in its own execution context and keeps it after the
  scope and the test end; long-lived background work must read the run clock.
- **Only `GetUtcNow` is virtual in `ProtoClock`; timers are real, and `DateTime.UtcNow` is unaffected.**
  Application code that calls `DateTime.UtcNow` directly will not see the test clock; use
  `TimeProvider`.

## Devices

- **In-process transports are keyed by `(TProgram, application)`.** Two applications, or two programs,
  are two transports; `IProtoInProcessDeviceTransport.CanConnect(context, applicationName)` answers for
  the requested application and the server factory it needs, so a client for application B is never
  routed through application A's `TestServer` at the same path. A path-only client whose application
  has no matching transport fails naming the application instead of trying another transport.
- **The in-process path uses the registered `WebSocketDeviceOptions`.** They resolve from DI with the
  transport (so `Validate` runs and a bad value fails when the device is created), and `ConnectTimeout`
  bounds the in-process connect like the socket path. `ReceiveBufferBytes` feeds the shared
  `WebSocketDeviceConnection` both paths use; `KeepAliveInterval` is a client-socket option applied on
  the socket path only, so the in-process transport does not use it.
- **`DeviceSession` has one conversation contract.** Connect is single-flight, sends are serialized,
  one receive may be in flight (a second fails fast naming the device), and a send that races a
  disconnect fails with a device error naming the device instead of a disposed-socket exception.
  A connect that fails or is canceled is cleared, so the next use starts a fresh attempt; only a caller
  that stops waiting leaves the shared in-flight connect cached.
  Sends and receives may run concurrently; do not fan out readers over one device.
- **Device resource and entity ids include the device type**: `device:{client}:{type}:{id}`. Two device
  types with one id on one client coexist, each with its own resource and entity.
- **The release path is a disconnect.** Teardown disposes the connection, writes
  `device.connected = false` and emits `device.disconnect`, so a test that never disconnects still ends
  with a final disconnected state; an explicit `DisconnectAsync` then a send reconnects and records
  both connects.
- **Container and device release are bounded.** An in-flight container start is awaited at most five
  seconds (`container.start.abandoned`), and a device connect the same way
  (`device.disconnect.abandoned`); normal release with nothing in flight is unchanged.
  `IProtoDeviceTransport.ConnectAsync(context, endpoint, cancellationToken)` carries the context, so an
  in-process connect works from a flow without ambient `Proto.Context`.
- **Selenium actions verify the resulting state.** `Check`/`SelectOption` fail with
  `WebActionabilityException` when the click did not take, matching Playwright; a stub-driver test is
  the proof where no real driver is installed.
- **Sheet integer reads are strict.** A fractional, out-of-range or NaN numeric cell fails naming the
  cell instead of rounding or saturating; whole in-range values convert.
- **Sheet record models construct through the constructor their columns map**, and a throwing
  constructor guard propagates with its original stack; an unmapped constructor parameter fails naming
  it. In `ProtoTest.Data`, a declared optional constructor-parameter default wins over a generated value.
- **Framework routes are never pages, and a table row asserts on its own context.** `/_…` and
  `/.well-known…` paths are excluded from the in-process page inventory whatever their content type,
  and `ProtoTableRow`'s shape assertion uses the table's context, so it works on a flow without ambient
  `Proto.Context`.
- **Some caches are process-lifetime by design.** The source-locator maps,
  `ProtoReadiness.SharedHttpClient` and `ProtoDocumentSource.SharedClient` are bounded by the
  assembly/source set and are not evicted (A5-27 decision; the register records the scope limit).

## Tests and parallelism

- NUnit test projects that already run in parallel link `tests/NUnitParallelization.cs`; the projects
  that stay single-threaded do so because they share run-scoped resources (gRPC state was fixed; Sql,
  Testcontainers and RabbitMQ share containers; Web shares browser pools; SampleApp.Domain shares
  in-memory state). A new parallel suite needs an isolation story before it links the file.
- Adapter tests must go through the runner (`dotnet test`). Tests that call lifecycle hooks directly
  hide runner-integration breaks (Audit 3 class 7).
- Registration-shape and report-markup tests are labelled `[Category("Characterization")]`; keep the
  label, they are deliberate refactoring brakes.
- Shared doubles and helpers live in `tests/ProtoTest.TestSupport` (`TemporaryTrace`,
  `TestNetworking.FreePort`, `SingleConnectionListener`). `eng/lint.ps1` fails a local identifier whose
  name contains the known roots (`FreePort`, `ServeOnceAsync`, `TemporaryTrace`,
  `SingleConnectionListener`) outside that project, so `GetFreePort` or `LazyTemporaryTrace` cannot
  drift back either (audit TST-2/A5-64). The rule is a deny list, not a shape scan: a helper renamed
  completely away from those roots (say `AcquirePort`) is not detected.
- **A substituting test pays a second server start.** The dedicated per-test server is built with the
  replacement before the build; a shared per-run server is never reconfigured, so one test's override
  cannot leak into the next. Substitution in published mode throws naming the address; closed-box
  harnesses (Aspire) cannot be substituted — choose `AddWorkerHost` white-box tests or Aspire topology
  runs, not both on one application. The unnamed `[ReplaceService]`/`[FailDependency]` gate follows the
  test's selected application (falling back to `Default`), the same resolution the substitution uses, so
  a mixed run that publishes the selected application skips instead of failing setup; a named one gates
  on its named server. The dedicated server is owned before it starts, so a registration that throws
  once the test started releasing cannot leak a started server.
- **A worktree checkout has no built SPA.** `samples/ProtoTest.SampleApp/Ui/dist` is untracked, so the
  demo's console journeys skip in a worktree and a green worktree gate does not cover them; run those
  tests in the main checkout (or build the console there) before trusting a gate on setup or attribute
  changes.
- **The gate fixtures execute the repository's own MTP binaries.** Run the `scripts` gate after the
  suite, never beside it: two concurrent runs of the same MTP project race its obj caches and produce
  an intermittent failure (one gate run failed this way on 2026-09-26). `eng/verify.ps1` keeps the two
  sequential for this reason, and `eng/test.ps1` logs every project to `artifacts/test-logs/`.
- The MTP runs carry per-project run-test minimums in `eng/test.ps1`: TUnit 14 (its
  `--minimum-expected-tests` counts tests that actually ran, so the deliberate adapter skips are
  excluded from the 15 discovered) and xUnit.net v3 17 (the JUnit total it writes includes the skip).
  xUnit.net v3's in-process runner exits 0 on a zero-test run, so its structured JUnit result is
  parsed and compared with the same minimum. Lowering a minimum is a deliberate edit that names the
  removed tests. `eng/test-gates.ps1` proves both guards fail on a filter that matches zero tests.
- **Parallel safety rests on per-test ownership, not on the runner policy.** Provisioning names come
  from `context.UniqueName(kind, sequence)` (the `[CsmsOperator]` pattern; `kind-{TestId}[-sequence]`,
  deterministic and persistent-store-safe) and predicates use test-owned ids; that is what makes
  `ParallelScope.All` safe. The default id generator's random six-digit run prefix also keeps reruns
  against a persistent database collision-free. A shared fixture or a fixed identifier reintroduces the
  repeatability bug (REF-1).
- **A per-run in-process server is one `WebApplicationFactory` the whole run shares, and the client
  ledger the framework keeps for clients it creates is not thread-safe.** ProtoTest builds its
  in-process HTTP client over the `TestServer`'s handler so the test context is its only owner; a
  suite that calls `CreateClient`/`CreateDefaultClient` on `ServerFactory<T>()` from parallel tests
  re-opens that ledger, and its teardown enumeration crashes the run.
- **A per-run WireMock fake keeps its stubs and request log for the whole run.** Teardown reports only
  the new requests; it no longer resets the fake, so a stub one test registers still matches in the
  next and `ReceivedRequests` accumulates. Clear shared state with `Reset()` when a test needs a
  clean fake, and keep the suite serial while tests share one server.
- **A test's setup runs inside the per-test transaction.** `ProtoTest.Sql` opens the connection and
  begins the transaction before hooks and attributes run, so DDL in a test hook or body is rolled back
  with the test (the postgres-ef trial: a table created in one test is gone in the next). Run-owned
  state goes through `AddRunSetup(name, delegate)`, registered after the piece that publishes its
  address; the round trip is `tests/ProtoTest.Sql.Tests/RunSetupSchemaTests.cs`.

## Versioning, feeds and gates

- **Branch packages must never reuse the published version.** `Directory.Build.props` carries
  `1.1.0-alpha.<n>` and `eng/pack.ps1` refuses to pack the `PackageValidationBaselineVersion`
  (`4bafa6d`). The old collision (a locally-packed `prototest.* 1.0.1` in the global cache made package
  validation compare the package against itself) is the reason: local consumers use package-source
  mapping and clear `~/.nuget/packages/prototest.*` after a repack.
- **The branch meets CI, and the gate record is honest.** `ci.yml` includes `version/**` and a manual
  dispatch, and every locally run stage records its evidence through `eng/verify.ps1 -Stage <name>`.
  The gate scopes to the working-tree change, or to the HEAD commit when the tree is clean; it formats
  only the projects the change touched, records `docs-only` for a docs change, and records `tooling`
  and runs `eng/test-gates.ps1` for a gate-script or workflow change. A stage that changed code but
  skipped lint/tests (it is already committed without `-Full`, or `-SkipLint`/`-SkipTests` was passed)
  records `incomplete` and exits 1; only `-AllowSkippedCodeGates` turns that record green, and the
  record then names the skipped gates and the approval. `-Pack` when public surface/packaging changed,
  `-Full` for the CI shape.
- `eng/pack.ps1` is the per-stage pack gate; it verifies the packable set, READMEs, dependency edges,
  PDB/DLL pairs and that a project disabling package validation carries a
  `<PackageValidationOptOutReason>`. Run it whenever packaging changes, then re-pack for consumers
  before re-running their restore.
- **Package versions are pinned per target framework; raise them deliberately.**
  `Directory.Packages.props` names the exact patch (`Microsoft.Extensions.*`, EF Core, Mvc.Testing,
  Sqlite, Npgsql) and no longer enables central floating versions, so a restore cannot silently move a
  patch under the suite.
- `eng/check-docs.ps1` fails when a documented `Add*` name is not a method in `src/**` (a small
  commented allowlist carries the framework and sample helpers the docs reference, such as
  `AddEnvironmentVariables`, `AddMinutes` and `AddNorthstarDomain`), so a renamed `Add*` symbol cannot
  stay green. The removed-symbol deny list is derived from `src/**/CompatibilitySuppressions.xml`:
  CP0001 type removals by short name, CP0002 member removals as `DeclaringType.Member` while no
  member of that name is left on the declaring type (a changed overload keeps the name), and CP0006 is
  excluded because it means a member was added to an interface. The configuration-key cross-check runs
  everywhere against the tracked `docs/configuration-keys.json`: its `sections` are the
  `ConfigurationSectionName`/`SectionName`/`SectionPath` constants scanned from `src/**/*.cs` and
  verified against the file on every run, and its `allowedKeys` are the documented non-section keys,
  each with a reason and required to stay mentioned in a page. A docs key with no source section
  fails in CI; the private `assets/internal/records/docs-facts` sheets add the fact-to-docs direction
  where the records checkout exists.
- **Publishing is tag-gated.** `.github/workflows/release.yml` logs in to NuGet and publishes only
  from `github.ref_type == 'tag'`; a dispatch from a branch with `dry_run: false` hits the guard and
  fails instead of pushing. `eng/release.ps1` refuses a real push from any non-tag ref - or from a
  local run with no CI ref - unless `-AllowBranch` is passed, and a tag ref must still be
  `v<version>`; `-DryRun` is always allowed so the workflow can validate a plan on a branch.
- **The viewer's recipe traces are snapshots; CI proves generation, not byte equality.**
  `eng/generate-recipe-traces.ps1` runs in the build-test-pack job after the suites, so a recipe filter
  that matches no test or a generator break fails CI. The committed
  `viewer/public/demos/recipes/*.prototrace` files embed wall-clock timestamps and per-run ids, so
  `git diff` cannot be the check: regeneration is exercised, byte staleness is not detected.
- **The changelog is cut before tagging.** `eng/cut-release.ps1` rolls `[Unreleased]` into
  `## [<version>] - <date>` from `Directory.Build.props` and fails on an empty section;
  `.github/workflows/release.yml` refuses to create a GitHub Release without that section instead of
  falling back to generated notes. `RELEASING.md` is tracked and is the release checklist; after a
  release publishes, `PackageValidationBaselineVersion` moves to it and the now-baselined packages drop
  their validation opt-out in the same pass.

## Repo hygiene

- `assets/internal/` is gitignored (`.gitignore:488`). Its `docs-facts/` set was written at `db9d7aa`
  against `0.1.0-alpha` and is **archived input, not guidance**. The `.opencode/skills` file references
  several `assets/internal/*` files that no longer exist for that reason. Engineering facts now live in
  `eng/facts/` (tracked).
- The viewer must not be started from an agent shell (a detached dev server keeps the session
  attached and looks like a hang). See `.opencode/skills/prototrace-design/SKILL.md`.
- The docs site and viewer have their own stylelint/typecheck gates; run them for UI changes.
