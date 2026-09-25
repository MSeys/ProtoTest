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

### Fixed

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

### Changed

- REST object request bodies serialize with the shared web defaults (camelCase names), matching
  GraphQL variables. Pass explicit `JsonSerializerOptions` to keep another naming policy.
- OpenAPI coverage reads specifications with `Microsoft.OpenApi` 3.x (JSON and YAML, including 3.1
  documents). `ProtoTest.OpenApi` references `ProtoTest.Core` directly instead of relying on a
  transitive reference.

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
- `IProtoClientInitializer.TryInitializeAsync` no longer takes a cancellation token. Test setup is
  not cancellable by any runner adapter; a run-scoped hook is the cancellable extension point.
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
