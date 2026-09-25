# ProtoTest DX and API-consistency review — a register, not a plan

Baseline: `version/1.1` working tree, read against `eng/plan-5.md` (2026-09-25), `eng/audit-plan-4.md`,
`eng/feature-plan.md` and the reference suites (`samples/ProtoTest.Demo`, `samples/Northstar.ProtoTest`,
`C:\Development\OpenCsms\tests\OpenCsms.Suite`). Read-only review: every claim cites source or a sample
call site; nothing here is a fix. P1 blocks a fluent test author, P2 is real friction, P3 is polish.
Items owned by `feature-plan` A1/B2/B3, audit ADDR-1/ADDR-4, the plan-5 routing-key decision or VOC-2 are
marked **already planned** and not re-reported. Counts: P1 = 5, P2 = 7, P3 = 6 (18 new findings).

## Summary

| ID | Area | Pri | Issue | Where it lands |
| --- | --- | --- | --- | --- |
| DX-01 | Assertions | P1 | Facade (`Should.HaveHttpStatus`) vs extensions (`ShouldMatchShape`, `ShouldHaveStatus`) and `void` returns split one assertion model into three idioms | new plan item |
| DX-02 | Assertions | P1 | `ShouldMatchShape` failures never name the response/route/message they were made against | new plan item (or B2) |
| DX-03 | Messaging | P1 | A tap must be pre-bound via `ProtoTest:Messaging:Destinations:n` or early messages are missed; no code API for it | new plan item |
| DX-04 | Messaging | P1 | `ProtoMessage` drops the routing key; `(exchange, routingKey)` publish/await is impossible | plan-5 decision recorded; promote when a second consumer needs it |
| DX-05 | Read APIs | P1 | `ReadAsJson<T>()`/`ReadDataAs<T>()` are `T?`, so tests write `!` everywhere; no required-value read | A1 covers the path read; required read is new |
| DX-06 | Context | P2 | No first-class unique-name helper; `$"...-{context.TestId}"` is the idiom by example | new plan item |
| DX-07 | Context | P2 | `SqlSession()` vs `Rest()`, `Data()` returns an interface, `Messaging()` takes no name while its peers do | new plan item |
| DX-08 | Capabilities | P2 | Skip attributes are stringly; `[RequiresDevice<T>]` has no worker/server/application parity | new plan item |
| DX-09 | Diagnostics | P2 | Core errors ("No active ProtoExecutionContext…", "No context of type X registered") do not name the fix | new plan item |
| DX-10 | Messaging | P2 | `ProtoMessage.Payload` is `string?` with no typed read; every suite re-declares `JsonSerializerOptions` | new plan item |
| DX-11 | Sheets | P2 | `Verify()`, `ShouldAll(...)` and `void` `Should` members break the `Should.*` naming and chaining | new plan item (extends B3 intent) |
| DX-12 | Assertions | P2 | `ShouldNot` exposes only status; GraphQL errors use `ShouldHaveErrors()` while status/shape use `Should.*` | extends B3 |
| DX-13 | Options | P3 | Section-name outliers (`ProtoTest:Grpc` vs `ProtoTest:Grpc:Attachments`) and `RabbitMqOptions` has no `Validate()` | A2 for RabbitMQ; naming rule is new |
| DX-14 | Style | P3 | Samples mix `Assert.Multiple` and `Assert.EnterMultipleScope` in one project | new plan item (samples/template) |
| DX-15 | Style | P3 | Three test-method naming conventions across samples and the template | new plan item (template/docs) |
| DX-16 | Style | P3 | The `dotnet new` template teaches `using`s outside the namespace; in-repo samples/tests put them inside | new plan item (template) |
| DX-17 | Vocabulary | P3 | `*.contract.shape` observation kinds are literals at four producers; VOC-2 names only web kinds and `graphql.failure` | Done in A5 (constants per protocol) |
| DX-18 | Capabilities | P3 | The same `Reason = "..."` repeats on every gated test; no suite-level reason source | new plan item |

**Already planned — referenced, not re-reported:** A1 path-based single-value read (feature-plan:47);
B2 expected shape in-call (feature-plan:87); B3 `Should` vocabulary parity (feature-plan:90);
ADDR-1 `UseRabbitMq` declaring `Broker` unconditionally with a localhost default
(`src/ProtoTest.Messaging/ProtoHostBuilderExtensions.cs:128`, `RabbitMqOptions.cs:16`; audit A2);
ADDR-4 gRPC missing-address message printing literal `{app}` (`ProtoGrpcClientInitializer.cs:85-88`);
routing-key addition (plan-5.md:233-235); VOC-2 literal kinds (audit A5).

