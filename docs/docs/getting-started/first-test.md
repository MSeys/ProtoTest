---
sidebar_position: 2
title: Write your first .NET integration test
sidebar_label: Your first test
description: "Build a small ProtoTest suite against an ASP.NET Core API, run it, make it fail once, and read the trace."
---

import TraceAnatomy from '@site/src/components/TraceAnatomy';
import {lessonTraces} from '@site/src/data/traceSources';

export const firstJourneyLayers = [
  {
    id: 'run',
    label: 'Run',
    when: 'before the first test',
    lead: 'What the run composed, and where the application ran.',
    entries: [
      {
        kind: 'capability',
        name: 'REST, GraphQL, ASP.NET Core, SQL, Data, Sheets, Playwright',
        meta: 'the capabilities the composition declared',
      },
      {
        kind: 'application',
        name: 'Northstar web on a loopback listener',
        meta: 'readiness /health, 1 attempt, waited 92 ms',
      },
      {
        kind: 'broker',
        name: 'Messaging broker',
        meta: 'released; no container started, so broker journeys skip',
      },
    ],
  },
  {
    id: 'setup',
    label: 'Setup',
    when: '500.5 ms',
    lead: 'Six hooks and four attributes run before the body: clients, the database connection, and the tenant the test asked for.',
    entries: [
      {kind: 'test.setup', name: 'Setup', meta: '6 hooks, 4 attributes'},
      {
        kind: 'client.initialize',
        name: 'Rest, GraphQL, loopback web, in-process Northstar, probe, messaging',
        meta: 'one operation per client, each with its address recorded',
      },
      {kind: 'sql.connection.open', name: 'Open SqliteConnection', meta: '0.05 ms'},
      {
        kind: 'attribute.before',
        name: 'Application, NorthstarTenant, SignedInAs, NorthstarMember',
        meta: 'the tenant attribute provisions a TenantResponse in 139.9 ms',
      },
    ],
  },
  {
    id: 'execution',
    label: 'Execution',
    when: '178.0 ms',
    lead: 'One request, the application event it caused, and the two checks that decided the test.',
    entries: [
      {kind: 'http.request', name: 'REST POST /api/v1/projects', meta: '158.5 ms, 201 Created'},
      {kind: 'Northstar.Domain', name: 'project.create', meta: 'reported by the application itself'},
      {kind: 'assert.http.status', name: 'Assert status 201 Created', meta: 'expected and actual agree'},
      {kind: 'assert.json.shape', name: 'Assert response shape', meta: '5 properties matched at once'},
    ],
  },
  {
    id: 'teardown',
    label: 'Teardown',
    when: '36.7 ms',
    lead: 'Attributes and hooks reverse, four attachments publish, and owned resources release in order.',
    entries: [
      {kind: 'attachment.publish', name: 'Request, response, expected shape, scenario summary', meta: '4 files into the archive'},
      {kind: 'data.cleanup', name: 'Cleanup TenantResponse', meta: 'the provisioned tenant is removed'},
      {kind: 'resource.release', name: 'Services, connection, consumer, broker, readiness, loopback', meta: 'each released in order'},
    ],
  },
];

# Write your first .NET integration test
This page takes a new test project to a passing test. Then you break the test on purpose and read the trace, the record of what the run did. The steps assume an ASP.NET Core application called `Orders.Api` next to the tests. The tip below creates one for you. The steps use **NUnit**. The other runners differ only in the setup class, which [Test runners](../runners/overview.md) covers.

