# ProtoTest.WireMock

Fake HTTP services for ProtoTest suites, backed by WireMock.Net: stub a dependency per test, read the
matched requests in the trace with the REST response shape, and cover the stubs.

```bash
dotnet add package ProtoTest.WireMock
```

```csharp
builder.AddWireMock("Payments");

var fake = Proto.Context.WireMock("Payments");
fake.Stub("GET", "/balance/*").RespondJson(HttpStatusCode.OK, new { available = 1200 });

// point the system under test at fake.BaseUrl, then act and assert on it
fake.VerifyNoUnmatchedRequests();
```

## Includes

- `AddWireMock(name)` registration; `Proto.Context.WireMock(name)` starts the fake on first use.
- Per-test servers by default; `PerRun()` shares one server, its stubs and its request log across the
  run (cleared by `Reset()` or the run's release); `Port(n)` listens on a fixed port.
- Stubbing through `Stub(method, path)` with `RespondWith`/`RespondJson`, plus the raw
  `Given`/`Server` escape hatches for matchers the facade does not cover.
- Matched requests traced as `http.response` observations with the REST response payload;
  unmatched requests as `http.failure`, so they never count as covered.
- A `WireMock` coverage collector, registered automatically per fake: registered stubs are gaps
  until a matched request covers them.

## Limits

- HTTP only: the fake serves stubbed HTTP responses; it does not proxy, record, or validate
  against an OpenAPI document.
- Response headers are whatever WireMock logs for the entry, usually empty; the status, body and
  request URL are always recorded, with the shared HTTP redaction applied.
- A per-run fake is shared: its stubs and request log live for the whole run, parallel tests share
  the log, and observations are attributed to the test whose teardown observed them. Call `Reset()`
  to clear the shared fake between tests.
- A fixed-port fake cannot be shared by parallel tests.
- The server stops with its owner (the test, or the run); assert `ReceivedCount` and
  `ReceivedRequests` before the test completes.

## Learn more

- [WireMock fakes](https://prototest.dev/docs/integrations/wiremock)
- [REST](https://prototest.dev/docs/integrations/rest/) (the observation shapes fakes reuse)
- [Coverage](https://prototest.dev/docs/observability/coverage)