## Findings

### DX-01 — One assertion surface, not three (P1)
**Evidence.** Facade: `RestResponse.Should`/`.ShouldNot` (`src/ProtoTest.Rest/Responses/RestResponse.cs:30,36`), `GraphQLResponse.Should`/`.ShouldNot` (`src/ProtoTest.GraphQL/Responses/GraphQLResponse.cs:53,59`); call `paid.Should.HaveHttpStatus(HttpStatusCode.OK);` (`samples/ProtoTest.Demo/MessagingJourney.cs:42`). Extensions with `ShouldX` names and mixed returns: `ProtoGrpcAssertions.ShouldMatchShape` returns `void`, `ShouldHaveStatus`/`ShouldNotHaveStatus` are extensions (`src/ProtoTest.Grpc/ProtoGrpcAssertions.cs:26,52,67`; call `GrpcJourney.cs:59`); `ProtoMessagingAssertions.ShouldMatchShape` returns `ProtoMessage` (`src/ProtoTest.Messaging/ProtoMessagingAssertions.cs:20`; call `ChargingSessionsBecomeInvoices.cs:61`); `RestResponse.ShouldMatchShape` returns `RestResponse` (`RestResponse.cs:136`); `SheetModelAssertions.ShouldMatchShape<TRow>` returns `void` (`src/ProtoTest.Sheets/SheetModelAssertions.cs:15`). Void assertions: `ProtoCellAssertions.Be` (`ProtoCellAssertions.cs:35`), `ProtoTableAssertions.ContainRow` (`ProtoTableAssertions.cs:30`), `ProtoRangeAssertions.HaveDimensions` (`ProtoRangeAssertions.cs:30`); `WebAssertions` returns `ValueTask` (`src/ProtoTest.Web/Model/WebAssertions.cs:22-43`).
**Problem.** The same act has three shapes and two return conventions; only REST/GraphQL chain status→shape, Sheets cannot chain at all, and `ShouldHaveStatus` reads as a fourth verb family.
**Direction.** One idiom: every assertable subject exposes `Should`/`ShouldNot`, each member returns the subject (`response.Should.HaveHttpStatus(Ok).ShouldMatchShape(shape)`), and `ShouldX` extensions exist only where C# forbids a facade (gRPC `IMessage`/`RpcException`, generic model rows). Add facade members to Sheets/Web/GraphQL errors; delegate the gRPC extension to a small facade (`ProtoGrpcAssertions.For(exception)`), keeping names as shims.
**Compatibility.** Additive (new facade members) with source-compatible `void` → subject returns; gRPC rename deprecation-first. **Lands.** New plan item.

### DX-02 — Shape failures do not name their subject (P1)
**Evidence.** `JsonShapeMismatchException` builds `"Shape mismatch failed with {n} error(s):"` with no route/operation/destination (`src/ProtoTest.Json/JsonShapeMatcher.cs:30-32`); `ProtoShapeAssertion` records the identifier as a trace attribute but rethrows the raw exception (`src/ProtoTest.Json/ProtoShapeAssertion.cs:99-116`). Only messaging prefixes the destination, and only for a non-JSON payload (`ProtoMessagingAssertions.cs:48-51`). A test with two responses in one body (`AccessJourney.cs:35-38`) cannot tell which failed from the NUnit message; the route is only in the trace.
**Problem.** The failure message is what the author reads first and it omits the subject the framework already knows (`request.identifier`, `messaging.destination`, `graphql.operation`).
**Direction.** Wrap at the producer: `ShouldMatchShape` catches the mismatch and throws a protocol exception whose message starts with the identifier (`"POST /api/v1/projects — shape mismatch failed with 1 error(s)…"`), as messaging already does for parse failures. Trace attributes unchanged.
**Compatibility.** Behavioral (message only). **Lands.** New plan item; can fold into B2's REST in-call work.

