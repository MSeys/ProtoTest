---
sidebar_position: 5
title: Troubleshooting
description: The first problems a new suite runs into — the host not starting, a client that cannot be resolved, containers, browsers, parallel tests — and what fixes them.
---

# Troubleshooting

The problems below are the ones a new suite meets first. Each starts with the message you see.

## The host is not there

> **ProtoHost is not initialized.**
> **No active ProtoHost is available.**

The runner never ran your setup class, so no host was built — the second sentence of the message is the adapter's own hint (`Ensure your setup class inherits from ProtoTestAssembly.`, `Register your fixture with [assembly: AssemblyFixture(...)]`, …). Check the one that applies to your runner:

- **NUnit** — the `[SetUpFixture]` only covers its own namespace and the namespaces below it. A test in `Orders.Tests.Api` is covered by a setup in `Orders.Tests`, not by one in `Orders.Tests.Web`. Move the setup up, or out of any namespace to cover the whole assembly.
- **xUnit v3** — `[assembly: AssemblyFixture(typeof(Setup))]` is missing.
- **xUnit v2** — the test class is not in the ProtoTest collection: add `[Collection(ProtoTestCollection.Name)]`.
- **MSTest, TUnit** — the assembly hooks do not call `InitializeAsync`, or the class holding them is not discovered (MSTest needs `[TestClass]` on it).

Each runner's page under [Test runners](../runners/overview.md) shows the complete setup.

## There is no test context

> **No active ProtoExecutionContext available on this thread.**

`Proto.Context` was read outside a ProtoTest test. Usually the test uses the runner's own attribute — `[Test]`, `[Fact]`, `[TestMethod]` — instead of the ProtoTest attribute that opens the context (`[ProtoTest]`, or `[ProtoTestFact]` / `[ProtoTestTheory]` for xUnit). It also happens in code that runs outside the test's async flow, such as a static initializer or a thread started by hand; pass the `ProtoExecutionContext` along instead.

## A client cannot be resolved

> **No application is selected for this test. Apply [Application(name)] or pass a Rest client name to the accessor.**

`Proto.Context.Rest()` without a name uses the selected application's client. Put `[Application("Api")]` on the class or the test, or ask for a client by name: `Proto.Context.Rest("Api")`.

> **Application 'Api' has no Rest client registered. Register one in AddApplication or pass a client name to the accessor.**

The application is composed without that protocol. Add it in the setup: `.AddApplication("Api", app => app.AddRest(rest => rest.AddClient("Api")))`.

> **No HTTP client 'Api' is registered. Register it under the application, back the application with AddAspNetCoreServer, or set 'ProtoTest:Applications:Api:BaseUrl'.**

The client has nowhere to send requests. Either host the application in-process with `AddAspNetCoreServer<Program>()`, or point it at a running one with `ProtoTest:Applications:Api:BaseUrl`. See [Configuration](./configuration.md).

## The application does not start in-process

- **`Program` is inaccessible.** A minimal-API application's entry point is internal. Add `public partial class Program;` to the application, as in [Your first test](./first-test.md#1-create-the-project).
- **The application reads configuration the test run does not have.** The in-process server runs the application's own `Program`, with its own `appsettings.json`. Values that come from the environment in production — connection strings, secrets — have to come from the test run: from [infrastructure](../foundation/infrastructure.md) that fills them, or from `configureWebHost` on `AddAspNetCoreServer`.

## Containers do not start

Infrastructure such as `PostgresDatabase.Container()` runs on Docker through Testcontainers. When Docker is not running or not reachable, the run fails before the first test, with Testcontainers' own message about the Docker endpoint.

- Start Docker Desktop, or on Linux make sure the current user can reach the Docker socket.
- On CI, use a runner image with Docker available.
- To run the suite where Docker is not available, give it a connection string through configuration instead of a container. A test-level skip cannot get in front of it: `AddInfrastructure` starts the container with the host, before any skip condition is evaluated, so a missing runtime fails the run at start. Start the container in the suite fixture instead, before registering anything: `PostgresDatabase.TryStart(...)` and `RabbitMqBroker.TryStart(...)` report the failure instead of throwing, so the fixture can fall back, replace the connection string, or skip the suite. When the fixture starts the container itself, register it with `AddResource` so the host still releases it — `AddInfrastructure` is for containers the host starts. `TryStart` blocks the calling thread while the container starts and has no timeout. See [Skip conditions](../foundation/skip-conditions.md).

## The browser does not launch

Playwright reports that the browser executable does not exist when it was never installed on the machine. Set `InstallBrowsers = true` to download it before the first launch, or use `Channel = "msedge"` or `"chrome"` to drive a browser that is already installed. On a clean Linux image, the operating-system libraries still come from `playwright.ps1 install --with-deps chromium`. See [Web](../integrations/web/index.md#browsers).

## Tests pass alone and fail together

Parallel tests share the application and its data. When two tests create the same customer, order number or email address, one of them fails — but only when they happen to run at the same time.

Make every value a test creates unique to that test, for example with `context.TestId`:

```csharp
new { email = $"customer-{context.TestId}@example.test" }
```

Tests that genuinely cannot run side by side need the runner's own tool: `[NonParallelizable]` on NUnit, a collection on xUnit.

## Where is the trace?

Without `ConfigureTracing`, the trace is written to `TestResults/prototest-{runId}.prototrace`. Relative paths — that one and your own — resolve against the directory the tests run in, which for `dotnet test` is the test project's output folder: `bin/Debug/net10.0/TestResults/`. Set an absolute path, or one built from an environment variable, to collect it from CI.

If [trace.prototest.dev](https://trace.prototest.dev) says the trace is **from an older ProtoTest**, the file was written before trace snapshot format 1.9. Run the tests again with a current ProtoTest. (The archive itself is manifest format 2.0: `spans.json` plus `state.json`, whose state documents are format 1.1.)

## Still stuck?

Open the trace: the test's story shows every hook, request and check in order, and the failure leads with the check that decided it. If that does not explain it, [open an issue](https://github.com/MSeys/ProtoTest/issues) with the trace attached.
