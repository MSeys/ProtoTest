# ProtoTest.Aspire

Runs an Aspire AppHost with the suite: it starts with the run, each declared resource becomes an
application target, and the run releases it.

```bash
dotnet add package ProtoTest.Aspire
```

The package targets `net8.0`, `net9.0` and `net10.0`; the Aspire release is pinned centrally (`Directory.Packages.props` for `Aspire.Hosting.Testing`, `global.json` for the AppHost SDK).

## Includes

- Registration through `AddAspireAppHost<TEntryPoint>()`; the suite runs the AppHost's own entry
  point through the Aspire testing host. The AppHost serves only when selected: the global
  `ProtoTest:Aspire:Enabled` or a resource's own `ProtoTest:Aspire:Resources:{resource}:Enabled`.
- Run-scoped lifetime: started after the infrastructure registered before it, stopped with the run.
- Each selected resource's endpoint published as `ProtoTest:Applications:{resource}:BaseUrl`, so the
  application's clients and `AddHttpReadiness` resolve that one address.
- Chain providers: `UseAspireResource<TEntryPoint>("resource")` on an application publishes the
  resource's endpoint under its derived `BaseUrl`, and on an infrastructure target publishes the
  resource's connection string under every declared key. The provider serves under the same selection
  keys; a configured provider earlier in the chain wins. The resource's key selects only its targets,
  so "AppHost infrastructure with an in-process application" and its reverse are expressible.
- The run's configuration and the settings earlier infrastructure published travel to the AppHost as
  command-line arguments (the options' `Set` values last), so the AppHost's own graph reads the
  addresses the suite resolved.
- `MapConnectionString(resource, key)` (on the options or the builder) fills a key no target declares.
- A run that configures every declared resource's key skips the AppHost even when selected and points
  at that environment instead; with only some configured, it starts and publishes only the selected
  missing keys, so a configured address is never masked.
- Per-test address lookup through `Proto.Context.AspireResource("api")`.
- The `aspire` capability and a run entity per AppHost, with the published endpoints as evidence.

## Limits

- `net8.0`/`net9.0`/`net10.0` package assets; the suite's AppHost leg is `net10.0`, because DCP
  launches `AddProject` resources with `dotnet run` and cannot choose a target framework for a
  multi-targeted project.
- Closed box: no per-test service substitution, no in-process assertions. White-box
  worker tests use `ProtoTest.Hosting`; topology runs use this package.
- One AppHost instance per run; the run's state is shared between tests.
- A chain's AppHost serves only when selected: every AppHost provider holds on
  `ProtoTest:Aspire:Enabled` (every resource) or a resource's own
  `ProtoTest:Aspire:Resources:{resource}:Enabled` (its targets only), so a suite that registers the
  AppHost without setting either key never starts it and resolves through its other providers. The
  AppHost publishes only the selected resources' keys, and a configured key is never masked.
- The AppHost receives the run's configuration and settings as arguments; bridging them into its
  projects (`AddConnectionString`, `WithEnvironment`) stays the AppHost project's code, because only
  it knows its graph.
- A connection-string resource has no endpoint: `MapConnectionString` replaces its endpoint publish,
  and a resource that exposes neither fails the AppHost start naming it.
- The AppHost needs Aspire's orchestration binaries (DCP, dashboard) at runtime; they restore with
  the AppHost SDK from NuGet. A suite that must run without them catches
  `ProtoAspireUnavailableException` at startup and skips with its message.
- Start does not wait for readiness; register `AddHttpReadiness` after the AppHost for the HTTP
  resources tests call.

## Learn more

- [Aspire topology runs](https://prototest.dev/docs/integrations/aspire)
- [Infrastructure](https://prototest.dev/docs/foundation/infrastructure)
