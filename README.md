<p align="center">
  <img width="1200" alt="ProtoTest" src="assets/brand/prototest-banner.svg" />
</p>

<p align="center">
  <a href="https://github.com/MSeys/ProtoTest/actions/workflows/ci.yml"><img src="https://github.com/MSeys/ProtoTest/actions/workflows/ci.yml/badge.svg" alt="CI" /></a>
  <a href="https://www.nuget.org/packages/ProtoTest.Core"><img src="https://img.shields.io/nuget/v/ProtoTest.Core" alt="NuGet" /></a>
  <a href="https://www.nuget.org/packages/ProtoTest.Core"><img src="https://img.shields.io/nuget/dt/ProtoTest.Core" alt="Downloads" /></a>
  <a href="https://www.nuget.org/profiles/MSeys"><img src="https://img.shields.io/badge/nuget-all%20packages-blue" alt="All NuGet packages" /></a>
  <a href="LICENSE"><img src="https://img.shields.io/github/license/MSeys/ProtoTest" alt="License" /></a>
</p>

**ProtoTest is a composable integration-testing foundation for .NET 8, 9 and 10.** Its shared runtime
coordinates the machinery around each test: one host per run, one isolated execution context per test,
one lifecycle and one portable trace. A single journey can cross REST, GraphQL, gRPC, messaging, SQL,
browsers, spreadsheets and in-process ASP.NET Core while the scenario — not the plumbing — remains the
thing you read.

## The idea

Infrastructure is not the test. A server should be a capability you compose, not a fixture you fight; a
tenant should be data you own, not a call to a control plane that only exists locally; a database should
come with the run, not with a runbook. ProtoTest composes those pieces onto the host and gives every test
a context where they are already there.

And a green run can still be a fluke — a fallback taken, a retry that hid a failure, a check that never
ran. So every test leaves a portable `.prototrace`: what was wired, what moved, what changed, and what was
actually verified. Anything the code cannot say, the trace does.

## A test, and what it leaves behind

```csharp
[ProtoTest]
public async Task CreatingAProjectReturnsIt()
{
    var created = await Proto.Context.Rest()
        .Post("/api/v1/projects")
        .Body(new { name = "orion" })
        .ExecuteAsync();

    created.Should.HaveHttpStatus(HttpStatusCode.Created)
        .ShouldMatchShape(new { id = JsonValue.Any, name = "orion" });
}
```

```
test.execution                                    succeeded   142 ms
├─ http.request · POST /api/v1/projects           succeeded    38 ms
│  ├─ assert.http.status                           passed    201
│  └─ assert.json.shape                            passed    matched { id, name: "orion" }
├─ state  value:project:orion                      created
└─ resources.release                               succeeded     6 ms
```

That bundle opens in the [viewer](https://trace.prototest.dev/) — one page per test, operations with their
evidence, tracked state with its versions, and the places a suite is thinner than it looks.

## What ships

- **Protocols**: REST, GraphQL, gRPC and messaging clients with the `[Auth<T>]` pipeline, shape
  assertions and destination coverage.
- **State and data**: a per-test SQL connection and EF Core context, deterministic builders and
  provisioners, run-owned PostgreSQL and RabbitMQ containers.
- **Surfaces**: in-process ASP.NET Core with access to the application's own services, and browser sessions
  with page objects, flows, login and page coverage over Playwright or Selenium.
- **Evidence**: spreadsheet assertions for generated `.xlsx` files, OpenAPI contract coverage, JSON and
  HTML reports, an OpenTelemetry bridge, and the trace itself.
- **Runners**: NUnit, xUnit v2, xUnit v3, MSTest and TUnit, sharing one lifecycle and one set of outcomes.

## Start here

```bash
dotnet new install ProtoTest.Templates
dotnet new prototest -n Shop
cd Shop && dotnet test
```

The template leaves `Shop.prototrace` and `Shop.html` in the test output. Everything else — installation,
concepts, a page per package — lives at **[prototest.dev](https://prototest.dev/)**. The twelve-journey demo
over a real SaaS, its Vue console and its browser journeys is in
[`samples/ProtoTest.Demo`](https://github.com/MSeys/ProtoTest/tree/main/samples/ProtoTest.Demo).

Contributions and sharp observations welcome at [MSeys/ProtoTest](https://github.com/MSeys/ProtoTest).
Build with `./eng/test.ps1`, validate packages with `./eng/pack.ps1`. MIT [licensed](LICENSE).
