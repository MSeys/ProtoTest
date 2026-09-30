---
sidebar_position: 2
title: Your first test
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

# Your first test

This page takes a fresh test project to a passing test. It then breaks the test and reads the trace. The steps assume an ASP.NET Core application, `Orders.Api`, beside the tests; the tip below creates one. It uses **NUnit**. The other runners differ only in the setup class, covered in [Test runners](../runners/overview.md).

:::tip[Rather start from a working solution?]
`dotnet new install ProtoTest.Templates`, then `dotnet new prototest -n Orders` creates an API and a suite for it that is already composed, traced and reported. Steps 1, 2, 3 and 6 below are ready to run, and `--runner` writes the suite for xUnit v2, xUnit v3, TUnit or MSTest instead. See [Installation](./installation.md#start-from-the-template).
:::

## The six steps

1. **Project.** A test project with the runner and integration packages.
2. **Host.** One setup class that builds the host, the sink, and the application.
3. **Test.** One passing test, one trace file under `TestResults/`.
4. **Assert.** A shape on the body, still green.
5. **Break it.** One wrong expectation, one failure message that names the fix.
6. **Trace.** The `.prototrace` that recorded all of it, read layer by layer below.

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

`ProtoTest.NUnit` needs NUnit 4.6.1 or newer; the standard `dotnet new nunit` template pins an older version, so update NUnit first: `dotnet add package NUnit --version 4.6.1`.

For a minimal-API application, make its entry point visible to the tests by adding this to `Orders.Api`:

```csharp
public partial class Program;
```

## 2. Configure the host

One class per test project builds the host. With NUnit it is a `[SetUpFixture]`:

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

This describes one application, `Api`, that exposes REST. It runs your application **in-process**. No deployed environment and no port are needed. Point it at a real address in an environment by setting `ProtoTest:Applications:Api:BaseUrl`. The sink writes `TestResults/report.html` under the test project's output folder, next to the trace in step 6. The template names the report after the project (`Shop.html`); this page uses the fixed name `report.html`.

:::tip
A `[SetUpFixture]` only covers its own namespace and the namespaces below it. Keep your tests in or under `Orders.Tests`.
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

- `[ProtoTest]` replaces NUnit's `[Test]` and wraps the test in a ProtoTest context.
- `[Application("Api")]` selects the `Api` application; `Proto.Context.Rest()` then uses its default REST client.
- `Proto.Context` is available anywhere in the test: no base class, no injected parameter.

Run it with `dotnet test`. One test passes, and a trace file lands under `TestResults/`.

## 4. Assert on the response

A status code says little. Describe the parts of the body the behavior depends on:

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

The shape is **partial**. The matcher ignores properties you do not list and reports all mismatches at once with their JSON paths. See [Shape matching](../foundation/shape-matching.md).

## 5. Make it fail once

Change `status = "pending"` to `"cancelled"` and run the test again. The test fails with the request, the JSON path and both values in the message:

```
POST /api/orders - Shape mismatch failed with 1 error(s):
  • [$.status]: Values did not match. (Expected: "cancelled", Actual: "pending")
```

The message names the fix. Change the expectation back, and the test passes. The [Read the trace](/learn/one-test-one-journey/read-the-trace) lesson walks a real failing trace the same way.

## 6. Read the trace

Tracing is on by default. Without `ConfigureTracing` the trace is written to `TestResults/prototest-{runId}.prototrace` under the test project's output folder. The sink from step 2 writes `TestResults/report.html` beside it.

To choose the trace path yourself, add one line to `Setup`:

```csharp
builder.ConfigureTracing(trace => trace.OutputPath = "TestResults/orders.prototrace");
```

Run the tests, then:

- drop the trace file (`TestResults/orders.prototrace` with the line above, otherwise the default `TestResults/prototest-{runId}.prototrace`) onto [trace.prototest.dev](https://trace.prototest.dev) to see every step of the test, the request and the shape comparison. The file is a binary archive, so open it in the viewer or print it with `prototest summary <file.prototrace>` ([ProtoTrace](../observability/prototrace.md#open-your-own-archive)); reading it as text shows nothing useful.
- open `TestResults/report.html` for the endpoints the suite exercised. See [Reporting](../observability/reporting.md).

One recorded journey reads like this. The walk below is the sample suite's project journey (`ProjectsJourney.CreatingAProjectReturnsIt`), which follows the same six steps against a real application:

<TraceAnatomy
  source={lessonTraces.firstJourney}
  title="One journey, layer by layer"
  test="Northstar.ProtoTest.ProjectsJourney.CreatingAProjectReturnsIt"
  layers={firstJourneyLayers}
  blindSpots={[]}
/>

The `ConfigureTracing` line above is optional. Without it the trace keeps its default name; everything else on this page stays the same.

## Going further: turn setup into a capability

When every order test needs a signed-in customer, write the setup once as an attribute and compose it onto any test. [Attributes](../foundation/attributes.md) shows the worked example: a `Customer` attribute, an authenticator that reads it, and the ordering and teardown rules that make the pair safe.

## Limits

- **One context per async flow.** Starting a second test before completing the active one throws. `Proto.Context` outside a test throws and names the alternatives (`ProtoHost.FindTraceWriter(Activity?)` off-flow, `ProtoHost.CurrentHost` for run scope).
- **A skip starts nothing.** A skipped test never creates a context. See [Skip conditions](../foundation/skip-conditions.md).
- **Names are unique per test.** A name without a prefix is stored as `{testId}-{name}`, and a duplicate attachment name throws.
- **Ids are configurable.** `ConfigureTestIds(ids => ids.RunPrefix = 42)` fixes the run prefix; `SequenceDigits` defaults to `6` and accepts 1 to 9. See [Configuration](./configuration.md).

## Where to next

- [Configuration](./configuration.md): the host options, and how to run the same suite against a deployed environment.
- [Troubleshooting](./troubleshooting.md): when the host, a client or a container does not come up.
- [Foundation](../foundation/overview.md): how the lifecycle, context and attributes fit together.
- [Observability](../observability/coverage.md): what your suite covered, and what it did not.
- [Recipes](../recipes/overview.md): common journeys, ready to adapt.
