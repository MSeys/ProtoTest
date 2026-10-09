---
sidebar_position: 3
title: Execution context
description: "ProtoExecutionContext lives for exactly one test and holds its clients, state, services, resources, attachments and observations."
---

import AnnotatedCode from '@site/src/components/AnnotatedCode';
import FlowStrip from '@site/src/components/FlowStrip';

export const contextCode = `public sealed record MemberContext(string Id, string Email) : IProtoContext;

[ProtoTest]
public async Task MemberSeesOwnInvoices()
{
    Proto.Context.SetContext(new MemberContext("owner", "owner@example.test"));

    using var response = await Proto.Context.Rest().GetAsync("/api/invoices");
    response.Should.HaveHttpStatus(HttpStatusCode.OK);

    Proto.Context.RecordObservation("invoices", "http.response", "GET /api/invoices");
    Proto.Context.AddAttachment("invoices.json", await response.Content.ReadAsStringAsync(), "application/json");
    Proto.Context.AddFinding("Invoices listed without a filter; check the default page size.");
}`;

export const contextCallouts = [
  {
    line: 6,
    title: 'Typed state',
    note: 'An attribute usually sets this state during setup. Helpers on the test flow read it with Resolve<MemberContext>().',
  },
  {
    line: 8,
    title: 'Client',
    note: 'Rest() retrieves the client created during setup. The host must configure a REST integration that can supply it.',
  },
  {
    line: 11,
    title: 'Observation',
    note: 'Records a fact about this test. Collectors that accept this observation can use it in coverage and reports.',
  },
  {
    line: 12,
    title: 'Attachment',
    note: 'Registers the response body as an attachment. Its name includes the test id for the runner and trace archive.',
  },
  {
    line: 13,
    title: 'Finding',
    note: 'Reports a finding without failing this test. Run gates can evaluate findings when the run ends.',
  },
];

# Execution context

Use `Proto.Context` to access clients, share typed state and attach evidence while a test runs. Its `ProtoExecutionContext` belongs to that test and owns its resources until teardown.

<AnnotatedCode
  filename="MemberInvoices.cs"
  code={contextCode}
  callouts={contextCallouts}
  foot={<>State, client, observation, attachment and finding in one test body.</>}
/>

The test checks the HTTP response, records an observation, attaches the response body and adds a finding for reports and run gates.

<details id="what-it-is">
<summary>What the context holds</summary>

```text
Proto.Context
  clients ......... per-test clients the initializers created
  typed state ..... what attributes and hooks published with SetContext
  services ........ the test DI scope
  resources ....... owned handles released in reverse at teardown
  findings ........ worth reporting, not failures
  attachments ..... files handed to the runner and the archive
  observations .... facts collectors turn into coverage and reports
  trace ........... the test own entries
  identity ........ test name, method, id
  cancellation .... the token the runner supplied
```

</details>

## Access the current test context \{#reaching-it}

```csharp
var context = Proto.Context;
```

`Proto.Context` works anywhere on the test's async flow: in the test method, in helpers it awaits, in page objects, in authenticators. Hooks and attributes receive the context as a parameter instead.

Without an active test on the current async flow, `Proto.Context` throws. This can happen when execution-context flow is suppressed or code runs in a static initializer.

The error names alternatives for code outside a test. Telemetry can use `ProtoHost.FindTraceWriter(Activity?)`. Run hooks can use their host reference. Run-level code can use `ProtoHost.CurrentHost` when the host is unambiguous.

:::tip[Parallel tests are isolated]
An `AsyncLocal` gives each parallel test its own current context. Helpers on that async flow can use `Proto.Context` without receiving it as a parameter. Sharing mutable objects between tests still requires care.
:::

### Read the test identity \{#test-identity}