### DX-03 — A messaging tap is a setup-time secret (P1)
**Evidence.** `MessagingOptions.Destinations` documents that "an adapter declares the test's per-test taps from it before the system under test publishes" (`src/ProtoTest.Messaging/Options/MessagingOptions.cs:15-19`); the reference suite binds it as raw configuration `["ProtoTest:Messaging:Destinations:0"] = CsmsEvents.Exchange` (`C:\Development\OpenCsms\tests\OpenCsms.Suite\Setup.cs:37`). The RabbitMQ consumer declares the tap on first await/setup (`RabbitMqProtoMessageConsumer.cs:177-229`); the handoff records that without the pre-bind the tap misses early messages and a missing exchange fails at setup (`assets/internal/handoff-phase2.md:55-57`).
**Problem.** The test's intent ("await invoice.issued") lives in the test, but the declaration that makes it reliable lives in `Setup` as an indexed config key; the failure mode is a lost message (timing) or a setup failure, not a named error at `AwaitAsync`.
**Direction.** A code API: `AddMessaging(m => m.CaptureAttachments().UseRabbitMq().Tap("invoice.issued"))` (or `Destinations("invoice.issued", …)`), composing with configuration; optionally auto-declare from `[AwaitsMessage("…")]`. Keep the config key as the environment override.
**Compatibility.** Additive. **Lands.** New plan item (handoff carries it as an open gap).

### DX-04 — `ProtoMessage` cannot express `(exchange, routingKey)` (P1)
**Evidence.** `ProtoMessage(string Destination, string? Payload, …)` (`src/ProtoTest.Messaging/IProtoMessageBroker.cs:7-11`); the consumer sets `Destination` to the exchange and drops `delivery.RoutingKey` (`RabbitMqProtoMessageConsumer.cs:246-251`); publish uses `routingKey: message.Destination` (`RabbitMqMessageBroker.cs:64`). The reference suite added a raw `RabbitMQ.Client` helper to publish the poison and read the DLQ (`OpenCsms.Suite/Billing/DeadLetterTests.cs:46-53`, `Support/RabbitMqRawClient.cs`); plan-5 records the framework addition as a future gap (`eng/plan-5.md:233-235`).
**Problem.** Routing keys are the addressing unit of direct/topic exchanges; without them a tap cannot filter and a test cannot publish to a routing key — the suite works around the framework with a second client.
**Direction.** Add nullable `RoutingKey` to `ProtoMessage` and overloads `PublishAsync(exchange, routingKey, payload, …)`, `AwaitAsync((exchange, routingKey), predicate, …)`; `Destination` stays the exchange, and the tap's `#` binding makes routing-key matching a predicate concern.
**Compatibility.** Additive. **Lands.** Recorded in plan-5 Decisions; promote to a plan item when a second consumer needs it.

### DX-05 — Nullable reads force `!` into every test (P1)
**Evidence.** `public T? ReadAsJson<T>(…)` (`RestResponse.cs:51`), `T? ReadAsAnonymous<T>` (`:85`), `dynamic? ReadAsDynamic` (`:90`), `T? ReadDataAs<T>` (`GraphQLResponse.cs:186`). Test code: `var readOnly = createdToken.ReadAsJson<ApiTokenSecretResponse>()!.Secret;` (`AccessJourney.cs:122`; also `DemoSupport.cs:30,47`, `DeliveryJourney.cs:99,125`, `CrossLayerJourneys.cs:48,91`, `BillingJourney.cs:223`, `PlatformJourney.cs:139`, `OpenCsms.Suite/Journeys/ChargingSessionsBecomeInvoices.cs:73,97`, template `Starter.Tests/OrderTests.cs:38`).
**Problem.** The common case (2xx with an expected body) pays a null-forgiving operator that also silences a genuinely missing body instead of naming it. A1 adds a path read but not a required read.
**Direction.** Keep `ReadAsJson<T>` nullable; add `ReadRequired<T>()`/`RequireJson<T>()` that throws a protocol exception naming the identifier when the body is empty or null, and `ReadRequired<T>(path)` composing with A1.
**Compatibility.** Additive. **Lands.** A1 for the path read; required read is a new item.

