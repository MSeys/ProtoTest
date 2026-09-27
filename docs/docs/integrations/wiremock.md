---
sidebar_position: 8
title: WireMock fakes
sidebar_label: WireMock
description: "Stub an HTTP dependency per test with WireMock.Net: scenario-like stubs, REST-shaped trace evidence and stub coverage."
---

# WireMock fakes

`ProtoTest.WireMock` starts a fake HTTP service per test, backed by [WireMock.Net](https://github.com/WireMock-Net/WireMock.Net). Stub the dependency your system under test calls, point it at the fake, and read what arrived: matched requests land in the trace with the [REST](./rest/index.md) response shape, and registered stubs contribute coverage.

```bash
dotnet add package ProtoTest.WireMock
```

ProtoTest targets **.NET 8, 9 and 10**.

## Registering

```csharp
builder.AddWireMock("Payments");                              // per-test fake, dynamic port
builder.AddWireMock("Ledger", fake => fake.PerRun());         // one server for the run
builder.AddWireMock("Legacy", fake => fake.Port(8089));       // fixed port
```

A repeated name with equal settings composes; a repeated name with different settings throws naming the fake. Registration declares a `protocol` capability named `WireMock` with the fake as its instance, so `[RequiresCapability(ProtoCapabilityKinds.Protocol, CapabilityName = "WireMock")]` gates on it.

## Stubbing

```csharp
var fake = Proto.Context.WireMock("Payments");

fake.Stub("GET", "/balance/*").RespondJson(HttpStatusCode.OK, new { available = 1200 });
fake.Stub(HttpMethod.Post, "/charges").RespondWith(HttpStatusCode.Accepted);
fake.Stub("GET", "/ping").RespondWith(HttpStatusCode.OK, "pong", "text/plain");
```

Paths follow the WireMock path syntax, where `*` matches a segment. A stub serves nothing until a response is set on it; calling a response method again replaces the mapping, so re-stubbing never stacks two mappings. `WithHeader(name, values)` adds response headers to the stub. The stub handle answers `ReceivedCount`, `StatusCode` and `VerifyHappened()` / `VerifyHappenedOnce()` / `VerifyHappened(times)`, which fail naming the stub.

For matchers the facade does not cover, `fake.Given(matcher)` and `fake.Server` expose the WireMock server directly. Requests those mappings serve are still observed, with the concrete request path as the identifier.

## Reaching it from a test

`fake.BaseUrl` is the URL to point the system under test at; `fake.Port` is the bound port. `fake.ReceivedRequests` lists every request the fake served (method, path, matched, status), `fake.UnmatchedRequests` lists what no stub matched, and `fake.VerifyNoUnmatchedRequests()` fails naming the fake and every unmatched request — call it at the end of a test that must only hit stubbed routes. `fake.Reset()` clears the stubs and the log without waiting for teardown.

Who owns the server is explicit: a per-test fake starts on the first `WireMock(name)` call of the test and stops with the test's resources; a per-run fake starts on first use, keeps its stubs and request log for the whole run, and stops with the run's resources. A per-run fake is therefore state a serial suite can build up on purpose — call `Reset()` when a test must start from a clean fake. Assert `ReceivedCount` and `ReceivedRequests` before the test completes — after teardown the server is gone.

## Tracing and coverage

Each stub registration records a `wiremock.stub` observation; each request the fake served records `http.response` when a stub matched (method, route template, status, sanitized body and headers, duration — the same payload REST records) or `http.failure` when nothing matched. An unmatched request carries the marker `WireMockUnmatchedRequest` as the failure's exception type: no exception produced the observation, so the fake reports the reason in the REST failure shape's type slot. The fake is a `server` run entity (`server:WireMock:{name}`) with its URL, lifetime and state.

Coverage is automatic: every registered fake gets a `WireMock` collector, no `AddCollector` needed. Each stub is an item — uncovered until a matched request covers it — so a stub no test's system under test called stays visible as a gap.

## Skip

```csharp
[RequiresCapability(ProtoCapabilityKinds.Protocol, CapabilityName = "WireMock")]
```

## Limits

- HTTP only: the fake serves stubbed responses; it does not proxy, record traffic, or validate against an OpenAPI document.
- Response headers are recorded as WireMock logs them, which for stubbed responses is usually empty; the status, body and request URL are always recorded with the shared HTTP redaction applied.
- A per-run fake is shared: its stubs and request log live for the whole run, parallel tests share the log, and matched observations are attributed to the test whose teardown observed them. Call `Reset()` to clear the shared fake between tests.
- A fixed-port fake cannot be shared by parallel tests.
- The server stops with its owner; read `ReceivedCount` and `ReceivedRequests` before the test completes.

## Learn more

- [REST](./rest/index.md) — the observation shapes fakes reuse.
- [Coverage](../observability/coverage.md) — what covered and gap mean in a report.
- [Infrastructure](../foundation/infrastructure.md) — run-owned resources and their release.