| Member | Meaning |
| --- | --- |
| `TestName` | the name the runner reported |
| `TestMethod` | the `MethodInfo` of the test |
| `Id` | a `ProtoTestId`, see [test ids](./lifecycle.md#test-ids) |
| `TestId` | the id as a zero-padded string |
| `TestNumber` | the id as a `long` |

### Use the test cancellation token \{#cancellation}

`CancellationToken` is the token the caller supplied when the test was started. The runner adapters pass the token their runner exposes:

| Runner | Token the adapter passes | Notes |
| --- | --- | --- |
| NUnit | the test execution context token | cancellable with `[CancelAfter]` |
| xUnit v2 | the token from the runner's `CancellationTokenSource` | |
| xUnit v3 | `TestContext.Current.CancellationToken` | |
| TUnit | `TestContext.Execution.CancellationToken` | |
| MSTest | `CancellationToken.None` | `ExecuteAsync(ITestMethod)` exposes no token; setup runs to the integration's own timeout |

```csharp
using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
var context = await host.StartTestAsync("Checkout", method, cancellation.Token);
var token = context.CancellationToken; // the test token, available to hooks and attributes
```

`ProtoTest.Sql` passes this token when opening a connection and beginning a transaction. The run-scoped [`IProtoRunHook`](./hooks.md#run-hooks) receives its token as a parameter.

### Name test records \{#unique-names}

Use `UniqueName` to name records such as tenants or customers from the test id. The same name and test id produce the same result:

```csharp
var tenant = Proto.Context.UniqueName("tenant");        // "tenant-0042317"
var second = Proto.Context.UniqueName("member", 2);     // "member-0042317-2"
```

```csharp
string UniqueName(string name, int sequence = 0);
```

The name is `{name}-{TestId}`. A positive `sequence` adds `-{sequence}` when one test needs several records of the same kind.

The default id generator combines a random run prefix with an incrementing test number. Names differ between tests in one host, but the random prefix cannot guarantee uniqueness across runs.

Fixing `RunPrefix` through `ConfigureTestIds` repeats the prefix, not the association between a test method and its id. Test start order also determines the id. `ProtoTest.Data` likewise derives generated member defaults from the test id.

### Share typed state \{#typed-state}

Typed state is how attributes, hooks and tests hand information to each other without globals.

<FlowStrip
  steps={[
    {title: 'An attribute', detail: <span>publishes the state with <code>SetContext</code></span>},
    {title: 'The test', detail: <span>reads it with <code>Resolve</code></span>},
    {title: 'Teardown', detail: <span>reads it with <code>TryResolve</code> and returns early when setup stopped short</span>},
  ]}
/>

```csharp
public sealed record SampleUserContext(
    string Id, string Tenant, string Email, string Role, string AccessToken) : IProtoContext;

context.SetContext(new SampleUserContext(user.Id, user.Tenant, user.Email, user.Role, user.AccessToken));

var state = Proto.Context.Resolve<SampleUserContext>();       // throws if missing
var maybe = Proto.Context.TryResolve<SampleUserContext>();    // null if missing
```

```csharp
void SetContext<T>(T context) where T : class, IProtoContext;
T Resolve<T>() where T : class, IProtoContext;
T? TryResolve<T>() where T : class, IProtoContext;
```

- `IProtoContext` is an empty marker interface.
- The unkeyed overloads above store state by the **exact generic type**. Setting that type again replaces its value. Read it back with the same type.
- A missing resolve throws *"No context of type 'X' is registered for this test. Register it before the test body reads it ..."*. Use `TryResolve` in teardown code, where setup may not have got that far.
- `SetContext` records the value as state on the trace's context entity. `TryResolve` never traces. `Resolve` writes a `context.resolve` event only when it fails, so the trace identifies missing lookups.

### Retrieve clients \{#clients}

Integrations register clients on the context. Retrieve them through extension methods such as `Rest()`, `GraphQL()` and `Web()`. Integrations use this underlying API:

```csharp
void RegisterClient<TClient>(TClient client, string name = "Default",
    ProtoClientOwnership ownership = ProtoClientOwnership.Context) where TClient : class;
TClient Client<TClient>(string name = "Default") where TClient : class;
TClient? TryClient<TClient>(string name = "Default") where TClient : class;
```

Clients use their type and **case-insensitive** name as the lookup key. Registering the same type and name twice throws. Registering a client after release has begun throws `ObjectDisposedException`.

By default, the context disposes clients when the test ends, in reverse registration order. With `ProtoClientOwnership.Caller`, the caller owns disposal.

A failing `Client<T>` lookup writes a `client.resolve` event before throwing. `TryClient` never traces. See [Clients](./clients.md) for writing your own.

### Attach files and content \{#attachments}

```csharp
ProtoTestAttachment AddAttachment(string name, string content, string mediaType = "text/plain", string? description = null);
ProtoTestAttachment AddAttachment(string name, ReadOnlyMemory<byte> content, string mediaType = "application/octet-stream", string? description = null);
ProtoTestAttachment AddAttachmentFile(string filePath, string? name = null, string mediaType = "application/octet-stream", string? description = null);
ProtoTestAttachment AddAttachment(ProtoTestAttachment attachment);

IReadOnlyList<ProtoTestAttachment> Attachments { get; }
```

The context prefixes names with `{testId}-` unless they already have that prefix. Duplicate names throw, ignoring case. See [Attachments](./attachments.md).

## Beyond the basics

Services, owned resources, findings, observations and the trace writer live on [Execution context advanced topics](./execution-context-advanced.md).

## Limits

- `RegisterResource` refuses run-scoped resources. `AddResource` on the builder is the run-scoped counterpart.
- `context.DisposeAsync` attempts every release and aggregates failures. A release that already failed is not retried; that failure is still part of the aggregate. An early release that is still running is awaited before the test's service scope is disposed. Calling dispose again waits for that same completion and does not release resources a second time.
- A test skipped before setup creates no context. See [Skip conditions](./skip-conditions.md).
- `Proto.Context` throws when the current async flow has no active test context. See [Concurrency](./concurrency.md) for the isolation rule both pages share.