### DX-06 — No first-class unique name (P2)
**Evidence.** String interpolation against `TestId` is the pattern: `$"northstar-{context.TestId}"` (`samples/Northstar.ProtoTest/NorthstarAttributes.cs:28`), `$"{Role}.{context.TestId}@example.test"` (`:68`), `$"member-{context.TestId}-{context.ObjectSequence:D4}@example.test"` (`NorthstarData.cs:17`), `$"scenario-{context.TestId}-{Guid.NewGuid():N}"` (`NorthstarScenario.cs:62`), `$"northstar-intruder-{Proto.Context.TestId}"` (`AccessJourney.cs:143`), `$"op-{context.TestId}"` / `$"tariff-…"` / `$"station-…"` (`OpenCsms.Suite/Support/CsmsOperatorAttribute.cs:22,28,43`). `ProtoData` already ships deterministic naming for defaults (`ProtoDataValueContext.NextString()`/`NextGuid()`, `src/ProtoTest.Data/Configuration/ProtoDataValueContext.cs:39-48`).
**Problem.** Every suite invents its own convention; the reference suite needed one because the database outlives the process, and the demo copies it. There is no single answer to "make a name that survives a persistent environment".
**Direction.** `context.UniqueName("tenant")` → `tenant-{TestId}` and `context.UniqueName("member", sequence: 2)` for a second object; deterministic, documented as persistent-store-safe, sharing the Data implementation.
**Compatibility.** Additive. **Lands.** New plan item.

### DX-07 — Accessor asymmetry (P2)
**Evidence.** `Rest(clientName)`, `GraphQL(clientName)`, `Grpc(clientName)`, `Devices(clientName)`, `Web(sessionName, …)` (`src/ProtoTest.Rest/ProtoExecutionContextExtensions.cs:13`, GraphQL:15, Grpc:12, Devices:15, Web:15); `Messaging()` takes no name (`src/ProtoTest.Messaging/ProtoExecutionContextExtensions.cs:11`); `SqlSession()`/`SqlConnection()`/`SqlTransaction()` (`src/ProtoTest.Sql/ProtoExecutionContextExtensions.cs:9,16,20`); `Data()` returns `IProtoData` (`src/ProtoTest.Data/ProtoExecutionContextExtensions.cs:8`); `Sheets()` returns `ProtoSheets` (`Sheets:8`). Calls: `Proto.Context.SqlConnection()` (`DomainAccessJourney.cs:34`), `Proto.Context.Messaging()` (`MessagingJourney.cs:45`), `Proto.Context.Data()` (everywhere).
**Problem.** A reader learns `context.<Protocol>()` then meets `SqlSession()`; `Data()` returning an interface is defensible but undocumented as a rule, so it reads as an accident.
**Direction.** Keep domain-led names but close the gaps: `context.Sql()` primary (`SqlSession()` stays an alias), `Messaging(name = null)` for a future second broker, and one line in `recipes.md` stating when an accessor returns the capability interface.
**Compatibility.** Additive. **Lands.** New plan item.

### DX-08 — Skip attributes are stringly; `[RequiresDevice<T>]` has no peers (P2)
**Evidence.** `[RequiresCapability(kind, CapabilityName = "…")]` (`src/ProtoTest.Core/Applications/RequiresCapabilityAttribute.cs:47`) and the typed `RequiresDeviceAttribute<TDevice>` (`src/ProtoTest.Devices/RequiresDeviceAttribute.cs:13-20`). Real usage names programs and servers by string: `ChargingSessionsBecomeInvoices.cs:21-26` (Broker + Worker), `MessagingJourney.cs:63-66` (`CapabilityName = "Northstar standalone"`), `WebJourney.cs:29-32`.
**Problem.** Typing the device capability is the best part of the skip model, but the two most common gates (worker program, named server/application) are strings that fail silently: a typo yields a plausible skip, not an error.
**Direction.** `[RequiresWorker<TProgram>]`, `[RequiresServer(name)]`, `[RequiresApplication(name)]` on the same base, each setting `CapabilityName` from the type/name with a default reason naming the `Add*` call; keep `[RequiresCapability]` for open kinds.
**Compatibility.** Additive. **Lands.** New plan item.

### DX-09 — Core errors do not name the fix (P2)
**Evidence.** `Proto.Context` off-flow throws `"No active ProtoExecutionContext available on this thread."` (`src/ProtoTest.Core/Internal/ProtoTestLifecycle.cs:33`), while its XML documents the alternative (`ProtoHost.FindTraceWriter` for off-flow telemetry, `src/ProtoTest.Core/Proto.cs:16-22`). Missing state: `"No context of type 'X' registered."` (`Internal/ProtoContextStateStore.cs:21-22`). No host: `"No active ProtoHost is available."` (`Internal/ProtoHostRegistry.cs:38`). Integration accessors are the counter-example — they name the call to add (`Devices` extension:19,25; `Sheets` extension:13; `Web` extension:31,53-56; `ProtoHttpClientResolver.cs:84-87`).
**Problem.** The framework's own rule (a reason "must name what is missing and how to provide it") is not applied to ambient-context failures, the first errors a new author hits.
**Direction.** Rewrite the three messages to carry the fix: `Proto.Context` must run inside a test body and points at `ProtoHost.FindTraceWriter`/`CurrentHost`; context state names the `SetContext`/attribute; no-host names `ProtoTestAssembly`/runner setup.
**Compatibility.** Behavioral (message only). **Lands.** New plan item.

