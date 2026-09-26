# Changelog

All notable changes to ProtoTest are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
All ProtoTest packages share one version; breaking API changes are called out below.

## [Unreleased]

### Added

- `ProtoTest.Hosting` runs a background worker or generic host in-process with the suite:
  `AddWorkerHost<TProgram>()` starts the worker's own entry point once per run, after the
  infrastructure registered before it, feeds it the suite's configuration, the run's settings
  (container connection strings and settings infrastructure values) and `AddWorkerHost` options and
  stops it with the run. Tests reach it through `Proto.Context.Host<TProgram>()` and
  `HostService<TProgram, TService>()`; the run records the worker as a `worker` entity and capability,
  and `ProtoCapabilityKinds.Worker` guards tests that need it.
- Readiness probes replace setup sleeps: `AddReadinessProbe(name, check)` waits at its registration
  position with a configurable timeout and interval, records attempts and wait in the trace, and fails
  the run naming the probe and the last error. `ProtoReadiness.Tcp`/`.Http` cover the common checks;
  `AddHttpReadiness(application)` waits for a published application's address and skips in-process ones.
  Containers declare a default TCP check - the PostgreSQL and RabbitMQ containers wait for their
  standard ports, overridable with `ReadyOn(port)` for custom images.
- A test clock replaces sleeping in time-dependent tests: `ConfigureClock(new ProtoClock(seed))` seeds
  the run, `Proto.Context.Clock` is a per-test `ProtoClock` that `Advance`s or `SetUtcNow`s, and the
  in-process application receives it as its `TimeProvider` (each request is linked to the test that
  caused it). Workers see the run clock, which `ProtoHost.CurrentHost.Clock` advances for the whole
  run. Every move records a `clock.advance` event and updates a `clock` entity, and each test starts
  from the run's seed so parallel tests never share a timeline.
- `ProtoTest.Devices` talks to devices - a simulator or real hardware - from the same context and
  trace: declare a named client once (`devices.AddWebSocketClient("Chargers", path:
  "/ocpp/{deviceId}").AddDevice<AcCharger>()`), then get typed instances per test by id through
  `Proto.Context.Devices("Chargers").For<AcCharger>("CP-001")`. Addresses resolve per test - an
  explicit template, a resolver, or the application's address when the client is registered inside
  `AddApplication` - so there is no per-device configuration. Transports implement
  `IProtoDeviceTransport`; `ProtoTest.Devices.WebSocket` is the first (`ws://`/`wss://`, options under
  `ProtoTest:Devices:WebSocket`). A matched `ExpectAsync` contributes protocol coverage with the
  catalog gaps a registered `IProtoDeviceProtocol` declares, and `[RequiresDevice<TDevice>]` skips what
  an environment cannot provide. New vocabulary: `ProtoCapabilityKinds.Device`,
  `ProtoTraceEntityKinds.Device`, and the `device.connect/send/receive/command` operations.
- Device clients reach an in-process application automatically: register the transport once with
  `AddInProcessWebSocketDevices<TProgram>(application)` and the same
  `AddWebSocketClient("Chargers", path: "/ocpp/{deviceId}")` uses the application's `TestServer` when
  it is hosted in-process and the configured address over a socket when it is published - the client
  registration never branches on the environment.
- `IProtoConfiguredInfrastructure` hands infrastructure the run's state as it starts - settings and the
  suite's configuration - which is how an in-process worker reads a broker a container just started.
- Infrastructure registered with `AddInfrastructure` is skipped when every key it declares already has
  a configured value, so a configured environment is never shadowed by a container or process that
  would fill the same address. `AddInfrastructureAlways` opts a piece out, settings-only infrastructure
  may declare keys too, and a skipped piece is recorded as a `skipped` run entity without being owned
  or released.
- `AddAspNetCoreServer` steps aside when the application's `BaseUrl` is configured: no test server
  starts, the application's `HttpClient` talks to the configured address (the same address web sessions
  and device clients resolve), the `ASP.NET Core` capability is dropped so `[RequiresInProcess]` skips
  instead of failing, and `ServerFactory`/`ApplicationServices` throw naming the address. The trace
  records `aspnetcore.server.skipped`. `AddCapabilityUnlessConfigured` is the Core primitive behind it,
  and it is how an integration keeps its capability honest when the environment provides the address.
- `ProtoCapabilityDescriptor.Instance` names the instance a capability describes - an
  `AddAspNetCoreServer` name, the application an in-process device transport belongs to. Two instances
  are two capabilities, in skip checks and in the run trace (`server:ASP.NET Core:A`), so configuring
  one server's address no longer hides the other.
- One application-address precedence: an address a started piece published through
  `ProtoInfrastructureSettings` wins over `ProtoTest:Applications:{app}` configuration for every
  reader. REST and GraphQL clients, gRPC channels, readiness, web sessions and device clients all
  resolve through `ProtoApplication` (`GrpcAddress(context, app)` is the gRPC form), so the suite
  talks to the process the run started; the in-process transport is only the fallback when neither
  resolves. `AddAspNetCoreServer`'s step-aside still reads static configuration (decided asymmetry),
  so give a published process its own application name when both must coexist.
- `ProtoReadinessOptions` implements `IProtoConfigurableOptions` and binds
  `ProtoTest:Readiness`; the new `ProtoInfrastructureContext.Readiness` hands the run's policy to
  infrastructure, so `ConfigureReadiness` (or configuration) sets the timeout and interval for host
  probes and the containers the run starts alike.
- `AddCapabilityWhenProvided(capability, keys…)` is the missing-address half of the conditional
  capability pair: the declaration drops when none of its keys can provide the capability — no
  configured value and no registered infrastructure piece declares one — so an integration whose
  address cannot exist is absent and `[RequiresCapability]` skips. It composes per declaration with
  `AddCapabilityUnlessConfigured` and plain declarations, and the `capability.skipped` event names the
  deciding keys and the reason (`already configured`, or `no key provided`).
