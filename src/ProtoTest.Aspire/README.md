# ProtoTest.Aspire

Runs an Aspire AppHost with the suite: it starts with the run, each declared resource becomes an
application target, and the run releases it.

```bash
dotnet add package ProtoTest.Aspire
```

The package targets `net8.0`, `net9.0` and `net10.0`; the Aspire release is pinned centrally (`Directory.Packages.props` for `Aspire.Hosting.Testing`, `global.json` for the AppHost SDK).

## Includes

- Registration through `AddAspireAppHost<TEntryPoint>()`; the suite runs the AppHost's own entry
  point through the Aspire testing host.
- Run-scoped lifetime: started after the infrastructure registered before it, stopped with the run.
- Each resource's endpoint published as `ProtoTest:Applications:{resource}:BaseUrl`, so the
  application's clients and `AddHttpReadiness` resolve that one address.
- A run that configures every declared resource's key skips the AppHost and points at that
  environment instead; with only some configured, it starts and publishes only the missing keys,
  so a configured address is never masked.
- Per-test address lookup through `Proto.Context.AspireResource("api")`.
- The `aspire` capability and a run entity per AppHost, with the published endpoints as evidence.

## Limits

- `net8.0`/`net9.0`/`net10.0` package assets; the suite's AppHost leg is `net10.0`, because DCP
  launches `AddProject` resources with `dotnet run` and cannot choose a target framework for a
  multi-targeted project.
- Closed box: no per-test service substitution, no in-process assertions. White-box
  worker tests use `ProtoTest.Hosting`; topology runs use this package.
- One AppHost instance per run; the run's state is shared between tests.
- The AppHost needs Aspire's orchestration binaries (DCP, dashboard) at runtime; they restore with
  the AppHost SDK from NuGet. A suite that must run without them catches
  `ProtoAspireUnavailableException` at startup and skips with its message.
- Start does not wait for readiness; register `AddHttpReadiness` after the AppHost for the HTTP
  resources tests call.

## Learn more

- [Aspire topology runs](https://prototest.dev/docs/integrations/aspire)
- [Infrastructure](https://prototest.dev/docs/foundation/infrastructure)