### DX-10 — No typed read on messages; nullable payload (P2)
**Evidence.** `ProtoMessage.Payload` is `string?` (`src/ProtoTest.Messaging/IProtoMessageBroker.cs:9`). Tests deserialize by hand with their own options: `private static readonly JsonSerializerOptions InvoiceJson = new(JsonSerializerDefaults.Web);` (`OpenCsms.Suite/Journeys/ChargingSessionsBecomeInvoices.cs:31`), `JsonSerializer.Deserialize<InvoiceIssued>(message.Payload!, InvoiceJson)!` (`:68`), same in the DLQ test (`Billing/DeadLetterTests.cs:29,54`). REST/GraphQL both expose a typed read (`RestResponse.ReadAsJson`, `GraphQLResponse.ReadDataAs`).
**Problem.** The messaging surface stops at the string, so every consumer repeats options and `!`, and the JSON defaults are not the framework's.
**Direction.** `message.ReadAsJson<T>(options = null)` reusing `ProtoJsonDefaults.Reader`, plus `ReadRequired<T>()`; keep `Payload` for raw inspection.
**Compatibility.** Additive. **Lands.** New plan item (pairs with DX-04).

### DX-11 — Sheets breaks the assertion naming and cannot chain (P2)
**Evidence.** `ProtoSheetModel.Verify()` (`src/ProtoTest.Sheets/ProtoSheetModel.cs:77`) and `ProtoModelColumn.ShouldAll(Func<…>)` (`ProtoModelColumn.cs:40`); the `Should` members return `void` (`ProtoCellAssertions.cs:35-52`, `ProtoTableAssertions.cs:30`, `ProtoRangeAssertions.cs:30,52`). Calls: `report.Verify(); report.Column(row => row.Environments).ShouldAll(count => count >= 0);` (`SheetsJourney.cs:46-47`). The untyped `ProtoTableRow` has no shape assertion at all (`ProtoTableRow.cs`).
**Problem.** Sheets is the only integration whose assertions do not read as `Should.*` and cannot chain; `Verify()` reads as a different act although it is a shape/model assertion.
**Direction.** `report.Should.MatchModel()` (obsolete `Verify()`), `column.Should.All(predicate)`, members returning the subject (`cell.Should.Be(1)`), and a `row.ShouldMatchShape(shape)` extension for `ProtoTableRow` mirroring the model-row extension (`SheetModelAssertions.cs:15`). Reuse `SheetAssertion.Run`.
**Compatibility.** Additive + deprecation. **Lands.** New plan item; extends B3's intent.

### DX-12 — `ShouldNot` is status-only; GraphQL errors use a third verb family (P2)
**Evidence.** `ProtoHttpAssertions` exposes only `HaveHttpStatus` (`src/ProtoTest.Http/Responses/ProtoHttpAssertions.cs:27`); `RestResponse` documents that shape has no negated form (`RestResponse.cs:33-35`). GraphQL errors are `response.ShouldHaveNoErrors()`/`ShouldHaveErrors()`/`ShouldHaveError(code)` (`GraphQLResponse.cs:114,125,135`; calls `PlatformJourney.cs:40,126`) while status and shape on the same object use `Should.HaveHttpStatus`/`ShouldMatchShape`.
**Problem.** On one response type three assertion spellings coexist; a reader cannot predict whether a new assertion will be `Should.X`, `ShouldX` or a response method.
**Direction.** Move errors onto the facade: `Should.HaveNoErrors()`, `Should.HaveErrors()`, `Should.HaveError(code)` returning the response, with the old names kept `[Obsolete]`. Document shape's deliberate lack of a negated form in the facade XML.
**Compatibility.** Additive + deprecation. **Lands.** Extends B3 (naming part is new).