- `SqlOptions.AddressKeys` is a code-declared `SqlAddressKeys` set naming the configuration keys that
  can provide the SQL connection; no configuration section binds it, because the capability decision is
  made when the host is built. With at least one declared, `AddSql` declares the `SQL` store capability
  with `AddCapabilityWhenProvided`; empty (the default) keeps the capability unconditional and the
  factory owning the address. `AddEntityFrameworkCore` shares the declared keys: it declares its
  `Entity Framework Core` store capability under the same rule and its enlistment hook stays inert with
  the SQL integration.
- `ProtoMessagingBuilder.UseBroker(factory, addressKeys)` lets an adapter name the configuration keys
  its address comes from, so the `Broker` capability is declared only while one of them is provided.
- `WebPageInventory.VisitedObservationKind` and `WebPageInventory.VerifiedObservationKind` name the
  `web.page.visited`/`web.page.verified` observation kinds beside the existing
  `AvailableObservationKind`, so producers and collectors reference one constant each.
- One assertion surface across the integrations. GraphQL responses expose `Should.HaveNoErrors()`,
  `Should.HaveErrors()` and `Should.HaveError(code)`; a failed gRPC call exposes
  `ProtoGrpcAssertions.For(exception).Should.HaveStatus(...)` (or `.ShouldNot`); a Sheets model exposes
  `Should.MatchModel()` and a typed column `Should.All(predicate)`, and every Sheets assertion member
  now returns its subject so assertions chain. `ShouldMatchShape` returns its subject on gRPC replies
  and model rows, and an untyped table row gained a shape assertion by leaf header names and rendered
  cell values (spelled `row.Should.MatchShape(shape)` since DX-02). The foundation docs gained an
  [Assertions](https://prototest.dev/docs/foundation/assertions) page.
- Shape matching is a facade member: `response.Should.MatchShape(shape)` on REST and GraphQL
  responses, `message.Should.MatchShape(shape)` on a consumed message (the `Should` property is
  `[JsonIgnore]`d, so the message's JSON is unchanged),
  `ProtoGrpcAssertions.For(reply).Should.MatchShape(shape)` on a gRPC reply, and
  `row.Should.MatchShape(shape)` on a Sheets table row. Each returns its subject, so
  `response.Should.HaveHttpStatus(Ok).Should.MatchShape(shape)` reads as one chain. Shape stays
  positive-only: there is no `ShouldNot.MatchShape`. A model row is a user type, so it keeps the
  `row.ShouldMatchShape(shape)` extension (C# has no extension properties).
- Messaging taps gain a code API: `AddMessaging(messaging => messaging.Tap("invoice.issued").UseRabbitMq())`
  declares the destinations an adapter pre-binds during test setup, so a message published before the
  test's first await is still received. Repeated calls compose and dedupe, and
  `ProtoTest:Messaging:Destinations` configuration still binds over the code values.
- REST and GraphQL read a single value by JSON path: `RestResponse.ReadAsJson<T>("$.id")` and
  `GraphQLResponse.ReadDataAs<T>("$.order.total")`, over the documented subset (`$`, dot members,
  `[n]` indices; a leading member without `$` is accepted). A path that does not resolve throws the
  protocol's assertion exception naming the subject and the path and records the protocol's failed
  deserialize evidence (`http.response.deserialize` event / `graphql.response.deserialize` operation)
  with the path; `ProtoTest.Json.JsonPathResolver` is the shared resolver.
- Required reads return `T`: `RestResponse.ReadRequired<T>()` / `ReadRequired<T>(jsonPath)` and
  `GraphQLResponse.ReadRequired<T>()` / `ReadRequired<T>(jsonPath)` throw the protocol's assertion
  exception naming the subject (and the path) when the body is empty, JSON `null`, or the path is
  missing. JSON `null` is checked before deserializing, so a value type reports the protocol exception
  too; REST records a required-read failure, and GraphQL a required path failure, through the same
  deserialize evidence. The nullable reads are unchanged.
- `Proto.Context.UniqueName("tenant")` is the first-class name for records that outlive the process:
  it derives a deterministic, persistent-store-safe name from the test id (`tenant-{TestId}`, or
  `tenant-{TestId}-{sequence}` with the optional sequence for a second object of the same kind), so
  parallel tests never collide and a rerun against a database that outlives the process never
  collides; the record is reused only when the suite fixes `RunPrefix` (`ConfigureTestIds`), because
  the default run prefix is random per run. `ProtoTest.Data`'s generated member defaults are
  deterministic per test in the same spirit.
- Accessor symmetry: `Proto.Context.Sql()` is the primary SQL accessor (`SqlSession()` stays a
  documented alias; `SqlConnection()`/`SqlTransaction()` delegate to it), and
  `Proto.Context.Messaging(name = null)` accepts an optional client name - the run's broker client is
  `Default` - failing with a message naming `AddMessaging` for an unknown name. The added parameter is
  source-compatible but binary-breaking for compiled consumers; the per-TFM
  `ProtoTest.Messaging/CompatibilitySuppressions.xml` records it.
- Typed skip conditions for the host composition: `[RequiresWorker<TProgram>]` checks the `worker`
  capability by the program assembly name with a default reason naming `AddWorkerHost<TProgram>()`;
  `[RequiresServer(name)]` checks the named `AddAspNetCoreServer` instance through the new
  `ProtoHost.HasCapability(kind, name, instance)` overload; `[RequiresApplication(name)]` checks that
  the suite declared the application with `AddApplication` through the new `ProtoHost.HasApplication`.
  `RequiresCapabilityAttribute.CapabilityInstance` exposes the instance filter to open kinds.
- `ProtoMessage` reads are typed: `message.ReadAsJson<T>(options)` reuses `ProtoJsonDefaults.Reader`
  (case-insensitive property names) and `message.ReadRequired<T>()` / `ReadRequired<T>(jsonPath)`
  return `T`, throwing `MessagingAssertionException` naming the destination when the payload is empty,
  JSON `null`, or the path is missing (the shared `JsonPathResolver` subset). `Payload` stays for raw
  inspection.
- Suite-level skip reasons: `builder.AddCapabilityReason(kind, reason, name?)` states a capability
  gate's reason once, so a suite with many gated tests does not repeat one sentence. A gate's own
  `Reason` still wins, then the suite reason for its name, then the suite reason for its kind, then the
  attribute's default; `ProtoHost.FindCapabilityReason(kind, name?)` exposes the lookup to custom
  conditions. `[RequiresWorker<T>]`, `[RequiresServer(name)]`, `[RequiresDevice<T>]` and
  `[RequiresInProcess]` inherit the behavior through `[RequiresCapability]`.
- `IProtoConfigurableOptions.FallbackConfigurationSectionName` lets a renamed options section keep its
  old key working: the fallback binds first and the current section binds over it, so a scalar in the
  current section wins; list-valued options accumulate the fallback and current entries (the legacy
  list is appended, not replaced). `GrpcClientOptions` uses it for the legacy gRPC section.
- `ProtoResourceState.Releasing` reports a resource whose release callback is running, so a snapshot
  taken during a blocked release no longer reads as `Registered`.
- `StartTestAsync` accepts a `CancellationToken`; it is carried as
  `ProtoExecutionContext.CancellationToken` and `ProtoTest.Sql` passes it to the connection open and
  transaction begin. No runner adapter supplies one yet; a caller that starts a test explicitly can
  pass one.
- `ProtoTestResult.FromException(Exception)` and `ProtoTestResult.IsCancellation(string)` are the
  shared outcome classifier the runner adapters use; `ProtoTestName.ForRow` composes the row name MSTest
  and TUnit record (`…MethodName[1, admin]`).
- `ProtoTest.Json` hosts the shared mechanics: `ProtoFailureDiagnostics` (sanitized
  address/type/message/cancellation/duration), `ProtoJsonRead`/`ProtoJsonReadSemantics` (one JSON read)
  and `ProtoObservationCapture`; `ProtoTest.Http.ProtoHttpFailureDiagnostics` and its capture moved
  there. `ProtoHttpEndpoint.RequireHttpAddress` is the shared absolute-HTTP(S) rule.
  `RestShapeMatchData.Method`/`RouteTemplate` structure a shape hit for OpenAPI coverage.
  `PlaywrightWebOptions.MaxTraceBytes` (32 MiB, 0 off), `WebSocketDeviceOptions.MaxMessageBytes`
  (4 MiB, 0 off) with `DeviceFrameTooLargeException`, and `IWebBackend.PollInterval` bound the reads
  and the session timing.
- `AddLoopbackApplication(applicationName, createApp)` (`ProtoTest.AspNetCore`) hosts a hand-built
  `WebApplication` on its own loopback listener for browser journeys: it starts the factory on
  `http://127.0.0.1:0`, publishes the bound address as `ProtoTest:Applications:{app}:BaseUrl` (a
  configured key skips the listener), forwards the suite's configuration with the started settings
  available at its registration position as command-line arguments, and releases the application
  with the run. The published instance is separate from `AddAspNetCoreServer`, with no
  `ServerFactory` or `[RequiresInProcess]`.

### Fixed

- A browser download is named binary content: `WebDownload` implements `IProtoBinaryContent`, so a
  downloaded file feeds anything consuming named bytes (for example `ProtoSheets.Open`) in one line
  instead of through a stream.
- The in-process HTTP client is owned by its test alone: ProtoTest builds it over the `TestServer`'s
  handler instead of `WebApplicationFactory.CreateDefaultClient`, so the parallel tests of a run no
  longer mutate the shared per-run factory's client ledger. A torn ledger entry crashed run teardown
  with a `NullReferenceException` from `WebApplicationFactory.DisposeAsync`.
- An in-process device connect carries the test id on the WebSocket handshake, the same way the
  in-process HTTP client does: the application under test resolves the connecting test's clock instead
  of falling back to the run clock, so `Proto.Context.Clock.Advance` reaches session timestamps the
  application stamps from its `TimeProvider`. A handshake with no test on its flow keeps the run clock.
- A canceled device connect no longer poisons the session: the cancellation reaches its caller and the
  canceled attempt is cleared, so the next send starts a fresh connect.
- A device client name reused under a second application fails naming the client and both applications,
  instead of the bare "already registered" message.
- A worker entry point always gets the run's `--contentRoot`/`--applicationName`: an overlay key named
  `contentRoot` or `applicationName` (any casing) is skipped, so the generated pair cannot be replaced.
- The RabbitMQ broker connection string is validated where the options resolve: a missing, non-absolute
  or non-`amqp`/`amqps` value fails naming `ProtoTest:Messaging:RabbitMq:ConnectionString` instead of
  failing later at connect time, including a value a started broker container publishes.
- The ambient-context failures name the fix: `Proto.Context` outside a test points at
  `ProtoHost.FindTraceWriter(Activity?)` for off-flow telemetry and `ProtoHost.CurrentHost` for run
  scope, a missing `Resolve<T>()` names `SetContext` (and the keyed form), and no active host names the
  runner setup (`ProtoTestAssembly`).
- `UseRabbitMq` declares the `Broker` capability conditionally on
  `ProtoTest:Messaging:RabbitMq:ConnectionString`: a run with a configured key or a broker container
  that declares it keeps the capability, while a run with neither drops it and
  `[RequiresCapability(ProtoCapabilityKinds.Broker)]` skips instead of failing at setup or first
  publish. A connection string set in the options callback keeps the unconditional declaration.
- A SQL run whose declared `AddressKeys` are all unprovided no longer fails at setup or run start: the
  connection hook, the isolation guard and the Entity Framework Core enlistment hook stay inert, the
  connection is not opened, and `SqlSession()`/`SqlConnection()`/`SqlTransaction()` (or
  `Sql<TContext>()`) throw naming the missing keys and the
  `[RequiresCapability(ProtoCapabilityKinds.Store)]` gate.
- Conditions are evaluated per declaration: a capability drops only when every conditional declaration
  for it is satisfied and no plain declaration promises it, and the `capability.skipped` trace event
  names the deciding keys. Satisfying one server's address no longer skips tests against another server
  that is still live in-process.
- `Build()` is terminal for every public registration entry: hooks, run gates, capabilities, the
  clock, sinks, infrastructure and application entries throw the same single-build message instead of
  silently registering into a host that already built its provider. A repeated registration that is a
  no-op by design before `Build()` (the same server or worker name for the same program) stays a
  no-op; the same name for a different program throws.
- Only the web backend that wins the first-wins registration declares its browser capability, so
  referencing both Playwright and Selenium leaves one honest capability behind the one live backend.
- A conditional declaration's key set compares by content (ordinal, distinct, order-independent), so
  registering the same declaration twice leaves one declaration.
- The in-process WebSocket device transport declares its capability only while the application runs
  in-process; with `BaseUrl` configured the capability is dropped and the socket transport serves.
- `AddHttpReadiness` no longer records "the application runs in-process" when the piece that publishes
  its address is registered after it: the skip names the registration position and the later key, and
  only a probe backed by an in-process server capability claims in-process. Register the probe after
  the piece that publishes the address.
- Container readiness honors the run's readiness policy instead of its private 30 s timeout, so a slow
  image is tuned with `ConfigureReadiness(options => options.Timeout = ...)` or
  `ProtoTest:Readiness:Timeout`.
- `AddHttpReadiness` rejects an address that is not an absolute HTTP(S) URL at run start, naming the
  configuration key, instead of probing it into a timeout.
- The gRPC missing-address error names the client's application (and the key to set) instead of
  printing the literal `{app}`.
- Shape mismatches whose expected shape carries a value constraint now record every mismatch in the
  trace instead of an internal compiler-generated type name (or an opaque constraint object on
  .NET 8).
- Malformed JSON response bodies are redacted with the shared sensitive-name policy instead of
  passing through unredacted.
- Report metadata (findings and observations) is redacted with the same policy the trace uses.
- A released container resource can be started again and its connection string is cleared on
  release, so a retry after a failed run start works and no released endpoint stays readable.
- Run resources restarted by a retry are released with their new ownership period instead of being
  skipped as already released.
- A failed web backend creation is no longer cached, so a session retries from a clean slate.
- RabbitMQ uses `RabbitMQ.Client` 7.x end to end (async connection, channel, publish and consume), and
  a delivery is converted to the broker-neutral message inside the consumer handler, where its body
  buffer is still valid.
- Every package packs again: `ProtoTest.AspNetCore`, `ProtoTest.Web`, `ProtoTest.Web.Playwright` and
  `ProtoTest.Web.Selenium` had lost their `IsPackable` setting, and `ProtoTest.Hosting`,
  `ProtoTest.Devices.WebSocket` and `ProtoTest.Devices.WebSocket.AspNetCore` were missing from the pack
  gate, so a 1.1 release would have shipped without them. `eng/pack.ps1` now covers all 35 packages
  and verifies the whole set in one run.
- A worker's `Program.Main` sees the run's configuration: `AddWorkerHost` passes the merged overlay
  (options over infrastructure settings over suite configuration) to the entry point as
  `--{key}={value}` arguments, so `Host.CreateApplicationBuilder(args)` and
  `Host.CreateDefaultBuilder(args)` read final-precedence values before `Build()`; the `HostBuilding`
  in-memory overlay remains the fallback for an entry point that ignores its arguments. A parameterless
  `Main` stays the documented limit.
- The test-clock lookup is scoped to the owning host instead of a process-global map keyed by test id
  alone: two hosts that share a `RunPrefix` each resolve their own clock, a failed test start leaves no
  entry, context disposal removes only its own host's entry, and host disposal clears the registry. The
  in-process ASP.NET Core request linking uses the owning host's registry, so a request sees the clock
  of the test that caused it even when another host has a test with the same id.
- `ProtoClock.Advance` performs its read-modify-write under the clock's lock, so concurrent advances on
  the run clock add up instead of losing updates and each advance records its own `clock.advance`.
- In-process device transports are keyed by `(TProgram, application)`: a second
  `AddInProcessWebSocketDevices<TProgram>` is a second transport, and a client is only routed through
  the transport of the application it was registered under. Two applications exposing the same path
  each serve their own clients, and a path-only client with no matching transport fails naming the
  application instead of falling back to another transport.
- The in-process device transport resolves the registered `WebSocketDeviceOptions` from DI and
  validates them with the transport, and its `ConnectTimeout` bounds the in-process connect - the
  `configure` callback is no longer dead.
- `DeviceSession` connects single-flight and serializes sends: concurrent sends share one connection
  instead of opening one each and leaking the loser. One receive may be in flight at a time (a second
  fails fast naming the device), and a send that races a disconnect fails with a device error naming
  the device instead of a disposed-socket exception or an NRE.
- Device resource and trace entity ids include the device type (`device:{client}:{type}:{id}`), so two
  device types with the same id on one client coexist with their own resource and entity.
- The device release path disconnects: teardown emits `device.disconnect` and finalises
  `device.connected = false`, so a test that never disconnects still ends disconnected and an explicit
  disconnect followed by a send reconnects and records both connects.
- `AddWorkerHost` throws when one name is registered for a different program instead of silently
  dropping the second registration, matches `IHostApplicationBuilder` (covering
  `WebApplicationBuilder`), and fails loudly naming an unrecognised builder shape instead of leaving it
  with its own configuration and `TimeProvider`. `ProtoWorkerOptions.Set(key, null)` now delivers an
  empty setting, as its documentation always promised, and the parameterless-`Main` fallback
  (`HostBuilding` overlay) is pinned by a test.
- Readiness waits ride the shared `ProtoPolling` loop instead of a second stopwatch/delay
  implementation, so one rule owns every poll interval and deadline (audit VOC-4). Observable behavior
  is unchanged: exceptions mean "not ready yet", and a timeout still names the probe, the attempts and
  the last error.
- A sink registered directly in the service collection is exported at run end like one added with
  `AddSink`; the export hook is registered once by the host build, so mixing both paths still exports
  once.
- `[Application]` runs at `ProtoAttributeOrder.Application` (`int.MinValue`), before every other setup
  attribute, so a web session declaration or a login always sees the selected application; a session
  created before the selection falls back to its own name and cannot resolve an address.
- A failed run start unwinds every completed run hook that owns state, in reverse, so a suite-setup
  hook's `BeforeRunAsync` state is released by its `AfterRunAsync`; gates, reports and the trace
  archive stay silent for a run that never started.
- A `ProtoTestScope` disposed off the async flow that started it, or while another test is active,
  records a `Lifecycle` finding on its test and throws instead of completing nothing silently.
- A throwing run gate keeps its exception type, message and stack in the run trace and in the gate
  verdict's report metadata.
- A telemetry capture failure records one coalesced `telemetry.capture_failed` run event with the
  exception; later failures collapse into it and capturing still never breaks the application.
- `UseRabbitMq` runs its options callback exactly once per registration: the capability decision reads
  the instance the callback configured instead of probing it with a second invocation.
- A publish to a released RabbitMQ broker throws `ObjectDisposedException` naming it instead of
  silently reopening the connection, and a repeated release is a no-op.
- A `Tap` destination whose exchange is missing no longer fails every test in the class at setup: the
  failure is scoped to the tests that await it, which fail there with the named error, and each tap
  owns its channel.
- The gRPC client's options are per named client: each `AddClient` configure callback applies to that
  client only, so metadata, `DefaultDeadline` and `SensitiveMetadataKeys` never leak into another
  client; a repeated registration for the same name still composes, the shared `ProtoTest:Grpc:Client`
  section still binds over each client's callback, and a transport-backed fallback client reads the
  run-wide configuration default.
- A gRPC client whose in-process transport has no base address fails naming `AddClient`, the
  application's `Grpc:Address`/`BaseUrl` keys and `AddAspNetCoreServer` instead of silently dialing
  `http://localhost`.

### Changed

- gRPC client options bind from `ProtoTest:Grpc:Client` (the section rule `ProtoTest:<Integration>
  [:<Area>]`); the legacy `ProtoTest:Grpc` section still binds as a deprecated fallback, so an existing
  suite keeps working, and a value under `ProtoTest:Grpc:Client` wins (list-valued options accumulate
  both sections). `GrpcAttachmentOptions` keeps the inherited `ConfigurationSectionName` instance
  property and names its constant `SectionName` — a `ConfigurationSectionName` constant would hide the
  inherited property and break `new GrpcAttachmentOptions().ConfigurationSectionName`. The old
  `ConfigurationSection` constant stays as an `[Obsolete]` alias.
- The pre-facade assertion spellings remain as `[Obsolete]` shims that delegate to the facade, so
  existing tests keep compiling: GraphQL's `ShouldHaveNoErrors()`/`ShouldHaveErrors()`/
  `ShouldHaveError(code)`, gRPC's `ShouldHaveStatus`/`ShouldNotHaveStatus` extension methods, and
  Sheets' `Verify()` and `ShouldAll(predicate)`. New tests use `Should.*`; the sheets docs and READMEs
  now teach `Should.MatchModel()` and `Should.All(predicate)`. The `void` → subject return changes
  behind the chaining are source-compatible but binary-breaking for compiled consumers; the affected
  packages' `CompatibilitySuppressions.xml` record them.
- Shape failures now name their subject and use the protocol's assertion exception. A REST mismatch
  throws `RestAssertionException` starting with the request identifier (`GET /orders/42 — Shape
  mismatch failed with 1 error(s): …`), GraphQL throws `GraphQLAssertionException` starting with the
  operation, gRPC throws `GrpcAssertionException` starting with the message type, messaging throws
  `MessagingAssertionException` starting with the destination, and a Sheets row throws
  `SpreadsheetAssertionException` starting with the row's `Sheet!Range` (a model row names the record
  type). The shared `JsonShapeMismatchException` — with the full mismatch list — stays reachable as
  the failure's `InnerException`, and the trace attributes, sections and observations are unchanged.
  This is a behavioral change: code that caught `JsonShapeMismatchException` from a protocol assertion
  now catches the protocol exception (or `ProtoAssertionException`).
- The shape spellings are obsolete shims: `RestResponse.ShouldMatchShape`,
  `GraphQLResponse.ShouldMatchShape`, `ProtoMessagingAssertions.ShouldMatchShape`,
  `ProtoGrpcAssertions.ShouldMatchShape` and `SheetModelAssertions.ShouldMatchShape(ProtoTableRow, …)`
  delegate to `Should.MatchShape`; new tests use the facade. `RestResponse.Should` and
  `GraphQLResponse.Should` now return the positive facade type (`RestShouldAssertions`,
  `GraphQLShouldAssertions`), which is source-compatible but binary-breaking for compiled consumers;
  the affected packages' `CompatibilitySuppressions.xml` record it.
- `IProtoInProcessDeviceTransport.CanConnect` takes the requested application
  (`CanConnect(context, applicationName)`) instead of a `DeviceEndpoint` it ignored, so a transport
  answers for the identity it serves. The device transport interface is unreleased 1.1 plumbing; a
  custom in-process transport updates its one method.
- REST object request bodies serialize with the shared web defaults (camelCase names), matching
  GraphQL variables. Pass explicit `JsonSerializerOptions` to keep another naming policy.
- OpenAPI coverage reads specifications with `Microsoft.OpenApi` 3.x (JSON and YAML, including 3.1
  documents). `ProtoTest.OpenApi` references `ProtoTest.Core` directly instead of relying on a
  transitive reference.
- `ProtoHost.FindClock` is an instance member; the clock lookup is scoped to the host that owns the
  test. The clock feature is unreleased, so no consumer migration is needed.
- `ProtoProtocol.CoverageCategory` is optional and `null` for a protocol that ships no collector; a
  collector with no category falls back to the protocol name, and the shipped REST/GraphQL/gRPC/OpenAPI
  collectors keep their categories.
- Messaging adapters share one concurrency contract: awaits serialize and a delivery matching no
  awaited predicate is kept for a later await, so concurrent awaits neither lose nor steal; RabbitMQ
  previously discarded the racing delivery and the in-memory adapter advanced past it.
- A successful publish records the `messaging.published` observation instead of `messaging.publish`
  (which stays the operation name); update a collector that filtered the old observation kind.
- Selenium `Check`/`SelectOption` verify the resulting state (`Selected`) and fail with
  `WebActionabilityException` within the action timeout instead of reporting a click that did not take.
- Sheet reads of `int`/`long` accept only finite, integral, in-range values; a fraction, an out-of-range
  number or NaN fails naming the cell instead of rounding or saturating.
- Sheet record models construct through the constructor their columns map (the record primary
  constructor); an unmapped constructor parameter fails naming it, and a throwing constructor guard
  propagates with its original stack.
- Optional constructor-parameter defaults win over generated values in `ProtoTest.Data`, so a declared
  default (including `null`) is honored.
- Messaging destinations a suite owns can be declared with `Declare(...)` on the `AddMessaging` chain
  or under `ProtoTest:Messaging:DeclaredDestinations`: the adapter creates them during test setup,
  before any tap binds, so a suite that owns the broker and publishes its own events works from the
  documented path. RabbitMQ creates a fanout, durable, non-auto-delete exchange once per run and
  leaves an existing one as it is; the in-memory broker treats a declaration as a no-op.
  `IProtoMessageBroker.DeclareAsync` is the adapter seam and its default implementation refuses,
  naming the adapter, instead of pretending.
- `AddRunSetup(name, delegate)` registers a run-scoped setup step: infrastructure that starts at its
  registration position — after the pieces registered before it, so it reads the connection string a
  container published — over the existing start/stop machinery. The step receives
  `ProtoRunSetupContext` (`Settings`, `Configuration`, the run's `CancellationToken`), owns nothing to
  release (stop and dispose never call it again), and a throwing step fails the run start with its own
  exception and leaves the host retryable, like failing infrastructure.
- An application under test runs in its own container image: `ApplicationContainer`
  (`ProtoTest.Testcontainers`) starts the image as run infrastructure
  (`AddInfrastructure(api, api.BaseUrlKey)`), maps the port the application listens on, publishes
  `http://{hostname}:{mapped port}` as `ProtoTest:Applications:{application}:BaseUrl`, and waits for the
  mapped port with the run's readiness policy. A run that configures the key skips the container;
  `TryStart` reports why a container could not start so a suite can skip without a runtime; a
  containerized application advertises no in-process server, so `[RequiresInProcess]` tests skip.
- `ProtoApplication.ResolveSetting(configuration, settings, key)` is public: one
  published-settings-over-configuration precedence for suites that read an application setting, with a
  blank published value ignored.
- `ProtoAttributeOrder` names the setup order bands (`Application`, `SessionDeclaration`, `Default`), so
  an attribute sequences itself against the built-ins without raw numbers and the deliberate bands live
  in one place.
- A trace can name the CI run that produced it: `ConfigureTracing` gains `RunMetadata` (explicit
  key/value facts) and `RunMetadataEnvironmentVariables` (names read from the process environment, such
  as `GITHUB_RUN_ID`), captured once when the host is built. Each fact is recorded as
  `environment.{key}` on the run resource in `spans.json` and as a `run_metadata` report item
  (`ProtoReportItemKinds.RunMetadata`); an unset variable contributes nothing, so a local run's trace
  and report are unchanged, and a key that would hide a built-in `environment.*` fact fails the build.
- `IProtoDeviceTransport.ConnectAsync(context, endpoint, cancellationToken)` has a default
  implementation forwarding to the context-free overload; the in-process transport overrides it, so a
  connect from a flow without ambient context still reaches the registered application.
- `ProtoTest.Analyzers` ships the intent-dependent checks the framework cannot make at runtime:
  `PT0001` reports a method that carries both a ProtoTest test attribute and the runner's own plain
  test attribute, and `PT0002` reports a test registered by a plain runner attribute that reads
  `Proto.Context`. Warnings only; consumers opt in by referencing the package.
- `AddLoopbackApplication(applicationName, createApp)` (`ProtoTest.AspNetCore`) starts the given
  `WebApplication` factory on `http://127.0.0.1:0` as run infrastructure, publishes the bound address
  as `ProtoTest:Applications:{application}:BaseUrl`, and forwards the suite's configuration with the
  started settings at its registration position as command-line arguments, so a browser session, a
  REST client and a readiness probe resolve one running application. A configured address skips the
  listener like any address provider; the listener is a separate instance with no `ServerFactory` or
  `[RequiresInProcess]`.
- A readiness timeout names the probed URL and the last error; a rejected GraphQL subscription names
  the server's errors; an oversized WebSocket frame fails naming the address and limit; a stuck
  Selenium pump is reported (`web.selenium.executor_abandoned`) instead of abandoned silently; a
  Playwright trace over the cap records `web.playwright.trace_too_large`; coverage reads the address
  inside the assertion operation.
- A body `OperationCanceledException` records `Cancelled` under xUnit v2 as it already did under
  MSTest, TUnit and xUnit v3; NUnit still records `Failed` because its result carries no exception.
  xUnit v2 completes the test scope in a `finally` even when its own pipeline throws.
- TUnit parameterized rows record their arguments in the trace name (`…MethodName[1]`) instead of the
  method name alone, so parallel rows stay distinguishable.
- Failure evidence is one record: gRPC and messaging record `ProtoFailureDiagnostics` through the
  protocol-identified guard, so a non-`RpcException` and a failed publish/await are observations; the
  capture-failure event is `{protocol}.diagnostics.failed` with the protocol's trace source (the old
  `http.diagnostics.failed`/`ProtoTest.Http` literal is gone).
- One wait timing per session: assertions poll at `IWebBackend.PollInterval` (Selenium's `PollInterval`,
  the shared default otherwise), and REST request-URI validation uses the shared wording
  (`REST request URI '…' must be an absolute HTTP or HTTPS URI`).
- `ProtoApplicationResolution.ResolveState` returns only the state the `[Application]` attribute set
  during the test's lifecycle; a context started without the resolved attribute reports "No application
  is selected" instead of re-reading the method. Adapters pass the resolved attributes, so suite
  behavior is unchanged.
- An untraced GraphQL response tolerates a null execution like the REST contract, and enumerating a
  subscription disposes each event as it advances (the final event stays the caller's).
- Container and device release are bounded: an in-flight container start is awaited for at most five
  seconds and recorded as `container.start.abandoned`; a device connect is awaited the same way and
  recorded as `device.disconnect.abandoned`; normal release with nothing in flight is unchanged.
- Framework routes (`/_…`, `/.well-known…`) are never page-inventoried, and a table row's shape
  assertion uses the table's own context instead of the ambient one.
- The HTTP response and attachment defaults bind `ProtoTest:Http:Responses` through the shared options
  registration when no explicit registration exists; a resolver that returns null still means capture
  is opt-in, so attachments stay off unless asked for.

### Removed

- `ProtoTest.OpenTelemetry` was retired: the `ProtoTest` `ActivitySource` always exists, so subscribing
  is one `AddSource("ProtoTest")` call as the observability page shows; a 12-line bridge did not earn a
  package of its own.
- `IProtoReadinessProbe` and `ProtoReadinessResult.LastError` were removed: probes are registered as
  delegates with `AddReadinessProbe`/`AddHttpReadiness`, the wait result carries attempts and waited
  time only, and the timeout message still carries the last error. Both were unreleased 1.1 plumbing.
- Messaging's coverage promise was removed rather than shipped: `ProtoMessageClient` no longer documents
  destination aggregation, and the Messaging protocol descriptor no longer declares a coverage category.
  The `messaging.*` observations remain trace evidence for a collector a suite registers; no
  `ProtoTest.Messaging` collector ships, and destinations are deliberately not a coverage category.
- `ProtoHttpAssertions<TResponse, TAssertions>` loses the unused `TAssertions` parameter
  (`ProtoHttpAssertions<TResponse>`); unreleased 1.1 plumbing, a deriver updates one base-type argument.
- `WebTiming` and `WebNames` are internal (web timing defaults and artifact naming are plumbing, not
  consumer promises); the web pages teach the public surface.

### Breaking

- `AddClientFrom` was removed from the REST and GraphQL builders. Register clients under an
  application and configure its base URL or endpoints, or use an `AddClient` resolver when the
  address depends on test context.
- Several registration, observation and runner implementation types are now internal. Use the
  public builder, response and runner APIs instead of constructing those implementation types.
- `ProtoHost` can no longer be constructed from a service provider directly; use `ProtoHostBuilder`,
  which registers the stores, gates and hooks the host's reporting depends on.
- `ProtoFlow` steps now declare the trace operation they record (`ProtoStepDescriptor`); the unused
  `ProtoStepOptions` retry and timeout surface was removed.
- `IProtoClientInitializer.TryInitializeAsync` no longer takes a cancellation token. No runner adapter
  supplies a test cancellation token; a caller that starts a test explicitly can pass one to
  `StartTestAsync`, visible as `ProtoExecutionContext.CancellationToken`, and a run-scoped hook remains
  the cancellable extension point for runner-driven suites.
- `ProtoTest.AspNetCore` now depends on the new `ProtoTest.Web.Pages` package (page identity and
  inventory) instead of the full `ProtoTest.Web`, for the one concept the in-process server and the
  browser sessions share. `ProtoTest.Web` depends on it too.
- `ProtoTest:Web:Sessions:{name}` settings no longer configure sessions. Put addresses under
  `ProtoTest:Applications:{application}` and select the session with `[WebSession]` or `Web()`;
  backend options remain under `ProtoTest:Web:Playwright` or `ProtoTest:Web:Selenium`.
- The web reshape removed members instead of obsoleting them: `IWebBackend.CurrentAddress` and its
  backend implementations, the old `Web()` overload, `WebFlow<TComponent>.Check(Func<TComponent,
  WebElement>, bool)`, `WebOperationContext.Result`, `PlaywrightWebOptions.Context`,
  `RequiresPlaywrightBrowserAttribute.Session`, and Selenium's download surface
  (`IWebBackendDownloads` and `SeleniumWebBackend.DownloadAsync`). Each package's
  `CompatibilitySuppressions.xml` records exactly these removals; the web pages describe the
  replacement surfaces.

### Changed

- REST, GraphQL and gRPC clients use protocol-scoped names, so the same logical name can be used
  by more than one integration.
- REST, GraphQL and gRPC now use one registration path per integration for host and application setup;
  messaging keeps its registration state with the host services rather than in a global table.
- Web sessions are created when requested and complete through the shared client lifecycle.
- Trace snapshots can safely read observations, attachments and findings while other test work records them.
- CI builds the sample UI once and releases the same packages that passed verification.

## [1.0.1] - 2026-09-20

### Fixed

- Preserve binary REST response artifacts byte-for-byte instead of converting them through text. This
  keeps downloaded workbooks, PDFs, archives, images and other binary responses valid inside
  `.prototrace` files.
- Correct API documentation links emitted from XML comments across the HTTP, Sheets and Web packages.

### Added

- Preview `.xlsx` artifacts directly in the ProtoTrace viewer, with worksheet tabs, dimensions, sticky
  row and column headers, typed cell values and bounded rendering for large workbooks.
- Add focused, shareable recipe traces for REST-to-GraphQL, REST-to-database and workbook journeys.
- Publish a generated .NET API reference alongside the task-oriented documentation, with one command
  producing the complete uploadable site.
- Add documentation quality, link and API-reference workflows; contributor, support, security and
  code-of-conduct guidance; richer CI guidance; and interactive stack and trace examples.

### Changed

- Improve the documentation landing page, navigation, SEO metadata, social preview and per-page feedback.
- Harden release validation: every ProtoTest package must share one version, every symbols package must
  match its DLL paths and portable-PDB identities, and a release tag must match the package version.
- Refuse duplicate NuGet versions during publishing so a rebuilt symbols package cannot be paired with
  an already immutable DLL from another commit.

## [1.0.0] - 2026-09-19

**ProtoTest 1.0 is here.** What began as a stubborn idea — that an integration test should read like the
scenario it describes while the framework quietly owns everything around it — is now a stable foundation
for .NET 8, 9 and 10. One host, one execution context and one explicit lifecycle; the test runner you
already use; and every integration sharing the same assertions, evidence and coverage, all the way down to
a portable trace you can open and read. Every package ships together at 1.0.0, documented, tested, and
ready for production suites.

ProtoTest is a composable integration-testing foundation: compose capabilities onto a host, and each test
runs through a recorded lifecycle of phases, operations, state changes, checks and findings.

### Added

**Host and lifecycle**

- One `ProtoHost` per test process and one `ProtoExecutionContext` per test, with run hooks and gates, test
  hooks, attributes, typed state, clients, attachments, findings and owned resources.
- Skip conditions (`[RequiresCapability]`, `[RequiresInProcess]`, `[RequiresPlaywrightBrowser]`) that stop a
  test before its lifecycle starts when the host cannot run it.

**Integrations**

- **REST** (`ProtoTest.Rest`): HTTP clients, `[Auth<T>]`, status and shape assertions, REST coverage.
- **GraphQL** (`ProtoTest.GraphQL`): queries, mutations, WebSocket/SSE subscriptions, uploads, schema coverage.
- **gRPC** (`ProtoTest.Grpc`): unary and streaming clients, metadata auth, method coverage.
- **Messaging** (`ProtoTest.Messaging`, `ProtoTest.Messaging.RabbitMq`): publish and await messages on an
  in-memory default broker or RabbitMQ.
- **SQL and EF Core** (`ProtoTest.Sql`, `ProtoTest.Sql.EntityFrameworkCore`): one database connection per test,
  optional transaction isolation, and a `DbContext` over the same connection.
- **Data** (`ProtoTest.Data`): deterministic builders, member defaults, provisioners and the `Ref<T>` identity map.
- **ASP.NET Core in-process** (`ProtoTest.AspNetCore`): host the application inside the test process, with
  server DI access.
- **Browser sessions** (`ProtoTest.Web` with Playwright and Selenium backends): sessions, page objects, flows,
  login and page coverage.
- **Spreadsheets** (`ProtoTest.Sheets`): cell, column, range, table and typed-model assertions for `.xlsx` files.
- **OpenAPI** (`ProtoTest.OpenApi`): contract coverage over REST response and shape observations.
- **Containers** (`ProtoTest.Sql.Testcontainers`, `ProtoTest.Messaging.RabbitMq.Testcontainers`,
  `ProtoTest.Testcontainers`): run-scoped PostgreSQL and RabbitMQ, or your own `ProtoContainerResource<TContainer>`.

**Observability**

- **Coverage** of REST endpoints, OpenAPI documents, GraphQL schemas, gRPC services, web pages and spreadsheet ranges.
- **ProtoTrace** format 2.0: a portable `.prototrace` bundle (`spans.json`, `state.json`) recording what ran and
  what existed and changed, read in the [viewer](https://trace.prototest.dev).
- **Reports** (`ProtoTest.Reporting`): JSON and HTML report sinks.
- **OpenTelemetry bridge** (`ProtoTest.OpenTelemetry`): export ProtoTest operations as OpenTelemetry spans.

**Runners and templates**

- Adapters for **NUnit**, **xUnit v2**, **xUnit v3**, **MSTest** and **TUnit**, sharing one lifecycle and one
  set of outcomes.
- `ProtoTest.Templates`: `dotnet new prototest` creates an API and a suite for it, already composed, traced and
  reported.

**Welcome to 1.0.** Install a package, compose the capabilities your system actually has, and run the same
suite in-process, in containers, or against a published environment. The trace will tell you the rest.

[1.0.1]: https://github.com/MSeys/ProtoTest/releases/tag/v1.0.1
[1.0.0]: https://github.com/MSeys/ProtoTest/releases/tag/v1.0.0
