---
sidebar_position: 8
title: WireMock fakes
sidebar_label: WireMock
description: "Stub an HTTP dependency per test with WireMock.Net: scenario-like stubs, REST-shaped trace evidence and stub coverage."
---

# WireMock

`ProtoTest.WireMock` starts a fake HTTP service per test, backed by [WireMock.Net](https://github.com/WireMock-Net/WireMock.Net). Stub the dependency your system calls, point it at the fake, and verify the requests it received. Matched requests land in the trace with the [REST](./rest/index.md) response shape, and registered stubs contribute coverage.

## What it adds

- A fake HTTP server per test or per run, on a dynamic port or a fixed one.
- Path stubs with JSON or text responses, response headers, and the WireMock matchers through the raw server.
- Call verification: how many times a stub was hit and which requests matched nothing.
- Trace evidence in the REST response shape, and one coverage item per registered stub.

## Install

```bash
dotnet add package ProtoTest.WireMock
```

ProtoTest targets **.NET 8, 9 and 10**.

## Compose

```csharp
builder.AddWireMock("Payments");                              // per-test fake, dynamic port
builder.AddWireMock("Ledger", fake => fake.PerRun());         // one server for the run
builder.AddWireMock("Legacy", fake => fake.Port(8089));       // fixed port
```

A repeated name with equal settings composes. A repeated name with different settings throws and names the fake. Registration declares a `protocol` capability named `WireMock` with the fake as its instance, so `[RequiresCapability(ProtoCapabilityKinds.Protocol, CapabilityName = "WireMock")]` gates on it.

In a test, reach the fake by name:

```csharp
var fake = Proto.Context.WireMock("Payments");
```

`fake.BaseUrl` is the URL to point the system under test at, and `fake.Port` is the bound port. Ownership is explicit:

- A per-test fake starts on the first `WireMock(name)` call of the test and stops with the test's resources.
- A per-run fake starts on first use, keeps its stubs and request log for the whole run, and stops with the run's resources.

[Infrastructure](../foundation/infrastructure.md) explains run-owned resources and their release.

## The tasks

```csharp
[ProtoTest]
public async Task Balance_endpoint_returns_the_stubbed_amount()
{
    var fake = Proto.Context.WireMock("Payments");
    var stub = fake.Stub("GET", "/balance/*")
        .RespondJson(HttpStatusCode.OK, new { available = 1200 });

    using var http = new HttpClient { BaseAddress = new Uri(fake.BaseUrl) };
    using var response = await http.GetAsync("/balance/42");

    Assert.That((int)response.StatusCode, Is.EqualTo(200));
    stub.VerifyHappenedOnce();
}
```

- **Serve a stubbed response.** `fake.Stub("GET", "/balance/*").RespondJson(HttpStatusCode.OK, new { available = 1200 })` stubs the route. `fake.Stub(HttpMethod.Post, "/charges").RespondWith(HttpStatusCode.Accepted)` and `RespondWith(status, body, contentType)` cover the rest. Paths follow the WireMock path syntax, where `*` matches a segment. A stub serves nothing until a response is set on it, and a response method replaces the mapping, so re-stubbing never stacks two mappings. `WithHeader(name, values)` adds response headers.
- **Verify the call arrived.** The stub handle answers `ReceivedCount`, `StatusCode` and `VerifyHappened()` / `VerifyHappenedOnce()` / `VerifyHappened(times)`, which fail naming the stub. Assert before the test completes, because after teardown the server is gone.
- **Fail on a route nothing stubbed.** `fake.ReceivedRequests` lists every request the fake served (method, path, matched, status). `fake.UnmatchedRequests` lists what no stub matched. `VerifyNoUnmatchedRequests()` fails naming the fake and every unmatched request. Call it at the end of a test that must only hit stubbed routes.
- **Clean a shared fake between tests.** A per-run fake keeps its stubs and request log for the whole run, so parallel tests share the log. `fake.Reset()` clears both without waiting for teardown. Reset a per-run fake when a test must start from a clean one.
- **Reach beyond the facade.** `fake.Given(matcher)` and `fake.Server` expose the WireMock server for matchers the facade does not cover. Requests those mappings serve are still observed, with the concrete request path as the identifier.

## In the trace and coverage

Each stub registration records a `wiremock.stub` observation. Each request the fake served records `http.response` when a stub matched. It carries the same payload [REST](./rest/index.md) records: method, route template, status, sanitized body and headers, duration. An unmatched request records `http.failure` with exception type `WireMockUnmatchedRequest`. No exception was thrown, and the type field carries the reason. The fake is a `server` run entity (`server:WireMock:{name}`) with its URL, lifetime and state.

Coverage is automatic. Every registered fake gets a `WireMock` collector, with no `AddCollector` needed. Each stub is an item, uncovered until a matched request covers it. A stub that the system under test never called stays visible as a gap. [Coverage](../observability/coverage.md) explains what covered and gap mean in a report.

```text
stub                  status    hits
GET /balance/*        covered   1
POST /charges         gap       0  (registered, never called)
```

## Skip

```csharp
[RequiresCapability(ProtoCapabilityKinds.Protocol, CapabilityName = "WireMock")]
```

## Limits

- HTTP only. The fake serves stubbed responses. It does not proxy, record traffic, or validate against an OpenAPI document.
- Response headers are recorded as WireMock logs them, which for stubbed responses is usually empty. The status, body and request URL are always recorded, with the shared HTTP redaction applied.
- A per-run fake is shared. Its stubs and request log live for the whole run, and parallel tests share the log. Matched observations are attributed to the test whose teardown observed them. Call `Reset()` to clear the shared fake between tests.
- A fixed-port fake cannot be shared by parallel tests.
- The server stops with its owner. Read `ReceivedCount` and `ReceivedRequests` before the test completes.

## Learn more

- [REST](./rest/index.md) - the observation shapes fakes reuse.
- [Coverage](../observability/coverage.md) - what covered and gap mean in a report.
- [Infrastructure](../foundation/infrastructure.md) - run-owned resources and their release.