### DX-13 — Options section naming and missing validation (P3)
**Evidence.** `ProtoTest:Rest:Responses`/`:Attachments` (`ProtoRestBuilder.cs:13,16`) and `ProtoTest:GraphQL:Responses`/`:Attachments` (`ProtoGraphQLBuilder.cs:13,16`), but `ProtoTest:Grpc` for client options (`GrpcClientOptions.cs:13`) and `ProtoTest:Grpc:Attachments` declared as `ConfigurationSection`, not `ConfigurationSectionName` (`GrpcAttachmentOptions.cs:14`); `ProtoTest:Sql` (`SqlOptions.cs:8`) and `ProtoTest:Sheets` (`SheetsOptions.cs:8`) have no area segment. `RabbitMqOptions` defaults to `amqp://guest:guest@localhost:5672/` with no `Validate()` (`RabbitMqOptions.cs:16-17`), while `MessagingOptions`/`GrpcClientOptions`/`WebSocketDeviceOptions` validate.
**Problem.** The convention is guessable but unstated, so a suite cannot tell whether `ProtoTest:Grpc:Responses` should exist; a malformed RabbitMQ address fails at connect time, not at options resolution.
**Direction.** State one rule (`ProtoTest:<Integration>[:<Area>]`, area ∈ {Responses, Attachments, Client, WebSocket, RabbitMq, …}); rename gRPC client to `ProtoTest:Grpc:Client` with the old key as fallback; add `RabbitMqOptions.Validate()` (absolute amqp/amqps URI) and align the const name.
**Compatibility.** Additive/behavioral with a configuration alias. **Lands.** A2 covers RabbitMQ validation with ADDR-1; the naming rule is a new item.

### DX-14 — `Assert.Multiple` vs `Assert.EnterMultipleScope` (P3)
**Evidence.** The same demo project uses both: `Assert.EnterMultipleScope()` (`WebJourney.cs:133`, `BillingJourney.cs:106`, `PlatformJourney.cs:150`, `DomainAccessJourney.cs:47`) and `Assert.Multiple(() => …)` (`MessagingJourney.cs:53`, `GrpcJourney.cs:57`, `SheetsJourney.cs:49`, `DatabaseProviderTests.cs:32`). Repo-wide 129 files use `Assert.Multiple`, 25 use `Assert.EnterMultipleScope`; OpenCSMS uses `Assert.Multiple` in all three test files.
**Problem.** The sample users copy mixes the pre-scoped and current NUnit idioms with no rule.
**Direction.** Use `Assert.EnterMultipleScope` in samples, the template and new tests; leave existing tests alone until touched; note the rule in `recipes.md` or the test style guide.
**Compatibility.** No production change. **Lands.** New plan item (samples/templates).

### DX-15 — Three test-method naming conventions (P3)
**Evidence.** Sentence case: `TheConsoleFromSignInToTheMonthlyReport` (`WebJourney.cs:40`), `AChargingSessionBecomesAnInvoice` (`OpenCsms.Suite/Journeys/ChargingSessionsBecomeInvoices.cs:34`). `Subject_ShouldVerb`: `IsPostgresStore_ShouldDeriveTheProviderFromConfiguration` (`DatabaseProviderTests.cs:21`). snake_case: `Creating_an_order_returns_it` (template `Starter.Tests/OrderTests.cs:14`); mixed inside one class: `TheMonthlyReport_ShouldMatchItsModel` (`SheetsJourney.cs:32`). `[Category]` appears only on framework characterization/benchmark suites (`tests/ProtoTest.Core.Tests/ClockLocatorTests.cs:9` etc., 20 files), not samples.
**Problem.** The template — the artifact a new suite starts from — teaches a third convention.
**Direction.** One convention for samples/templates: PascalCase sentence (`CreatingAnOrderReturnsIt`), matching the demo journeys and the reference suite; keep `Subject_ShouldVerb` only for pure unit fixtures and say so in the template README.
**Compatibility.** No production change. **Lands.** New plan item (template/docs).

