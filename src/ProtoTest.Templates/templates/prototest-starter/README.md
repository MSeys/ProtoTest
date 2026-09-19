# Starter

A small ASP.NET Core API and a [ProtoTest](https://prototest.dev) suite for it.

```bash
dotnet test
```

The run leaves two files in `Starter.Tests/bin/Debug/<framework>/TestResults/`:

- **Starter.prototrace** — every test, request and check. Drop it onto [trace.prototest.dev](https://trace.prototest.dev); it is read in your browser and never uploaded.
- **Starter.html** — the report, with the endpoints the suite called.

## Where things are

| File | What it does |
| --- | --- |
| `Starter.Api/Program.cs` | The API: create an order, read it back. |
| `Starter.Tests/Setup.cs` | Composes the suite once: the API in-process, a REST client, coverage, the trace and the report. |
| `Starter.Tests/OrderTests.cs` | The tests. `[Application("Api")]` selects the API; `Proto.Context.Rest()` calls it. |

## Next

- Make one test fail — change `quantity = 2` to `3` — and open the trace to see how a failure reads.
- Run the same suite against a deployed API by setting `ProtoTest:Applications:Api:BaseUrl`: [Configuration](https://prototest.dev/docs/getting-started/configuration).
- Add browser, GraphQL, SQL or messaging tests: [Integrations](https://prototest.dev/docs/integrations/overview).