:::tip[Rather start from a working solution?]
Run `dotnet new install ProtoTest.Templates`, then `dotnet new prototest -n Orders`. This creates an API and a test suite that already has the host set up, tracing on and a report. Steps 1, 2, 3 and 6 below are then done for you. Add `--runner` to get xUnit v2, xUnit v3, TUnit or MSTest instead. See [Installation](./installation.md#start-from-the-template).
:::

## The six steps

1. **Create the project.** A test project with the runner and integration packages.
2. **Configure the host.** One setup class describes your application and where reports go.
3. **Write a test.** One test passes and writes a trace file under `TestResults/`.
4. **Assert on the body.** Check the shape of the response. The test still passes.
5. **Make it fail.** One wrong expectation gives a failure message that names the fix.
6. **Read the trace.** The `.prototrace` file recorded all of it. You read it layer by layer.

## 1. Create the project

```bash
dotnet new nunit -n Orders.Tests
cd Orders.Tests
dotnet add reference ../Orders.Api/Orders.Api.csproj
dotnet add package ProtoTest.NUnit
dotnet add package ProtoTest.Rest
dotnet add package ProtoTest.AspNetCore
dotnet add package ProtoTest.Reporting
```

`ProtoTest.NUnit` needs NUnit 4.6.1 or newer. The standard `dotnet new nunit` template pins an older version, so update NUnit first with `dotnet add package NUnit --version 4.6.1`.

If `Orders.Api` is a minimal-API application, its entry point is hidden from other projects. Add this to `Orders.Api` so the tests can start it:

```csharp
public partial class Program;
```

## 2. Configure the host

Each test project has one **setup class**. It tells ProtoTest how to build the **host**, the one object per test run that owns what the tests share. With NUnit the setup class is a `[SetUpFixture]`:

```csharp
using NUnit.Framework;
using ProtoTest.AspNetCore;
using ProtoTest.Core;
using ProtoTest.NUnit;
using ProtoTest.Reporting;
using ProtoTest.Rest;

namespace Orders.Tests;

[SetUpFixture]
public sealed class Setup : ProtoTestAssembly
{
    protected override void Configure(IProtoHostBuilder builder) =>
        builder
            .AddSink<HtmlReportSink>(sink => sink.OutputPath = "TestResults/report.html")
            .AddApplication("Api", app => app
                .AddAspNetCoreServer<Program>()
                .AddRest(rest => rest
                    .AddClient("Api")
                    .AddCollector<RestCoverageCollector>()));
}
```

This describes one application, `Api`, that offers a REST interface. The host starts your application **in-process**, inside the test process, so you need no deployed environment and no open port. To test a deployed application instead, set `ProtoTest:Applications:Api:BaseUrl` to its address. The sink writes the HTML report to `TestResults/report.html` under the test project's output folder, next to the trace from step 6. The template names its report after the project (`Shop.html`). This page uses the fixed name `report.html`.

:::tip
A `[SetUpFixture]` only covers its own namespace and the namespaces below it. Keep your tests in `Orders.Tests` or in a namespace under it.
:::

## 3. Write a test

```csharp
using System.Net;
using NUnit.Framework;
using ProtoTest.Core;
using ProtoTest.NUnit;
using ProtoTest.Rest;

namespace Orders.Tests;

[Application("Api")]
public sealed class OrderTests
{
    [ProtoTest]
    public async Task CreatingAnOrderReturnsIt()
    {
        using var response = await Proto.Context.Rest()
            .Body(new { product = "notebook", quantity = 2 })
            .PostAsync("/api/orders");

        response.Should.HaveHttpStatus(HttpStatusCode.Created);
    }
}
```

- `[ProtoTest]` replaces NUnit's `[Test]`. It gives the test its own **test context**, which holds the test's clients and state.
- `[Application("Api")]` selects the `Api` application. `Proto.Context.Rest()` then returns a REST **client** for it.
- `Proto.Context` is available anywhere in the test. You need no base class and no injected parameter.

Run it with `dotnet test`. When it works, one test passes and a trace file appears under `TestResults/`.

## 4. Assert on the response

A status code alone says little. Describe the parts of the body that the behavior depends on:

```csharp
using ProtoTest.Json;

response
    .Should.HaveHttpStatus(HttpStatusCode.Created)
    .Should.MatchShape(new
    {
        id = JsonValue.GreaterThan(0),
        product = "notebook",
        quantity = 2,
        status = "pending"
    });
```

The shape is **partial**. The check ignores properties you do not list. When something does not match, it reports every mismatch at once, each with its JSON path. The test still passes with this shape. See [Shape matching](../foundation/shape-matching.md).

## 5. Make it fail once

Change `status = "pending"` to `"cancelled"` and run the test again. The test now fails. The message names the request, the JSON path and both values:

```
POST /api/orders - Shape mismatch failed with 1 error(s):
  • [$.status]: Values did not match. (Expected: "cancelled", Actual: "pending")
```

The message tells you what to fix. Change the expectation back and the test passes again. The [Read the trace](/learn/start/read-the-trace) lesson walks a real failing trace the same way.

## 6. Read the trace

Tracing is on by default. Without `ConfigureTracing`, the run writes the trace to `TestResults/prototest-{runId}.prototrace` under the test project's output folder. The sink from step 2 writes `TestResults/report.html` beside it.

To choose the trace path yourself, add this line to `Setup`:

```csharp
builder.ConfigureTracing(trace => trace.OutputPath = "TestResults/orders.prototrace");
```

Run the tests, then:

- Drop the trace file onto [trace.prototest.dev](https://trace.prototest.dev). That is `TestResults/orders.prototrace` with the line above, otherwise the default `TestResults/prototest-{runId}.prototrace`. The viewer shows every step of the test, the request and the shape comparison.
- Or print a text summary with `prototest summary <file.prototrace>`. The trace is a binary archive, so opening it in a text editor shows nothing useful. See [ProtoTrace](../observability/prototrace.md#open-your-own-archive).
- Open `TestResults/report.html` to see which endpoints the suite exercised. See [Reporting](../observability/reporting.md).

The walk below shows one recorded test. It is the sample suite's project journey (`ProjectsJourney.CreatingAProjectReturnsIt`), which follows the same steps against a real application:

<TraceAnatomy
  source={lessonTraces.firstJourney}
  title="One journey, layer by layer"
  test="Northstar.ProtoTest.ProjectsJourney.CreatingAProjectReturnsIt"
  layers={firstJourneyLayers}
  blindSpots={[]}
/>

The `ConfigureTracing` line above is optional. Without it the trace keeps its default name, and everything else on this page stays the same.

## Going further: turn setup into a capability

Suppose every order test needs a signed-in customer. You can write that setup once as an **attribute** and put it on any test. [Attributes](../foundation/attributes.md) shows the worked example: a `Customer` attribute, an authenticator that reads it, and the ordering and teardown rules that keep the pair safe.

## Limits

- **One context per async flow.** Starting a second test before the active one completes throws.
- **No context outside a test.** Reading `Proto.Context` outside a test throws. The message names the alternatives: `ProtoHost.FindTraceWriter(Activity?)` off the test flow, and `ProtoHost.CurrentHost` for run scope.
- **A skipped test starts nothing.** It never creates a context. See [Skip conditions](../foundation/skip-conditions.md).
- **Names are unique per test.** A name without a prefix is stored as `{testId}-{name}`, and a duplicate attachment name throws.
- **Ids are configurable.** `ConfigureTestIds(ids => ids.RunPrefix = 42)` fixes the run prefix. `SequenceDigits` defaults to `6` and accepts 1 to 9. See [Configuration](./configuration.md).

## Where to next

- [Configuration](./configuration.md): the host options, and how to run the same suite against a deployed environment.
- [Troubleshooting](./troubleshooting.md): when the host, a client or a container does not come up.
- [Foundation](../foundation/overview.md): how the lifecycle, context and attributes fit together.
- [Observability](../observability/coverage.md): what your suite covered, and what it did not.
- [Recipes](../recipes/overview.md): common journeys, ready to adapt.