### DX-16 — Template teaches `using`s outside the namespace (P3)
**Evidence.** Template files put `using` before the file-scoped namespace (`src/ProtoTest.Templates/templates/prototest-starter/Starter.Tests/OrderTests.cs:1-8`, `Setup.cs:1-8`). Every in-repo sample and test puts them inside (samples 54/54, OpenCSMS 7/7, tests 205/207 — the two exceptions are `tests/ProtoTest.TUnit.Tests/Setup.cs` and `tests/ProtoTest.Xunit3.Tests/Setup.cs`, where assembly attributes must precede the namespace).
**Problem.** The generated starter is the canonical copy-paste source and contradicts the codebase style.
**Direction.** Move the template's `using` directives inside the namespace (or document the exception if deliberate). The TUnit/Xunit3 files stay as-is for the attribute reason.
**Compatibility.** No production change. **Lands.** New plan item (template).

### DX-17 — `*.contract.shape` kinds are literals at four producers (P3)
**Evidence.** `"http.contract.shape"` (`RestResponse.cs:153`), `"graphql.contract.shape"` (`GraphQLResponse.cs:179`), `"grpc.contract.shape"` (`ProtoGrpcAssertions.cs:46`), `"messaging.contract.shape"` (`ProtoMessagingAssertions.cs:42`). VOC-2 names only `web.page.verified`/`web.page.visited` and `graphql.failure` (audit-plan-4.md:125).
**Problem.** The producer/consumer drift class VOC-2 exists to end still applies to the shape vocabulary; the four literals are copied from each other.
**Direction.** Extend VOC-2's sweep: one internal constants type per protocol (or a shared `ProtoObservationKinds.ContractShape`) referenced by producers and any collector.
**Compatibility.** Internal (wire strings unchanged). **Lands.** A5/VOC-2 extension.

### DX-18 — Capability reasons are repeated per test (P3)
**Evidence.** The same reason string appears on every gated class: `MessagingJourney.cs:26-28`, `WebJourney.cs:29-32`, `CrossLayerJourneys.cs:25-28,67-70`, `DiagnosticsShowcase.cs:138-141`, `OpenCsms.Suite/Journeys/ChargingSessionsBecomeInvoices.cs:21-26`, `Billing/DeadLetterTests.cs:18-23`.
**Problem.** A suite with ten broker-gated tests repeats one sentence ten times; a changed environment key means ten edits and drift between them.
**Direction.** A suite-level reason source: `builder.AddCapabilityReason(ProtoCapabilityKinds.Broker, "Set ProtoTest:Messaging:RabbitMq:ConnectionString.")`; `[RequiresCapability(kind)]` without `Reason` reads it before the default, keeping the per-test override.
**Compatibility.** Additive. **Lands.** New plan item.

## Deliberate idioms — do not change

- **gRPC's `ShouldHaveStatus`/`ShouldNotHaveStatus` extension methods.** Documented deviation: C# has no extension properties, so `Should`/`ShouldNot` cannot attach to `RpcException` (`src/ProtoTest.Grpc/ProtoGrpcAssertions.cs:61-66`). DX-01 keeps the implementation and only adds a facade for new code.
- **`ShouldNot` has no negated shape form.** A negated shape match has no meaning; recorded on the response (`RestResponse.cs:33-35`). DX-12 documents it rather than inventing one.
- **`WebFlow`'s synchronous `Click`/`Fill`/`Check`/`Select`/`Press`.** They build a plan; `RunAsync` executes it (`src/ProtoTest.Web/Model/WebFlow.cs:17-43`). The `Async` suffix marks execution, which is why `WebElement.ClickAsync` and `WebAssertions.HaveTextAsync` carry it; DX-01's rule is for assertion facades, not plan builders.
- **Host-only vs application `Add*` overloads.** `AddRest`/`AddGraphQL`/`AddGrpc`/`AddDevices`/`AddWeb`/`AddAspNetCoreServer` have application overloads because their address is application-relative; `AddMessaging`/`AddSql`/`AddSheets`/`AddData`/`AddWorkerHost` are run-scoped (one broker, one connection strategy, one document reader per run). `recipes.md:22-23` already says "where applications are meaningful" — this is the domain, not drift.
- **`Data()` returning `IProtoData`.** Provisioners are the extension point; the interface is the capability. DX-07 asks only that the rule be written down.
- **Application-targeted configuration under `ProtoTest:Applications:{app}`** (BaseUrl, Endpoints, Grpc:Address, OpenApi:Specification) while run-level options live under `ProtoTest:<Integration>`. This is the address-authority model (`eng/facts/architecture.md` address table), not a section-naming inconsistency.
- **`Assert.Multiple` in existing tests.** Characterization brakes and older tests are not churned for style; DX-14 applies to samples, the template and new tests only.
