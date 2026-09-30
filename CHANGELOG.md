# Changelog

All notable changes to ProtoTest are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
All ProtoTest packages share one version; breaking API changes are called out below.

## [1.1.0] - 2026-09-30

ProtoTest 1.1 adds the agent evidence layer (the MCP server, diagnosis, verification, feedback and the
CLI), the devices family (WebSocket and MQTT), the topology integrations (Aspire, WireMock,
Testcontainers), the extended runner surface, and a rewritten documentation site with the Learn track.
See [Migrating from 1.0](https://prototest.dev/docs/getting-started/migrating-from-1-0) for the renames
and the deprecated surface; the [docs](https://prototest.dev/docs/) cover the rest.

### Features

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

#### Web

- `WebPageInventory.VisitedObservationKind`/`VerifiedObservationKind` name the page observation kinds. [Web](https://prototest.dev/docs/integrations/web/)
- the backend building blocks are public (`WebBackendOptions`, `WebBackendErrors`, `WebBackendDefaults`). [Web](https://prototest.dev/docs/integrations/web/)
- `PlaywrightWebOptions.MaxTraceBytes` caps a browser trace; 0 turns the cap off. [Web](https://prototest.dev/docs/integrations/web/)
- sessions are created when requested and complete through the shared client lifecycle. [Web](https://prototest.dev/docs/integrations/web/)

#### Sheets

- `model.Should.MatchHeaders()` asserts the header row against the model in declaration order. [Sheets](https://prototest.dev/docs/integrations/sheets/)
- key-value sheets declare labels with `[Label]` and read through `Workbook.KeyValueModel<T>()`. [Sheets](https://prototest.dev/docs/integrations/sheets/)

#### SQL and Data

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
- `dotnet new prototest --runner nunit|xunit|xunit3|tunit|mstest` writes a suite per runner. [Overview](https://prototest.dev/docs/integrations/overview)
- `ProtoTest.Analyzers` reports `PT0001` and `PT0002` for intent the runtime cannot check. [Analyzers](https://prototest.dev/docs/project/analyzers)

#### Agent workflows

- `prototest summary` prints the diagnosis document. [CLI](https://prototest.dev/docs/agent-workflows/cli#summary)
- `prototest index <folder>` writes a static index and digest for a folder of runs. [CLI](https://prototest.dev/docs/agent-workflows/cli#index)
- `prototest feedback` and `prototest verify` post the channels and print a verdict. [CLI](https://prototest.dev/docs/agent-workflows/cli)
- `prototest-mcp` reads `.prototrace` files over stdio (`list_runs`, `get_failure`, `get_coverage`). [Setup](https://prototest.dev/docs/agent-workflows/setup)
- `get_diagnosis` returns the run digest or one failing test's context package. [Diagnosis](https://prototest.dev/docs/agent-workflows/diagnosis)
- the demo endpoint (`samples/ProtoTest.Mcp.DemoEndpoint`) serves the same tools over the demo trace. [Coding agents](https://prototest.dev/docs/agent-workflows/coding-agents#demo-endpoint)
- `ProtoDiagnosis.Read` builds a deterministic digest; `ReadContext` adds one test's context. [Diagnosis](https://prototest.dev/docs/agent-workflows/diagnosis)
- `ProtoTest.Verification` compares a baseline and a candidate report. [Verification](https://prototest.dev/docs/agent-workflows/verification)
- `ProtoTest.Feedback` posts a failing run's digest to a PR comment, annotations or a webhook. [Loop](https://prototest.dev/docs/agent-workflows/loop)
- the ProtoTest Feedback GitHub Action uploads the trace and posts the digest. [CI](https://prototest.dev/docs/continuous-integration/#the-feedback-action)

#### Traces and reporting

- run and test artifacts are declared and readable (`ProtoTraceArchive.Artifacts`, `ReadArtifact`). [ProtoTrace](https://prototest.dev/docs/advanced/extending#reading-a-trace-in-code)
- `ProtoTest.Traces` reads the whole 2.0 archive and selects the viewer's failure. [ProtoTrace](https://prototest.dev/docs/observability/prototrace)
- `ProtoReport.ReadJson` reads the JSON report a sink wrote. [Reporting](https://prototest.dev/docs/observability/reporting#the-json-report)
- the HTML report ends with a quiet pointer to the viewer, naming the `.prototrace` to drop there. [Reporting](https://prototest.dev/docs/observability/reporting)

#### Viewer

- the walkthrough, ProtoTrace guide and viewer README describe Steps, Timeline, State and Evidence, diagnosis rules, untraced gaps and run selections, with updated demo excerpts. [ProtoTrace](https://prototest.dev/docs/observability/prototrace)
- State uses the same test clock and phase marks as Timeline, shades the selected operation's time across the lifelines, and highlights the items and changes it touched. The ruler stays visible on phones. [ProtoTrace](https://prototest.dev/docs/observability/prototrace)
- the run names attention with the diagnosis rules, marks untraced gaps on its timeline, lists every environment value and the run id, and opens run operations and tracked items in the inspector. [ProtoTrace](https://prototest.dev/docs/observability/prototrace)
- Evidence brings observations, files, findings and moments into time order with the operation that recorded each; details show their metadata and sections, a section index jumps through an operation, and binary bodies are marked instead of drawn as broken text (`#/test/<id>/files` links still open). [ProtoTrace](https://prototest.dev/docs/observability/prototrace)
- one test list on every screen, led by the run, with one filter for every list and a path from the run to the open operation; each test is a link. [ProtoTrace](https://prototest.dev/docs/observability/prototrace)
- a test's Steps open on the test body: setup and teardown fold into one line that says what they did, time with no recorded operation gets its own row, and the verdict names the failure with the rule `prototest summary` uses (`#/test/<id>/story` links still open). [ProtoTrace](https://prototest.dev/docs/observability/prototrace)
- the Timeline replaces Spans: every operation on the test's clock, zoomed per phase, with framework machinery dimmed or hidden, moments and evidence marked on their bars, and untraced time drawn through the rows (`#/test/<id>/spans` links still open). [ProtoTrace](https://prototest.dev/docs/observability/prototrace)
- the viewer stays responsive at 1,000+ tests; search, filter and open times drop. [Benchmarks](https://prototest.dev/docs/project/benchmarks)
- skipped ticks read as planned, focus states and hit targets are restored, and the run header's outcome pill drops under the title on narrow screens. [Trace viewer](https://trace.prototest.dev)
- each screen answers its question in order: failing rows name their reason, the failure card states the phase and offset, story rows read stated observations inline, and an empty file list separates no files from no match. [Trace viewer](https://trace.prototest.dev)

#### Docs and samples

- a copy-in skill (`skills/prototest-evidence-loop`) teaches agents the evidence loop and the CLI. [Coding agents](https://prototest.dev/docs/agent-workflows/coding-agents#the-skills-bundle)
- the xUnit pages cover converting an existing suite and the Microsoft.Testing.Platform opt-in on SDK 10. [Runners](https://prototest.dev/docs/runners/overview)
- the integrations overview lists every `ProtoTest.Devices*` package with a one-line purpose. [Overview](https://prototest.dev/docs/integrations/overview)
- the 44 packages are tiered into supported and preview sets, with the stability promise and the graduation path stated. [Installation](https://prototest.dev/docs/getting-started/installation)
- the web pages make the backend choice explicit (Playwright or Selenium), and the conversion order covers NUnit, MSTest and TUnit suites. [Web](https://prototest.dev/docs/integrations/web)
- the agent workflows document the feedback webhook payload, the configuration page lists every section's keys, and the template page shows what the scaffold creates. [CLI](https://prototest.dev/docs/agent-workflows/cli)
- the reference pages gain the recorded trace walks, the decision figures and the triage tables; the longest pages split into child pages (web page coverage, messaging adapters, gRPC calls, the ProtoTrace archive, the CI providers). [Docs](https://prototest.dev/docs/)
- the home opens on the blueprint hero with the journey as test, composition and trace, then proves three claims with real artefacts: the plumbing each fixture stops writing, the failing check as the viewer shows it, and one model across integrations; the starter includes the working directory and runner options; the integration pages open with a shown test and keep reference detail below the tasks. [Docs](https://prototest.dev/docs/)
- tab strips in code and the viewer walkthrough scroll with a plain mouse wheel, fade only where there is more, and keep the selected tab in view. [Docs](https://prototest.dev/docs/)
- tabbed code keeps its copy button beside the tabs on a phone and fades the tab strip where it scrolls, and every figure in running text keeps a paragraph's distance to the text after it. [Docs](https://prototest.dev/docs/)
- tables read on a phone: every docs table labels its cells with the column heading at build time, and below 700px a row becomes a short card with the first cell as its title; the Level 5 modes comparison does the same, and the failure tour labels its questions quietly and says what opening a card shows. [Docs](https://prototest.dev/docs/)
- the package builder reads its choices from the integrations catalog, so it offers every shipped integration (Selenium, RabbitMQ, SQL and its containers, workers, Aspire, WireMock, devices, analyzers) grouped like the map, and a docs test keeps the catalog equal to the shipped packages. [Integrations](https://prototest.dev/docs/integrations/overview)
- trace figures speak the viewer's language: the layer-by-layer trace marks each phase in its colour with quiet operation rows, and a pointer to a recorded test reads as a viewer test row with an Open in the viewer button; wrapped code keeps its first token beside the line number. [ProtoTrace](https://prototest.dev/docs/observability/prototrace)
- Start here opens with one paragraph and four starting points (try it, learn it, weigh it, use it) in two columns, and the sidebar narrows between tablet and wide screens so the text keeps its measure. [Docs](https://prototest.dev/docs/)
- the READMEs follow one shape per kind, from the root to the package pages. [Docs](https://prototest.dev/docs/)
- Northstar with the Learning demo suite is the in-repo sample. [Learn](https://prototest.dev/learn)

### Fixes

#### Core

- assertion messages across GraphQL, gRPC, messaging, REST and Sheets use a plain hyphen separator. [Assertions](https://prototest.dev/docs/foundation/assertions)
- ambient-context errors name the fix (`FindTraceWriter`, `SetContext`, the runner setup). [Lifecycle](https://prototest.dev/docs/foundation/lifecycle)
- `ReplaceClient` with the already-registered instance keeps its owner instead of double-disposing. [Clients](https://prototest.dev/docs/foundation/clients)
- trace snapshots can be read while other tests record observations, attachments and findings. [ProtoTrace](https://prototest.dev/docs/observability/prototrace)
- the test-clock lookup is scoped to its host, so two hosts sharing a prefix keep separate clocks. [Time](https://prototest.dev/docs/foundation/time)
- `ProtoClock.Advance` is atomic, so concurrent advances add up and each records its event. [Time](https://prototest.dev/docs/foundation/time)
- capability conditions are evaluated per declaration; one server no longer skips another. [Skip conditions](https://prototest.dev/docs/foundation/skip-conditions)
- a conditional declaration's key set compares by content, so a repeat leaves one declaration. [Skip conditions](https://prototest.dev/docs/foundation/skip-conditions)
- `Build()` is terminal; every public registration after it throws the single-build message. [Lifecycle](https://prototest.dev/docs/foundation/lifecycle)
- `AddHttpReadiness` names the later publishing piece instead of claiming in-process. [Infrastructure](https://prototest.dev/docs/foundation/infrastructure)
- `AddHttpReadiness` rejects a non-absolute address at run start, naming the configuration key. [Infrastructure](https://prototest.dev/docs/foundation/infrastructure)
- shape mismatches with value constraints record every mismatch, not an internal type name. [Shape matching](https://prototest.dev/docs/foundation/shape-matching)
- malformed JSON bodies and report metadata are redacted with the same policy as the trace. [Attachments](https://prototest.dev/docs/foundation/attachments)
- resources restarted by a retry release with their new ownership period. [Lifecycle](https://prototest.dev/docs/foundation/lifecycle)
- a failed run start unwinds completed run hooks in reverse and keeps the trace silent. [Lifecycle](https://prototest.dev/docs/foundation/lifecycle)
- a scope disposed off its async flow records a `Lifecycle` finding and throws. [Lifecycle](https://prototest.dev/docs/foundation/lifecycle)
- a throwing run gate keeps its exception and stack in the trace and report metadata. [Hooks](https://prototest.dev/docs/foundation/hooks)
- telemetry capture failures record one coalesced `telemetry.capture_failed` event. [ProtoTrace](https://prototest.dev/docs/observability/prototrace)
- `[Application]` runs before every other setup attribute. [Attributes](https://prototest.dev/docs/foundation/attributes)
- a sink added directly to the service collection is exported once, like one added with `AddSink`. [Reporting](https://prototest.dev/docs/observability/reporting)
- readiness waits ride the shared `ProtoPolling` loop, with behavior and messages unchanged. [Infrastructure](https://prototest.dev/docs/foundation/infrastructure)
- a readiness timeout names the probed URL and the last error. [Infrastructure](https://prototest.dev/docs/foundation/infrastructure)
- gRPC and messaging share one failure record and the `{protocol}.diagnostics.failed` event. [ProtoTrace](https://prototest.dev/docs/observability/prototrace)
- `ResolveState` returns only the state the `[Application]` attribute set during the lifecycle. [ASP.NET Core](https://prototest.dev/docs/integrations/aspnetcore)
- container starts and device connects are bounded on release and recorded as abandoned. [Lifecycle](https://prototest.dev/docs/foundation/lifecycle)
- a cancelled step in a collect-mode flow no longer skips the remaining steps; a cancelled flow token still stops the flow. [Lifecycle](https://prototest.dev/docs/foundation/lifecycle)
- an off-flow dispose releases the orphaned test's context before it throws. [Lifecycle](https://prototest.dev/docs/foundation/lifecycle)

#### Hosting

- a worker's `Program.Main` sees the run's configuration as `--key=value` arguments. [Hosting](https://prototest.dev/docs/integrations/hosting)
- a worker entry point always gets the run's `--contentRoot`/`--applicationName`. [Hosting](https://prototest.dev/docs/integrations/hosting)
- `AddWorkerHost` throws for a repeated name with another program. [Hosting](https://prototest.dev/docs/integrations/hosting)
- `ProtoWorkerOptions.Set(key, null)` delivers an empty setting. [Hosting](https://prototest.dev/docs/integrations/hosting)

#### ASP.NET Core

- the in-process HTTP client is owned by its test, fixing a teardown crash. [ASP.NET Core](https://prototest.dev/docs/integrations/aspnetcore)
- a malformed `ProtoTest-User` header leaves the request anonymous instead of a 500. [ASP.NET Core](https://prototest.dev/docs/integrations/aspnetcore)
- a repeated `AddAspNetCoreServer` name with another program throws naming both. [ASP.NET Core](https://prototest.dev/docs/integrations/aspnetcore)
- an unnamed `[ReplaceService]`/`[FailDependency]` gates on the selected application. [ASP.NET Core](https://prototest.dev/docs/integrations/aspnetcore)
- a substituting test's dedicated server is owned before it starts. [ASP.NET Core](https://prototest.dev/docs/integrations/aspnetcore)
- a method-level `[SignedInAs]` keeps the class-level authenticators. [Authentication](https://prototest.dev/docs/integrations/rest/authentication)

#### REST and HTTP

- a client bound to a configured or published address gets its own handler and cookie jar per test. [Clients](https://prototest.dev/docs/foundation/clients)
- response and attachment defaults bind `ProtoTest:Http:Responses`; attachments stay opt-in. [Attachments](https://prototest.dev/docs/integrations/rest/attachments)
- binary request and response bodies are traced as bytes with their media type; a binary response records no JSON body block, no decoded body in its observation, and no decoded text in a failed status assertion. [Responses](https://prototest.dev/docs/integrations/rest/responses)

#### GraphQL

- a null execution is tolerated like REST, and subscription events are disposed as they advance. [Subscriptions](https://prototest.dev/docs/integrations/graphql/subscriptions)
- a rejected subscription names the server's errors. [Subscriptions](https://prototest.dev/docs/integrations/graphql/subscriptions)
- a fluent upload argument fails when the document is built instead of rendering as a literal. [GraphQL](https://prototest.dev/docs/integrations/graphql)

#### gRPC

- the built-in test user's metadata is redacted by default (`prototest-user`). [gRPC](https://prototest.dev/docs/integrations/grpc/)
- a transport with no base address fails naming `AddClient` and the application's keys. [gRPC](https://prototest.dev/docs/integrations/grpc/)
- client options are per named client; the shared section binds over each callback. [gRPC](https://prototest.dev/docs/integrations/grpc/#options-and-keys)

#### Messaging

- a null payload on a positional MassTransit contract fails naming the contract. [MassTransit](https://prototest.dev/docs/integrations/messaging/masstransit)
- a MassTransit consumer re-baselines when a substituting test replaces the harness. [MassTransit](https://prototest.dev/docs/integrations/messaging/masstransit)
- awaiting two destinations no longer misses the second destination's first delivery. [Messaging](https://prototest.dev/docs/integrations/messaging/)
- awaits serialize and an unmatched delivery is kept for a later await, across brokers. [Messaging](https://prototest.dev/docs/integrations/messaging/)
- `UseRabbitMq` declares the Broker capability only when the address is provided or declared. [Messaging](https://prototest.dev/docs/integrations/messaging/)
- `AddMessaging` declares Broker with `UseBrokerWhenInProcess`, replacing `UseBrokerUnlessConfigured`, and MassTransit's Broker follows its application's chain. [MassTransit](https://prototest.dev/docs/integrations/messaging/masstransit)
- a missing or non-amqp RabbitMQ connection string fails naming the configuration key. [Messaging](https://prototest.dev/docs/integrations/messaging/)
- `UseRabbitMq` runs its options callback once per registration. [Messaging](https://prototest.dev/docs/integrations/messaging/)
- RabbitMQ uses `RabbitMQ.Client` 7.x end to end (async connect, channel, publish, consume). [Messaging](https://prototest.dev/docs/integrations/messaging/)
- a tap with a missing exchange fails only the tests that await it, with the named error. [Messaging](https://prototest.dev/docs/integrations/messaging/)
- publishing to a released broker throws instead of reopening, and a repeated release is a no-op. [Messaging](https://prototest.dev/docs/integrations/messaging/)
- no coverage collector ships; destinations are deliberately not a coverage category. [Coverage](https://prototest.dev/docs/observability/coverage)

#### Devices

- an in-process connect carries the test id, so the application resolves the test's clock. [Devices](https://prototest.dev/docs/integrations/devices)
- a canceled connect is cleared, so the next send starts a fresh connect. [Devices](https://prototest.dev/docs/integrations/devices)
- reusing a client name under a second application fails naming the client and both applications. [Devices](https://prototest.dev/docs/integrations/devices)
- in-process transports are keyed by program and application; a path-only client fails naming the app. [Devices](https://prototest.dev/docs/integrations/devices)
- the in-process transport validates its options and applies `ConnectTimeout`. [Devices](https://prototest.dev/docs/integrations/devices)
- `DeviceSession` connects single-flight and serializes sends. [Devices](https://prototest.dev/docs/integrations/devices)
- device resource and entity ids include the device type. [Devices](https://prototest.dev/docs/integrations/devices)
- teardown disconnects and records `device.disconnect`. [Devices](https://prototest.dev/docs/integrations/devices)
- the in-process transport declares its capability only while the application runs in-process. [Devices](https://prototest.dev/docs/integrations/devices)
- a device capability needs a provided address; `ProtoDeviceClientBuilder.WithAddressKeys` declares the keys, and the MQTT client is inert without its broker. [Devices](https://prototest.dev/docs/integrations/devices)
- an oversized frame fails naming the address and limit (`WebSocketDeviceOptions.MaxMessageBytes`). [Devices](https://prototest.dev/docs/integrations/devices)
- an MQTT broker set through `configure` keeps the device capabilities, so gated tests no longer skip. [Devices](https://prototest.dev/docs/integrations/devices)

#### Web

- a failed backend creation is no longer cached, so a session retries from a clean slate. [Web](https://prototest.dev/docs/integrations/web/)
- only the backend that wins the first-wins registration declares the browser capability. [Web](https://prototest.dev/docs/integrations/web/)
- Selenium `Check`/`SelectOption` verify the resulting state or fail within the action timeout. [Interactions](https://prototest.dev/docs/integrations/web/interactions)
- assertions poll at `IWebBackend.PollInterval`; over-cap traces and stuck pumps are recorded. [Diagnostics](https://prototest.dev/docs/integrations/web/diagnostics)
- framework routes (`/_`, `/.well-known`) are never page-inventoried. [Web](https://prototest.dev/docs/integrations/web/)
- `WebDownload` implements `IProtoBinaryContent`, so a download feeds `ProtoSheets.Open` in one line. [Web](https://prototest.dev/docs/integrations/web/)
- sessions key by name and application, so the same name under two applications stays distinct. [Web](https://prototest.dev/docs/integrations/web)

#### Sheets

- `int`/`long` reads accept only finite, integral, in-range values and fail naming the cell. [Sheets](https://prototest.dev/docs/integrations/sheets/)
- record models construct through their mapped primary constructor. [Sheets](https://prototest.dev/docs/integrations/sheets/)
- a repeated `AddSheets` composes its options callbacks instead of keeping the first. [Sheets](https://prototest.dev/docs/integrations/sheets)

#### SQL and Data

- optional constructor-parameter defaults win over generated values. [Defaults](https://prototest.dev/docs/integrations/data/defaults)
- a run whose declared keys are unprovided keeps the hooks inert and names the required gate. [SQL](https://prototest.dev/docs/integrations/sql/)

#### Testcontainers

- container readiness honors the run's readiness policy. [Infrastructure](https://prototest.dev/docs/foundation/infrastructure)
- a released container can be started again, and its connection string is cleared. [Infrastructure](https://prototest.dev/docs/foundation/infrastructure)

#### Aspire

- an AppHost for a partly configured topology publishes only the missing keys. [Aspire](https://prototest.dev/docs/integrations/aspire)
- a published connection string is recorded redacted, so credentials never reach the trace, while its address stays readable. [Aspire](https://prototest.dev/docs/integrations/aspire)

#### WireMock

- a run-scoped fake keeps its stubs and request log for the whole run; `Reset()` clears it. [WireMock](https://prototest.dev/docs/integrations/wiremock)
- pinning Humanizer 3.0.10 avoids a `NU1608` next to Aspire. [WireMock](https://prototest.dev/docs/integrations/wiremock)
- `WireMockAssertionException` derives from `ProtoAssertionException`. [WireMock](https://prototest.dev/docs/integrations/wiremock)

#### Runners and analyzers

- an NUnit test body that throws is recorded failed with its exception and source location. [NUnit](https://prototest.dev/docs/runners/nunit)
- a body `OperationCanceledException` records `Cancelled` under xUnit v2 as under the other adapters. [Runners](https://prototest.dev/docs/runners/overview)
- TUnit parameterized rows record their arguments in the trace name. [TUnit](https://prototest.dev/docs/runners/tunit)
- the MSTest floor accepts the standard template's version, and every runner page states its framework minimum. [Runners](https://prototest.dev/docs/runners/overview)

#### Agent workflows

- output is written as UTF-8, so the middle-dot separator renders in a default Windows console. [CLI](https://prototest.dev/docs/agent-workflows/cli#summary)
- the MCP host prints its usage on an unknown argument, like the CLI does. [Coding agents](https://prototest.dev/docs/agent-workflows/setup)
- `prototest summary` and the pull request comment print the failing operation once. [Diagnosis](https://prototest.dev/docs/agent-workflows/diagnosis)
- the shared failure selector prefers a failed operation over a deeper cancelled one that recorded an error, in the CLI, the MCP tools and the viewer. [Diagnosis](https://prototest.dev/docs/agent-workflows/diagnosis)

#### Traces and reporting

- an unreadable archive under `TestResults` no longer hides readable runs elsewhere from `prototest index` and MCP discovery. [Setup](https://prototest.dev/docs/agent-workflows/setup)

#### Viewer

- the header keeps the brand, the path and the actions apart on a phone instead of drawing them over each other. [ProtoTrace](https://prototest.dev/docs/observability/prototrace)
- the tab strip is one tablist with a roving focus and arrow keys. [ProtoTrace](https://prototest.dev/docs/observability/prototrace)
- the run strip names each tick with the test it opens, and the inspector resizer takes arrow keys. [ProtoTrace](https://prototest.dev/docs/observability/prototrace)
- every key-value list in the details (an item's state, request and response fields, attributes, metadata, finding and moment facts) uses one property list: rows with a single line across both columns and keys aligned; state and fields sit in a card whose head names the namespace and the count. [ProtoTrace](https://prototest.dev/docs/observability/prototrace)
- a tracked item's details read as properties: the shared namespace once, short keys, values as text with their unit; its trail folds the creating change's values and shows later changes as before and after, one line each; a long path or id no longer pushes the details' head past the panel. [ProtoTrace](https://prototest.dev/docs/observability/prototrace)
- run operations keep their name on one line on a phone instead of breaking it letter by letter in the number column. [ProtoTrace](https://prototest.dev/docs/observability/prototrace)
- the run timeline reads one line per test, with the reason a hover away and the test list's filters named rather than repeated; State rows read two lines, with the change count on the ticks' hint. [ProtoTrace](https://prototest.dev/docs/observability/prototrace)
- the run splits into views instead of stacking every panel: Overview (Needs attention beside what the run could see, or a plain all-clear), Timeline, Operations, Details and Files, each at `#/run/<view>`; a view with nothing in it has no tab, and the walkthrough shows the same shape. [ProtoTrace](https://prototest.dev/docs/observability/prototrace)
- sideways strips (view tabs, the details' section index, workbook sheets) scroll with a plain mouse wheel while they have room, fade only on the sides that have more, and keep the selected entry in view. [ProtoTrace](https://prototest.dev/docs/observability/prototrace)
- fewer alarms at once: the run's Needs attention reads rule and reason on one line, the verdict bar names the rule as text and keeps its place in a tooltip, the test list's reasons are muted beside their red marks, and panel explanations move from a standing line to a styled hint beside the title, opened on hover or keyboard focus. [ProtoTrace](https://prototest.dev/docs/observability/prototrace)
- one Framework switch beside the view tabs shows, dims or hides framework operations in Steps and Timeline, and the machinery a test ran on in State; dim is the default, the choice is remembered, and a failing framework operation always stays. [ProtoTrace](https://prototest.dev/docs/observability/prototrace)
- Steps and Timeline rows name their kind as quiet text instead of a filled chip, passing checks drop their outline so only a failing one stands out, and duration bars lose the track behind them. [ProtoTrace](https://prototest.dev/docs/observability/prototrace)
- tertiary text on hover rows meets WCAG AA in both themes. [ProtoTrace](https://prototest.dev/docs/observability/prototrace)
- the inspector frames source, validated shapes, JSON bodies and code in one card with the same head, opens attached files from a row like the other links, and keeps its path and section index on one line. It leads with what went wrong: a comparison comes before the source, and the exception and check verdicts that repeat it fold to the end with the response and the attributes; the head keeps two lines. [ProtoTrace](https://prototest.dev/docs/observability/prototrace)

#### Docs and samples

- the home, Learn lessons, integration catalog and recipes use a clearer reading hierarchy; figures follow the reader theme, product previews retain blueprint, and code controls share accessible copying. Navigation and the API reference follow the existing design tokens. [Home](https://prototest.dev/)
- the sample and the template write a per-run trace, so a passing rerun cannot overwrite a failed run's evidence. [Learn](https://prototest.dev/learn)
- the ProtoTrace page states what `Enabled = false` disables. [ProtoTrace](https://prototest.dev/docs/observability/prototrace)
- the Devices page no longer lists the unshipped `device.replay`. [Devices](https://prototest.dev/docs/integrations/devices)
- the site self-hosts its typefaces, and the viewer drops three unused Inter files. [Design system](https://prototest.dev/docs/advanced/design-system)
- the coverage page's viewer mock shows the shipped 44-test demo, and the home page's structured data names 1.1.0. [Coverage](https://prototest.dev/docs/observability/coverage)
- the accessibility gate also covers the Learn track, the changelog, search and the 404 route. [Design system](https://prototest.dev/docs/advanced/design-system)
- the Learning demo's drill archives and lesson values are regenerated from the current code, and the sample README's counts match a fresh run. [Learn](https://prototest.dev/learn)
- every value quoted from a lesson archive matches the committed trace; the first-test walk, the readiness waits, the tenant ids and the artifact sizes were reconciled. [Learn](https://prototest.dev/learn)
- the teardown finding reads the same in the reporting, lifecycle and Learn pages, and the first test registers the report sink it promises. [Reporting](https://prototest.dev/docs/observability/reporting)
- the viewer walkthrough carries the shipped demo's records and the viewer's own chrome, so the mocks and the real screens agree. [Coverage](https://prototest.dev/docs/observability/coverage)
- the Learn track adds four lessons: the run's one host, signing in as a test user, findings and the run gate, and swapping a dependency for one test. [Learn](https://prototest.dev/learn)
- the home's command box offers every runner's commands, with a proof strip and a link to the conversion page. [Home](https://prototest.dev/)
- a vocabulary page for the foundation terms and the trace entities. [Vocabulary](https://prototest.dev/docs/foundation/vocabulary)
- a message-to-fix table in troubleshooting, built from the errors readers hit. [Troubleshooting](https://prototest.dev/docs/getting-started/troubleshooting)
- a where-your-evidence-goes map for the archive, the viewer, reports, OpenTelemetry, the CLI and MCP. [Observability](https://prototest.dev/docs/observability/where-evidence-goes)
- a which-runner chooser in the runner overview. [Runners](https://prototest.dev/docs/runners/overview)
- the navbar groups the API reference, trace viewer and changelog under Resources; the footer keeps focused entry points. [Reference](https://prototest.dev/docs/)
- the reference pages lead with a runnable example, the real output and the resolution tables, and the obsolete shims move to the migration page. [Docs](https://prototest.dev/docs/)
- the runner chooser, the observability first screens, the CI digest and the agent quickstart show real output. [Runners](https://prototest.dev/docs/runners/overview)
- the integration pages gain the sequence diagrams, the isolation table and the trace examples. [Integrations](https://prototest.dev/docs/integrations/overview)
- the home leads with the task, the recipes end in trace anatomy, and the project pages lead with their verdict. [Home](https://prototest.dev/)
- the Learn track corrects the published case, the report claim and the decision tables, and names the sample consistently. [Learn](https://prototest.dev/learn)
- the package READMEs state the queue, container and runner semantics the code does, with one container contract. [Runners](https://prototest.dev/docs/runners/overview)

#### Packaging

- every package packs again (44), verified in one `eng/pack.ps1` run. [Installation](https://prototest.dev/docs/getting-started/installation)

### Breaking changes


These are the 1.0 to 1.1 migration changes; from 1.1 onward the 1.x surface stays additive.

- Core: `AddClientFrom` is removed from the REST and GraphQL builders.
  - Register clients under an application and configure its base URL or endpoints, or use an `AddClient` resolver ([Clients](https://prototest.dev/docs/foundation/clients)).
- Core: registration, observation and runner implementation types are internal; use the public APIs.
- Core: `ProtoHost` can no longer be constructed from a service provider; use `ProtoHostBuilder` ([Lifecycle](https://prototest.dev/docs/foundation/lifecycle)).
- Core: `ProtoFlow` steps declare their operation; unused retry and timeout options are gone.
- Core: `IProtoClientInitializer.TryInitializeAsync` no longer takes a cancellation token.
  - Pass one to `StartTestAsync`, or use a run hook as the cancellable extension point ([Lifecycle](https://prototest.dev/docs/foundation/lifecycle)).
- Core: `ProtoExecutionContext.RegisterClient<T>` takes `ProtoClientOwnership` instead of a `bool`.
  - `ProtoClientOwnership.Context` is a client the test owns; `ProtoClientOwnership.Caller` is one the caller owns ([Clients](https://prototest.dev/docs/foundation/clients)).
- Core: `ProtoDocumentSource.LoadText` is one method with optional parameters; the explicit overload is gone ([Extending](https://prototest.dev/docs/advanced/extending)).
- Core: shape mismatches throw the protocol's assertion exception, with `JsonShapeMismatchException` as the inner.
  - Code that caught `JsonShapeMismatchException` catches `ProtoAssertionException` ([Migrating from 1.0](https://prototest.dev/docs/getting-started/migrating-from-1-0)).
- Core: `ProtoTest.OpenTelemetry` is retired.
  - Subscribe to the `ProtoTest` source with `.AddSource("ProtoTest")` ([OpenTelemetry](https://prototest.dev/docs/observability/opentelemetry)).
- HTTP: `ProtoHttpAuthLifecycleHook` is sealed and takes a `ProtoProtocol` instead of a string.
  - `ProtoHttpClientResolution` is a positional record with `SourceName`, `SourceClientName` and `Deconstruct` gone, and `ProtoHttpClientResolver.Resolve` takes the client name as an optional third argument ([Extending](https://prototest.dev/docs/advanced/extending)).
- REST: object request bodies serialize camelCase by default, matching GraphQL variables.
  - Pass explicit `JsonSerializerOptions` to keep another naming policy ([Migrating from 1.0](https://prototest.dev/docs/getting-started/migrating-from-1-0)).
- gRPC: `GrpcAttachmentOptions` no longer derives from the HTTP attachment options, so `SensitiveHeaders` and `SensitiveQueryParameters` are gone.
  - Metadata redaction uses the gRPC client's `SensitiveMetadataKeys` ([gRPC](https://prototest.dev/docs/integrations/grpc/)).
- gRPC: `ProtoGrpcClient.ServerStreaming` and `DuplexStreaming` moved to the blocking facade, `client.Blocking` ([gRPC](https://prototest.dev/docs/integrations/grpc/)).
- Messaging: a publish records the `messaging.published` observation; the operation stays `messaging.publish`.
  - Update a collector that filtered the old observation kind ([Migrating from 1.0](https://prototest.dev/docs/getting-started/migrating-from-1-0)).
- Messaging: `RabbitMqOptions.PollInterval` is removed; awaits are event-driven and `MessagingOptions.DefaultTimeout` bounds them ([Messaging](https://prototest.dev/docs/integrations/messaging/)).
- Web: `ProtoTest:Web:Sessions:{name}` settings no longer configure sessions.
  - Put addresses under `ProtoTest:Applications:{application}` and select with `[WebSession]` or `Web()` ([Web](https://prototest.dev/docs/integrations/web/)).
- Web: the reshape removed `IWebBackend.CurrentAddress`, the old `Web()` overload and Selenium's download surface.
  - The web pages describe the replacements; `CompatibilitySuppressions.xml` records the removals.
- Web: `PlaywrightWebOptions.Context` is removed; configure the browser context with `ConfigureContext` ([Web](https://prototest.dev/docs/integrations/web/)).
- Web: `RequiresPlaywrightBrowserAttribute.Session` is removed; the condition probes the configured browser or channel ([Skip conditions](https://prototest.dev/docs/foundation/skip-conditions)).
- Web: `WebOperationContext.Result` is no longer public ([Web](https://prototest.dev/docs/integrations/web/)).
- ASP.NET Core: the package now depends on `ProtoTest.Web.Pages` instead of the full `ProtoTest.Web`.

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
