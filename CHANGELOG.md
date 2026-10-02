# Changelog

All notable changes to ProtoTest are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
All ProtoTest packages share one version; breaking API changes are called out below.

## [1.1.0] - 2026-09-30

ProtoTest 1.1 adds the agent evidence layer (the MCP server, diagnosis, verification, feedback and the
CLI), the devices family (WebSocket, MQTT, TCP and serial), the topology integrations (Aspire, WireMock,
Testcontainers), the extended runner surface, and a rewritten documentation site with the Learn track.
It ships 46 packages: 19 new, listed first below, and `ProtoTest.OpenTelemetry` removed. The breaking changes are listed at the end.
See [Migrating from 1.0](https://prototest.dev/docs/getting-started/migrating-from-1-0) for the renames
and the deprecated surface; the [docs](https://prototest.dev/docs/) cover the rest.

### Features

#### New packages

- `ProtoTest.Analyzers`: compile-time rules for what the framework cannot catch at run time.
- `ProtoTest.Aspire`: runs an Aspire AppHost with the suite and publishes its resources as targets.
- `ProtoTest.Cli`: the `prototest` command that summarizes, indexes and verifies traces.
- `ProtoTest.Devices`: typed devices, one per test, with every exchange in the trace.
- `ProtoTest.Devices.WebSocket` and `ProtoTest.Devices.WebSocket.AspNetCore`: devices over WebSocket, or in-process without a socket.
- `ProtoTest.Devices.Mqtt` and `ProtoTest.Devices.Mqtt.Testcontainers`: devices over MQTT, with a Mosquitto broker for the run.
- `ProtoTest.Devices.Tcp` and `ProtoTest.Devices.Serial`: devices over TCP and serial lines, with stream framing.
- `ProtoTest.Diagnosis`: names a failure's cause from the recorded evidence.
- `ProtoTest.Feedback`: posts a run's digest to a pull request, check annotations or a webhook.
- `ProtoTest.Hosting`: hosts background workers in the test process.
- `ProtoTest.Mcp`: an MCP server that lets a coding agent read runs.
- `ProtoTest.Messaging.MassTransit`: uses the application's MassTransit test harness as the broker.
- `ProtoTest.Traces`: reads `.prototrace` archives for the CLI, MCP and tools.
- `ProtoTest.Verification`: compares a run with a baseline, by report and by trace.
- `ProtoTest.Web.Pages`: the page model and coverage, shared by the web backends and ASP.NET Core.
- `ProtoTest.WireMock`: WireMock.Net fakes owned by a test or the run.

#### Core

- readiness probes replace setup sleeps (`AddReadinessProbe`, `ProtoReadiness.Tcp`/`.Http`). [Infrastructure](https://prototest.dev/docs/foundation/infrastructure)
- one readiness policy (`ProtoTest:Readiness`) tunes host probes and containers. [Infrastructure](https://prototest.dev/docs/foundation/infrastructure)
- a per-test clock replaces sleeping (`ConfigureClock`, `Proto.Context.Clock`, `Advance`/`SetUtcNow`). [Time](https://prototest.dev/docs/foundation/time)
- targets resolve through ordered provider chains; losers record named skip reasons. [Environment resolution](https://prototest.dev/docs/foundation/environment-resolution)
- applications, workers and containers declare one chain (`UseConfigured`, `UseInProcess`). [Environment resolution](https://prototest.dev/docs/foundation/environment-resolution)
- `[RequiresTestClock]` skips tests that need the test clock without a provider that bridges it. [Skip conditions](https://prototest.dev/docs/foundation/skip-conditions)
- `IProtoTargetProvider.ConfigureServices` and `ResolveAfter` are public. [Environment resolution](https://prototest.dev/docs/foundation/environment-resolution)
- infrastructure with every declared key configured is skipped, unless `AddInfrastructureAlways`. [Infrastructure](https://prototest.dev/docs/foundation/infrastructure)
  - The old `AddInfrastructure(piece, keys)` skip rule is deprecated; the provider chain replaces it. [Migrating from 1.0](https://prototest.dev/docs/getting-started/migrating-from-1-0)
- `IProtoConfiguredInfrastructure` hands infrastructure the run's settings as it starts. [Infrastructure](https://prototest.dev/docs/foundation/infrastructure)
- `AddRunSetup(name, delegate)` adds a run-scoped step after the pieces registered before it. [Infrastructure](https://prototest.dev/docs/foundation/infrastructure-recipes#run-scoped-setup)
- composite attributes expand recursively where attributes resolve, with cycles throwing the chain. [Attributes](https://prototest.dev/docs/foundation/attributes#composite-attributes)
- `ProtoCapabilityDescriptor.Instance` names the instance a capability describes. [Skip conditions](https://prototest.dev/docs/foundation/skip-conditions)
- `AddCapabilityWhenProvided`/`AddCapabilityUnlessConfigured` gate capabilities on keys. [Skip conditions](https://prototest.dev/docs/foundation/skip-conditions)
- one application-address precedence: a published address wins over configuration. [Environment resolution](https://prototest.dev/docs/foundation/environment-resolution)
- `Proto.Context.UniqueName("tenant")` derives a persistent-store-safe name from the test id. [Execution context](https://prototest.dev/docs/foundation/execution-context#unique-names)
- `Proto.Context.Sql()` is the primary accessor; `Messaging(name)` accepts a name. [Execution context](https://prototest.dev/docs/foundation/execution-context#clients)
- typed skip gates (`[RequiresWorker<T>]`, `[RequiresServer]`, `[RequiresApplication]`). [Skip conditions](https://prototest.dev/docs/foundation/skip-conditions)
- `AddCapabilityReason` states a skip reason once for the suite, a kind or a name. [Skip conditions](https://prototest.dev/docs/foundation/skip-conditions)
- `ProtoHost.HasApplication(name)` and `HasCapability(kind, name, instance)` answer the typed skip gates. [Skip conditions](https://prototest.dev/docs/foundation/skip-conditions)
- `ProtoResourceState.Releasing` reports a resource while its release callback runs. [Lifecycle](https://prototest.dev/docs/foundation/lifecycle)
- `StartTestAsync` accepts a cancellation token on `ProtoExecutionContext`. [Execution context](https://prototest.dev/docs/foundation/execution-context#cancellation)
- `ProtoTest.Json` hosts the shared JSON read, failure diagnostics and observation capture. [Extending](https://prototest.dev/docs/advanced/extending)
- `IProtoConfigurableOptions.FallbackConfigurationSectionName` keeps an old section working. [Configuration](https://prototest.dev/docs/getting-started/configuration)
- `ProtoOptionsRegistration.ConfigureKeyed` registers keyed per-client options. [Configuration](https://prototest.dev/docs/getting-started/configuration)
- the extension points a third-party integration needs are public. [Extending](https://prototest.dev/docs/advanced/extending)
- a bare client name resolves across applications; an ambiguous name names both candidates. [Clients](https://prototest.dev/docs/foundation/clients#fallback-chains)
- clients use protocol-scoped names and one registration path per integration. [Clients](https://prototest.dev/docs/foundation/clients)
- one `Should` assertion surface across protocols; request, message and reply assertions return their subject and chain, while Web element assertions are async. [Assertions](https://prototest.dev/docs/foundation/assertions)
- shape matching lives under `Should.MatchShape`; old spellings stay as shims. [Shape matching](https://prototest.dev/docs/foundation/shape-matching)
- exhaustive shape mode (`exact: true`) works on every shape surface through one matcher walk. [Shape matching](https://prototest.dev/docs/foundation/shape-matching#exact-matching)
- OpenAPI and GraphQL collectors record the specification identity (`spec.source`, `spec.hash`). [Coverage](https://prototest.dev/docs/observability/coverage)
- `ProtoProtocol.CoverageCategory` is optional and falls back to the protocol name. [Coverage](https://prototest.dev/docs/observability/coverage#writing-a-collector)
- `ConfigureTracing` captures CI facts with `RunMetadata` and `RunMetadataEnvironmentVariables`. [ProtoTrace](https://prototest.dev/docs/observability/prototrace)
- `ProtoApplication.ResolveSetting` publishes the settings-over-configuration precedence. [Environment resolution](https://prototest.dev/docs/foundation/environment-resolution)
- `ProtoAttributeOrder` names the setup order bands (`Application`, `SessionDeclaration`, `Default`). [Hooks](https://prototest.dev/docs/foundation/hooks#reserved-order-bands)
- suites name their own sensitive values (`ConfigureRedaction`, `ProtoTest:Redaction`); state and findings redact them like the defaults. [ProtoTrace](https://prototest.dev/docs/observability/prototrace)

#### Hosting

- `AddWorkerHost<TProgram>()` runs a background worker or generic host in-process with the suite. [Hosting](https://prototest.dev/docs/integrations/hosting)

#### ASP.NET Core

- `ProtoTestHost.For<TProgram>(builder)` composes an in-process application in one line. [ASP.NET Core](https://prototest.dev/docs/integrations/aspnetcore)
- `AddAspNetCoreServer` steps aside when `BaseUrl` is configured. [ASP.NET Core](https://prototest.dev/docs/integrations/aspnetcore#real-server-or-in-process)
- `AddLoopbackApplication` hosts a hand-built `WebApplication` on loopback. [ASP.NET Core](https://prototest.dev/docs/integrations/aspnetcore#hosting-a-browser-journey)
- per-test service substitution (`Override`, `[ReplaceService]`) builds a dedicated server. [ASP.NET Core](https://prototest.dev/docs/integrations/aspnetcore)
- `[SignedInAs]` signs a test in; `AddTestUserAuthentication()` reads it app-side. [Authentication](https://prototest.dev/docs/integrations/rest/authentication#built-in-test-user)
- `ProtoTestContextPropagation` applies the trace context and test id to raw requests. [ASP.NET Core](https://prototest.dev/docs/integrations/aspnetcore)

#### REST and HTTP

- read one value by JSON path (`ReadAsJson<T>("$.id")`), with the protocol's failure evidence. [Responses](https://prototest.dev/docs/integrations/rest/responses)
- `ReadRequired<T>` returns `T` and fails naming the subject or path when the body is empty or null. [Responses](https://prototest.dev/docs/integrations/rest/responses)
- `Should`/`ShouldNot` assert content type, headers, cookies and redirect location. [Responses](https://prototest.dev/docs/integrations/rest/responses)
- `PostAsync(...).ExpectAsync(shape)` asserts the response shape where the call is made. [Responses](https://prototest.dev/docs/integrations/rest/responses)
- `RestTrafficCoverageCollector` reports the fields no shape assertion mentioned. [Coverage](https://prototest.dev/docs/observability/coverage)
- protocol builders adopt the public authenticator, base-address and options resolvers. [Extending](https://prototest.dev/docs/advanced/extending)

#### GraphQL

- `ReadDataAs<T>(jsonPath)` and `ReadRequired<T>` read data or fail naming the operation. [Responses](https://prototest.dev/docs/integrations/graphql/responses)

#### gRPC

- client options bind `ProtoTest:Grpc:Client`; the legacy `ProtoTest:Grpc` section still binds as a fallback. [gRPC](https://prototest.dev/docs/integrations/grpc/#options-and-keys)
- `ProtoGrpcBuilder.ProtocolName` names the protocol key. [gRPC](https://prototest.dev/docs/integrations/grpc/)

#### Messaging

- `ProtoMessage.MatchesShape(shape)` picks a message in an `AwaitAsync` predicate with the shared shape matcher: true or false, nothing recorded, false for an empty or non-JSON payload. [Messaging](https://prototest.dev/docs/integrations/messaging/)
- `ProtoDestination.Queue(name)` awaits a named queue; adapters without queues refuse it by name. [Messaging](https://prototest.dev/docs/integrations/messaging/)
- `Declare(...)` or `ProtoTest:Messaging:DeclaredDestinations` declares suite-owned destinations. [Messaging](https://prototest.dev/docs/integrations/messaging/)
- taps gain a code API (`AddMessaging(m => m.Tap(...))`) that pre-binds destinations during setup. [Messaging](https://prototest.dev/docs/integrations/messaging/)
- messages carry a routing key, and `PublishAsync`/`AwaitAsync` accept one. [Messaging](https://prototest.dev/docs/integrations/messaging/)
- `ProtoMessage` reads are typed (`ReadAsJson<T>`, `ReadRequired<T>`). [Messaging](https://prototest.dev/docs/integrations/messaging/)
- `UseBroker(factory, addressKeys)` gates the Broker capability on an address key. [Messaging](https://prototest.dev/docs/integrations/messaging/)
- `UseMassTransit<Program>()` publishes and awaits over the app's MassTransit harness. [MassTransit](https://prototest.dev/docs/integrations/messaging/masstransit)
- `MassTransitEnvelope.Wrap`/`Unwrap` build and read the wire envelope, addresses included. [MassTransit](https://prototest.dev/docs/integrations/messaging/masstransit#envelope-interop)
- `ProtoMessageConsumerBase` exposes the one await contract to adapters. [Messaging](https://prototest.dev/docs/integrations/messaging/adapters#writing-an-adapter)
- RabbitMQ taps prepare and release in parallel, cutting per-test setup and teardown. [Messaging](https://prototest.dev/docs/integrations/messaging/)

#### Devices

- `ProtoTest.Devices` talks to simulators and hardware from the test context and trace. [Devices](https://prototest.dev/docs/integrations/devices)
- `AddInProcessWebSocketDevices` routes device clients through the in-process server. [Devices](https://prototest.dev/docs/integrations/devices)
- clients carry per-client transport settings (`WithSetting`) filled into `DeviceEndpoint.Settings`. [Devices](https://prototest.dev/docs/integrations/devices)
- `ProtoTest.Devices.Mqtt` speaks MQTT 5; `AddMqttClient` declares the topics. [Devices](https://prototest.dev/docs/integrations/devices#mqtt)
- `ProtoTest.Devices.Mqtt.Testcontainers` owns a Mosquitto broker for the run. [Devices](https://prototest.dev/docs/integrations/devices)
- `ProtoDeviceConnect` and its timeout live in `ProtoTest.Devices`, shared by every transport. [Devices](https://prototest.dev/docs/integrations/devices)
- `IProtoDeviceTransport.ConnectAsync` gains a default context overload for custom transports. [Devices](https://prototest.dev/docs/integrations/devices)
- `ProtoTest.Devices.Tcp` connects devices out (`AddTcpClient`) or listens for the system under test (`AddTcpListener`, `ListenAsync`). [Devices](https://prototest.dev/docs/integrations/devices#tcp-and-serial)
- `ProtoTest.Devices.Serial` opens `serial://` lines; a client without an address reads `ProtoTest:Devices:Serial:Ports:{client}`. [Devices](https://prototest.dev/docs/integrations/devices#tcp-and-serial)
- stream framing (`IDeviceFramer`, `DeviceFramers`, `WithFramer`) and `StreamDeviceConnection` serve any byte-stream transport. [Devices](https://prototest.dev/docs/integrations/devices#tcp-and-serial)
- `DeviceMessage` writes and reads device data strings as records, with `SendMessageAsync`/`ExpectMessageAsync`. [Devices](https://prototest.dev/docs/integrations/devices#data-strings-as-messages)
- `[DeviceChecksum<T>]` and `[DeviceFormat<T>]` add checksums and field formatters (`DeviceFormatters`: scaled, hex, Unix time, BCD). [Devices](https://prototest.dev/docs/integrations/devices#formatters)
- `[DeviceBinaryMessage]` writes and reads fixed-layout binary messages, with `Crc16Modbus`, `Xor8Checksum` and `Sum8Checksum`. [Devices](https://prototest.dev/docs/integrations/devices#binary-messages)
- `DeviceMessageProtocol` and `AddProtocol(instance)` report the message types no test expected. [Devices](https://prototest.dev/docs/integrations/devices#coverage-per-message-type)

#### Web

- `WebPageInventory.VisitedObservationKind`/`VerifiedObservationKind` name the page observation kinds. [Web](https://prototest.dev/docs/integrations/web/)
- the backend building blocks are public (`WebBackendOptions`, `WebBackendErrors`, `WebBackendDefaults`). [Web](https://prototest.dev/docs/integrations/web/)
- `PlaywrightWebOptions.MaxTraceBytes` caps a browser trace; 0 turns the cap off. [Web](https://prototest.dev/docs/integrations/web/)
- sessions are created when requested and complete through the shared client lifecycle. [Web](https://prototest.dev/docs/integrations/web/)
- sessions key by name and application, and `Web()` without an application finds the one session with that name, such as one `[WebSession(Application = ...)]` opened. [Web](https://prototest.dev/docs/integrations/web/)
- `WebDownload` implements `IProtoBinaryContent`, so a download feeds `ProtoSheets.Open` in one line. [Web](https://prototest.dev/docs/integrations/web/)

#### Sheets

- `model.Should.MatchHeaders()` asserts the header row against the model in declaration order. [Sheets](https://prototest.dev/docs/integrations/sheets/)
- key-value sheets declare labels with `[Label]` and read through `Workbook.KeyValueModel<T>()`. [Sheets](https://prototest.dev/docs/integrations/sheets/)

#### SQL and Data

- `ProtoDataValueContext.Clock` gives defaults the test clock, so a default stamp follows `Proto.Context.Clock`. [Defaults](https://prototest.dev/docs/integrations/data/defaults#the-value-context)
- `SqlOptions.AddressKeys` declares the connection keys and gates the SQL and EF Core capabilities. [SQL](https://prototest.dev/docs/integrations/sql/)
- `SqlAddressRule` publishes the declared-keys lookup for a sibling SQL provider. [SQL](https://prototest.dev/docs/integrations/sql/)
- generated member defaults are deterministic per test. [Defaults](https://prototest.dev/docs/integrations/data/defaults)

#### Testcontainers

- `UseContainer` and `DockerProbe.IsAvailable()` skip without a Docker runtime. [Infrastructure](https://prototest.dev/docs/foundation/infrastructure)
- `ApplicationContainer` runs an application under test in its own image. [Infrastructure](https://prototest.dev/docs/foundation/infrastructure)

#### Aspire

- `AddAspireAppHost<TEntryPoint>()` runs an AppHost when selected and publishes its resources' addresses. [Aspire](https://prototest.dev/docs/integrations/aspire)
- `UseAspireResource` serves an endpoint or connection string through the provider chain. [Aspire](https://prototest.dev/docs/integrations/aspire#serving-targets-through-the-chain)
- the AppHost receives the suite's configuration and the run's settings as arguments. [Aspire](https://prototest.dev/docs/integrations/aspire)

#### WireMock

- `AddWireMock(name)` fakes HTTP dependencies per test, with stubs as coverage gaps until hit. [WireMock](https://prototest.dev/docs/integrations/wiremock)

#### OpenAPI

- coverage reads specifications with `Microsoft.OpenApi` 3.x (JSON and YAML, including 3.1). [OpenAPI](https://prototest.dev/docs/integrations/openapi)

#### Runners and analyzers

- opt-in low-ceremony mode (`[assembly: ProtoTestAutoWrap]`) wraps plain tests in the lifecycle. [Runners](https://prototest.dev/docs/runners/overview)
- one outcome classifier (`ProtoTestResult`) and row-name helper (`ProtoTestName.ForRow`). [Runners](https://prototest.dev/docs/runners/overview)
- xUnit v3 and TUnit pass their per-test cancellation token into the lifecycle; MSTest's 4.0.2 floor exposes none. [Execution context](https://prototest.dev/docs/foundation/execution-context#cancellation)
- `dotnet new prototest --runner <name>` (nunit, xunit, xunit3, tunit or mstest) writes a suite per runner. [Overview](https://prototest.dev/docs/integrations/overview)
- `ProtoTest.Analyzers` reports `PT0001` and `PT0002` for intent the runtime cannot check. [Analyzers](https://prototest.dev/docs/project/analyzers)
- `PT0003` flags a fixed wait and `PT0004` a hand-made `HttpClient` in a ProtoTest test. [Analyzers](https://prototest.dev/docs/project/analyzers)

#### Agent workflows

- `prototest summary` prints the diagnosis document. [CLI](https://prototest.dev/docs/agent-workflows/cli#summary)
- `prototest index <folder>` writes a static index and digest for a folder of runs. [CLI](https://prototest.dev/docs/agent-workflows/cli#index)
- `prototest feedback` and `prototest verify` post the channels and print a verdict. [CLI](https://prototest.dev/docs/agent-workflows/cli)
- `prototest-mcp` reads `.prototrace` files over stdio (`list_runs`, `get_failure`, `get_coverage`). [Setup](https://prototest.dev/docs/agent-workflows/setup)
- `get_diagnosis` returns the run digest or one failing test's context package. [Diagnosis](https://prototest.dev/docs/agent-workflows/diagnosis)
- `get_suite_map` lists what a new test reuses: clients, provisioners, attributes, pages, examples and gaps. [Setup](https://prototest.dev/docs/agent-workflows/setup#what-the-agent-can-see)
- the `prototest-write-test` skill teaches writing a test on top of the suite and proving it. [Coding agents](https://prototest.dev/docs/agent-workflows/coding-agents)
- `dotnet new prototest` starts agent-ready: `AGENTS.md`, `.mcp.json`, both skills, a tool manifest and the analyzers. [Coding agents](https://prototest.dev/docs/agent-workflows/coding-agents)
- the demo endpoint (`samples/ProtoTest.Mcp.DemoEndpoint`) serves the same tools over the demo trace. [Coding agents](https://prototest.dev/docs/agent-workflows/coding-agents#demo-endpoint)
- `ProtoDiagnosis.Read` builds a deterministic digest; `ReadContext` adds one test's context. [Diagnosis](https://prototest.dev/docs/agent-workflows/diagnosis)
- `ProtoTest.Verification` compares a baseline and a candidate report. [Verification](https://prototest.dev/docs/agent-workflows/verification)
- `prototest compare` and the `compare_runs` tool compare two runs test by test and name the operation where each broken, fixed or still-failing test left the baseline. [CLI](https://prototest.dev/docs/agent-workflows/cli#compare)
- `ProtoTest.Feedback` posts a failing run's digest to a PR comment, annotations or a webhook. [Loop](https://prototest.dev/docs/agent-workflows/loop)
- the ProtoTest Feedback GitHub Action uploads the trace and posts the digest. [CI](https://prototest.dev/docs/continuous-integration/#the-feedback-action)

#### Traces and reporting

- run and test artifacts are declared and readable (`ProtoTraceArchive.Artifacts`, `ReadArtifact`). [ProtoTrace](https://prototest.dev/docs/advanced/extending#reading-a-trace-in-code)
- `ProtoTest.Traces` reads the whole 2.0 archive and selects the viewer's failure. [ProtoTrace](https://prototest.dev/docs/observability/prototrace)
- `ProtoReport.ReadJson` reads the JSON report a sink wrote. [Reporting](https://prototest.dev/docs/observability/reporting#the-json-report)
- the HTML report ends with a quiet pointer to the viewer, naming the `.prototrace` to drop there. [Reporting](https://prototest.dev/docs/observability/reporting)

#### Viewer

- one ViewerMock component draws every viewer picture in the docs (the walkthrough's six views, the home's failing check and the coverage page's run header) from one file of the demo run's values, taken from the viewer itself; Timeline, State and Evidence now show the viewer's own rows, counts and positions. [ProtoTrace](https://prototest.dev/docs/observability/prototrace)
- the walkthrough, ProtoTrace guide and viewer README describe Steps, Timeline, State and Evidence, diagnosis rules, untraced gaps and run selections, with updated demo excerpts. [ProtoTrace](https://prototest.dev/docs/observability/prototrace)
- State uses the same test clock and phase marks as Timeline, shades the selected operation's time across the lifelines, and highlights the items and changes it touched. The ruler stays visible on phones. [ProtoTrace](https://prototest.dev/docs/observability/prototrace)
- the run names attention with the diagnosis rules, marks untraced gaps on its timeline, lists every environment value and the run id, and opens run operations and tracked items in the inspector. [ProtoTrace](https://prototest.dev/docs/observability/prototrace)
- Evidence brings observations, files, findings and moments into time order with the operation that recorded each; details show their metadata and sections, a section index jumps through an operation, and binary bodies are marked instead of drawn as broken text (`#/test/<id>/files` links still open). [ProtoTrace](https://prototest.dev/docs/observability/prototrace)
- one test list on every screen, led by the run, with one filter for every list and a path from the run to the open operation; each test is a link. [ProtoTrace](https://prototest.dev/docs/observability/prototrace)
- a test's Steps open on the test body: setup and teardown fold into one line that says what they did, time with no recorded operation gets its own row, and the verdict names the failure with the rule `prototest summary` uses (`#/test/<id>/story` links still open). [ProtoTrace](https://prototest.dev/docs/observability/prototrace)
- the Timeline replaces Spans: every operation on the test's clock, zoomed per phase, with framework machinery dimmed or hidden, moments and evidence marked on their bars, and untraced time drawn through the rows (`#/test/<id>/spans` links still open). [ProtoTrace](https://prototest.dev/docs/observability/prototrace)
- the viewer stays responsive on a 1,000-test run; the benchmark page states the open and search times. [Benchmarks](https://prototest.dev/docs/project/benchmarks)
- a skipped test reads as planned in the run strip, keyboard focus and touch targets work across the viewer, and the run header's outcome pill moves under the title on narrow screens. [Trace viewer](https://trace.prototest.dev)
- each screen answers its question in order: failing rows name their reason, the failure names where in the test it started, step rows read stated observations inline, and an empty evidence list separates no files from no match. [Trace viewer](https://trace.prototest.dev)
- the run splits into Overview, Timeline, Operations, Details and Files instead of stacking every panel. [ProtoTrace](https://prototest.dev/docs/observability/prototrace)
- a Framework switch shows, dims or hides framework operations in Steps and Timeline, and the machinery in State. [ProtoTrace](https://prototest.dev/docs/observability/prototrace)
- the viewer works on a phone and with a keyboard: one tablist with arrow keys, readable rows on narrow screens, and text that meets WCAG AA. [ProtoTrace](https://prototest.dev/docs/observability/prototrace)

#### Docs and samples

- a copy-in skill (`skills/prototest-evidence-loop`) teaches agents the evidence loop and the CLI. [Coding agents](https://prototest.dev/docs/agent-workflows/coding-agents#the-skills-bundle)
- the xUnit pages cover converting an existing suite and the Microsoft.Testing.Platform opt-in on SDK 10. [Runners](https://prototest.dev/docs/runners/overview)
- the integrations overview lists every `ProtoTest.Devices*` package with a one-line purpose. [Overview](https://prototest.dev/docs/integrations/overview)
- the 46 packages are tiered into supported and preview sets, with the stability promise and the graduation path stated. [Installation](https://prototest.dev/docs/getting-started/installation)
- the web pages make the backend choice explicit (Playwright or Selenium), and the conversion order covers NUnit, MSTest and TUnit suites. [Web](https://prototest.dev/docs/integrations/web)
- the agent workflows document the feedback webhook payload, the configuration page lists every section's keys, and the template page shows what the scaffold creates. [CLI](https://prototest.dev/docs/agent-workflows/cli)
- the reference pages gain the recorded trace walks, the decision figures and the triage tables; the longest pages split into child pages (web page coverage, messaging adapters, gRPC calls, the ProtoTrace archive, the CI providers). [Docs](https://prototest.dev/docs/)
- the home opens on the blueprint hero with the journey as test, composition and trace, then proves three claims with real artefacts: the plumbing each fixture stops writing, the failing check as the viewer shows it, and one model across integrations; the starter includes the working directory and runner options; the integration pages open with a shown test and keep reference detail below the tasks. [Docs](https://prototest.dev/docs/)
- tab strips in code and the viewer walkthrough scroll with a plain mouse wheel, fade only where there is more, and keep the selected tab in view. [Docs](https://prototest.dev/docs/)
- tabbed code keeps its copy button beside the tabs on a phone and fades the tab strip where it scrolls, and every figure in running text keeps a paragraph's distance to the text after it. [Docs](https://prototest.dev/docs/)
- tables read on a phone: every docs table labels its cells with the column heading at build time, and below 700px a row becomes a short card with the first cell as its title; the Level 5 modes comparison does the same, and the failure tour labels its questions quietly and says what opening a card shows. [Docs](https://prototest.dev/docs/)
- the package builder reads its choices from the integrations catalog, so it offers every shipped integration (Selenium, RabbitMQ, SQL and its containers, workers, Aspire, WireMock, devices, analyzers) grouped like the map, and a docs test keeps the catalog equal to the shipped packages. [Integrations](https://prototest.dev/docs/integrations/overview)
- trace figures speak the viewer's language: the layer-by-layer trace marks each phase in its colour with quiet operation rows, and a pointer to a recorded test reads as a viewer test row with an Open in the viewer button; wrapped code keeps its first token beside the line number. [ProtoTrace](https://prototest.dev/docs/observability/prototrace)
- every Learn lesson is rewritten to one shape (the problem, do it, what happened, check yourself, remember) in plain Markdown with a lesson banner, and Across boundaries adds five lessons built on the sample's own tests: call an API, read back over GraphQL, check the database, drive the browser and follow a message. [Learn](https://prototest.dev/learn)
- Learn is regrouped into seven tracks (Start, Write good integration tests, Across boundaries, Reliable tests, Understand failures, Real systems, Extend ProtoTest), shown as cards read from the sidebar; every old lesson address redirects, and lessons get a Markdown-first shape with a small lesson banner. [Learn](https://prototest.dev/learn)
- the Vocabulary page explains every ProtoTest word in one plain sentence and links the lesson that teaches it and the page with the details; lessons explain a word again at its first use. [Vocabulary](https://prototest.dev/docs/foundation/vocabulary)
- the API docs of `UniqueName`, `SignedInAsAttribute.Claims`, `IProtoMessageConsumer` and the readiness probe say what they guarantee: unique within a host (a rerun matches only with an id generator that derives from the test), claim types only in `auth:user`, isolation per exchange tap. [Test context](https://prototest.dev/docs/foundation/execution-context)
- Getting started reads plainly: each page says what it is for, introduces its terms in plain words, says what you should see after each step, and folds what only some readers need; the troubleshooting page no longer presents the CI examples' `PROTOTEST_RESULTS` variable as a ProtoTest setting. [Getting started](https://prototest.dev/docs/getting-started/installation)
- Start here opens with one paragraph and four starting points (try it, learn it, weigh it, use it) in two columns, and the sidebar narrows between tablet and wide screens so the text keeps its measure. [Docs](https://prototest.dev/docs/)
- the READMEs follow one shape per kind, from the root to the package pages. [Docs](https://prototest.dev/docs/)
- Northstar with the Learning demo suite is the in-repo sample. [Learn](https://prototest.dev/learn)
- a message-to-fix table in troubleshooting, a where-your-evidence-goes map and a which-runner chooser. [Troubleshooting](https://prototest.dev/docs/getting-started/troubleshooting)

#### Packaging

- 46 packages pack in one version, 18 more than 1.0; the installation page lists the supported and preview tiers. [Installation](https://prototest.dev/docs/getting-started/installation)

### Fixes

#### Core

- assertion messages across GraphQL, gRPC, messaging, REST and Sheets use a plain hyphen separator. [Assertions](https://prototest.dev/docs/foundation/assertions)
- ambient-context errors name the fix (`FindTraceWriter`, `SetContext`, the runner setup). [Lifecycle](https://prototest.dev/docs/foundation/lifecycle)
- trace snapshots can be read while other tests record observations, attachments and findings. [ProtoTrace](https://prototest.dev/docs/observability/prototrace)
- diagnostic request URLs keep a root-relative path on Linux and macOS instead of turning it into a `file://` address. [ProtoTrace](https://prototest.dev/docs/observability/prototrace)
- `Build()` is terminal; every public registration after it throws the single-build message. [Lifecycle](https://prototest.dev/docs/foundation/lifecycle)
- shape mismatches with value constraints record every mismatch, not an internal type name. [Shape matching](https://prototest.dev/docs/foundation/shape-matching)
- malformed JSON bodies and report metadata are redacted with the same policy as the trace. [Attachments](https://prototest.dev/docs/foundation/attachments)
- a failed run start unwinds completed run hooks in reverse and keeps the trace silent. [Lifecycle](https://prototest.dev/docs/foundation/lifecycle)
- a scope disposed off its async flow records a `Lifecycle` finding and throws. [Lifecycle](https://prototest.dev/docs/foundation/lifecycle)
- a throwing run gate keeps its exception and stack in the trace and report metadata. [Hooks](https://prototest.dev/docs/foundation/hooks)
- telemetry capture failures record one coalesced `telemetry.capture_failed` event. [ProtoTrace](https://prototest.dev/docs/observability/prototrace)
- `[Application]` runs before every other setup attribute. [Attributes](https://prototest.dev/docs/foundation/attributes)
- a sink added directly to the service collection is exported once, like one added with `AddSink`. [Reporting](https://prototest.dev/docs/observability/reporting)
- gRPC and messaging share one failure record and the `{protocol}.diagnostics.failed` event. [ProtoTrace](https://prototest.dev/docs/observability/prototrace)
- `ResolveState` returns only the state the `[Application]` attribute set during the lifecycle. [ASP.NET Core](https://prototest.dev/docs/integrations/aspnetcore)
- container starts and device connects are bounded on release and recorded as abandoned. [Lifecycle](https://prototest.dev/docs/foundation/lifecycle)
- an off-flow dispose releases the orphaned test's context before it throws. [Lifecycle](https://prototest.dev/docs/foundation/lifecycle)

#### ASP.NET Core

- the in-process HTTP client is owned by its test, fixing a teardown crash. [ASP.NET Core](https://prototest.dev/docs/integrations/aspnetcore)
- a repeated `AddAspNetCoreServer` name with another program throws naming both. [ASP.NET Core](https://prototest.dev/docs/integrations/aspnetcore)

#### REST and HTTP

- a client bound to a configured or published address gets its own handler and cookie jar per test. [Clients](https://prototest.dev/docs/foundation/clients)
- response and attachment defaults bind `ProtoTest:Http:Responses`; attachments stay opt-in. [Attachments](https://prototest.dev/docs/integrations/rest/attachments)
- binary request and response bodies are traced as bytes with their media type. A binary response records its media type and length; it stores no decoded body, no JSON body block and no decoded text in a status assertion. [Responses](https://prototest.dev/docs/integrations/rest/responses)

#### GraphQL

- a null GraphQL execution result is tolerated, and subscription events are disposed as they advance. [Subscriptions](https://prototest.dev/docs/integrations/graphql/subscriptions)
- a rejected subscription names the server's errors. [Subscriptions](https://prototest.dev/docs/integrations/graphql/subscriptions)
- a fluent upload argument fails when the document is built instead of rendering as a literal. [GraphQL](https://prototest.dev/docs/integrations/graphql)

#### gRPC

- a transport with no base address fails naming `AddClient` and the application's keys. [gRPC](https://prototest.dev/docs/integrations/grpc/)
- client options are per named client; the shared section binds over each callback. [gRPC](https://prototest.dev/docs/integrations/grpc/#options-and-keys)
- a call that misses its deadline in-process reports `DeadlineExceeded`, as over a socket, instead of the transport's abort. [gRPC](https://prototest.dev/docs/integrations/grpc/#limits)

#### Messaging

- awaiting two destinations no longer misses the second destination's first delivery. [Messaging](https://prototest.dev/docs/integrations/messaging/)
- awaits serialize and an unmatched delivery is kept for a later await, across brokers. [Messaging](https://prototest.dev/docs/integrations/messaging/)
- `UseRabbitMq` declares the Broker capability only when a connection string is available. [Messaging](https://prototest.dev/docs/integrations/messaging/)
- `AddMessaging` declares Broker with `UseBrokerWhenInProcess`, replacing `UseBrokerUnlessConfigured`, and MassTransit's Broker follows its application's chain. [MassTransit](https://prototest.dev/docs/integrations/messaging/masstransit)
- a missing or non-amqp RabbitMQ connection string fails naming the configuration key. [Messaging](https://prototest.dev/docs/integrations/messaging/)
- `UseRabbitMq` runs its options callback once per registration. [Messaging](https://prototest.dev/docs/integrations/messaging/)
- RabbitMQ uses `RabbitMQ.Client` 7.x end to end (async connect, channel, publish, consume). [Messaging](https://prototest.dev/docs/integrations/messaging/)
- a tap with a missing exchange fails only the tests that await it, with the named error. [Messaging](https://prototest.dev/docs/integrations/messaging/)
- publishing to a released broker throws instead of reopening, and a repeated release is a no-op. [Messaging](https://prototest.dev/docs/integrations/messaging/)

#### Web

- a failed backend creation is no longer cached, so a session retries from a clean slate. [Web](https://prototest.dev/docs/integrations/web/)
- only the backend that wins the first-wins registration declares the browser capability. [Web](https://prototest.dev/docs/integrations/web/)
- Selenium `Check`/`SelectOption` verify the resulting state or fail within the action timeout. [Interactions](https://prototest.dev/docs/integrations/web/interactions)
- assertions poll at `IWebBackend.PollInterval`; over-cap traces and stuck pumps are recorded. [Diagnostics](https://prototest.dev/docs/integrations/web/diagnostics)

#### Sheets

- `int`/`long` reads accept only finite, integral, in-range values and fail naming the cell. [Sheets](https://prototest.dev/docs/integrations/sheets/)
- record models construct through their mapped primary constructor. [Sheets](https://prototest.dev/docs/integrations/sheets/)
- a repeated `AddSheets` composes its options callbacks instead of keeping the first. [Sheets](https://prototest.dev/docs/integrations/sheets)

#### SQL and Data

- building a value with a `DateTime`, `DateTimeOffset`, `decimal` or `Guid` member no longer overflows the stack while tracing it. [Defaults](https://prototest.dev/docs/integrations/data/defaults)
- optional constructor-parameter defaults win over generated values. [Defaults](https://prototest.dev/docs/integrations/data/defaults)
- a run whose declared keys are unprovided keeps the hooks inert and names the required gate. [SQL](https://prototest.dev/docs/integrations/sql/)

#### Testcontainers

- container readiness honors the run's readiness policy. [Infrastructure](https://prototest.dev/docs/foundation/infrastructure)
- a released container can be started again, and its connection string is cleared. [Infrastructure](https://prototest.dev/docs/foundation/infrastructure)

#### Runners and analyzers

- an NUnit test body that throws is recorded failed with its exception and source location. [NUnit](https://prototest.dev/docs/runners/nunit)
- a body `OperationCanceledException` records `Cancelled` under xUnit v2 as under the other adapters. [Runners](https://prototest.dev/docs/runners/overview)
- TUnit parameterized rows record their arguments in the trace name. [TUnit](https://prototest.dev/docs/runners/tunit)
- the MSTest floor accepts the standard template's version, and every runner page states its framework minimum. [Runners](https://prototest.dev/docs/runners/overview)

### Breaking changes

These are the 1.0 to 1.1 migration changes; from 1.1 onward the 1.x surface stays additive.

#### Core

- `AddClientFrom` is removed from the REST and GraphQL builders.
  - Register clients under an application and configure its base URL or endpoints, or use an `AddClient` resolver ([Clients](https://prototest.dev/docs/foundation/clients)).
- registration, observation and runner implementation types are internal; use the public APIs.
- `ProtoHost` can no longer be constructed from a service provider; use `ProtoHostBuilder` ([Lifecycle](https://prototest.dev/docs/foundation/lifecycle)).
- `IProtoClientInitializer.TryInitializeAsync` no longer takes a cancellation token.
  - Pass one to `StartTestAsync`, or use a run hook as the cancellable extension point ([Lifecycle](https://prototest.dev/docs/foundation/lifecycle)).
- `ProtoExecutionContext.RegisterClient<T>` takes `ProtoClientOwnership` instead of a `bool`.
  - `ProtoClientOwnership.Context` is a client the test owns; `ProtoClientOwnership.Caller` is one the caller owns ([Clients](https://prototest.dev/docs/foundation/clients)).
- `ProtoDocumentSource.LoadText` is one method with optional parameters; the explicit overload is gone ([Extending](https://prototest.dev/docs/advanced/extending)).
- shape mismatches throw the protocol's assertion exception, with `JsonShapeMismatchException` as the inner.
  - Code that caught `JsonShapeMismatchException` catches `ProtoAssertionException` ([Migrating from 1.0](https://prototest.dev/docs/getting-started/migrating-from-1-0)).
- `ProtoTest.OpenTelemetry` is retired.
  - Subscribe to the `ProtoTest` source with `.AddSource("ProtoTest")` ([OpenTelemetry](https://prototest.dev/docs/observability/opentelemetry)).

#### ASP.NET Core

- the package now depends on `ProtoTest.Web.Pages` instead of the full `ProtoTest.Web`.

#### REST and HTTP

- `ProtoHttpAuthLifecycleHook` is sealed and takes a `ProtoProtocol` instead of a string.
  - `ProtoHttpClientResolution` is a positional record with `SourceName`, `SourceClientName` and `Deconstruct` gone, and `ProtoHttpClientResolver.Resolve` takes the client name as an optional third argument ([Extending](https://prototest.dev/docs/advanced/extending)).
- object request bodies serialize camelCase by default, matching GraphQL variables.
  - Pass explicit `JsonSerializerOptions` to keep another naming policy ([Migrating from 1.0](https://prototest.dev/docs/getting-started/migrating-from-1-0)).

#### gRPC

- `GrpcAttachmentOptions` no longer derives from the HTTP attachment options, so `SensitiveHeaders` and `SensitiveQueryParameters` are gone.
  - Metadata redaction uses the gRPC client's `SensitiveMetadataKeys` ([gRPC](https://prototest.dev/docs/integrations/grpc/)).
- `ProtoGrpcClient.ServerStreaming` and `DuplexStreaming` moved to the blocking facade, `client.Blocking` ([gRPC](https://prototest.dev/docs/integrations/grpc/)).

#### Messaging

- a publish records the `messaging.published` observation; the operation stays `messaging.publish`.
  - Update a collector that filtered the old observation kind ([Migrating from 1.0](https://prototest.dev/docs/getting-started/migrating-from-1-0)).
- `RabbitMqOptions.PollInterval` is removed; awaits are event-driven and `MessagingOptions.DefaultTimeout` bounds them ([Messaging](https://prototest.dev/docs/integrations/messaging/)).

#### Web

- `ProtoTest:Web:Sessions:{name}` settings no longer configure sessions.
  - Put addresses under `ProtoTest:Applications:{application}` and select with `[WebSession]` or `Web()` ([Web](https://prototest.dev/docs/integrations/web/)).
- the reshape removed `IWebBackend.CurrentAddress`, the old `Web()` overload and Selenium's download surface.
  - The web pages describe the replacements; `CompatibilitySuppressions.xml` records the removals.
- `PlaywrightWebOptions.Context` is removed; configure the browser context with `ConfigureContext` ([Web](https://prototest.dev/docs/integrations/web/)).
- `RequiresPlaywrightBrowserAttribute.Session` is removed; the condition probes the configured browser or channel ([Skip conditions](https://prototest.dev/docs/foundation/skip-conditions)).
- `WebOperationContext.Result` is no longer public ([Web](https://prototest.dev/docs/integrations/web/)).
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

**ProtoTest 1.0 is here.** What began as a stubborn idea, that an integration test should read like the
scenario it describes while the framework quietly owns everything around it, is now a stable foundation
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
