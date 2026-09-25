# ProtoTest.Hosting

Runs a background worker or generic host in-process with the suite: it starts with the run, reads the
run's settings and stops with the run.

```bash
dotnet add package ProtoTest.Hosting
```

## Includes

- Registration through `AddWorkerHost<TProgram>()`; the suite runs the worker's own entry point.
- Run-scoped lifetime: started after the infrastructure registered before it, stopped with the run.
- The suite's configuration, the run's settings - container connection strings and settings
  infrastructure values - and `AddWorkerHost` options reach the worker's normal configuration, in that
  order of precedence.
- Access from a test through `Proto.Context.Host<TProgram>()` and
  `HostService<TProgram, TService>()`.

## Limits

- In-process only: a suite running against a published environment has no worker to start. Skip those
  tests with `[RequiresCapability(ProtoCapabilityKinds.Worker)]`.
- One worker instance per run; the run's state is shared between tests.
- The worker's `Run()` never runs; the suite starts and stops its host.
- Start does not wait for readiness; register a readiness probe before the worker when it depends on a
  container that is still booting.

## Learn more

- [Background workers](https://prototest.dev/docs/integrations/hosting)
- [Infrastructure](https://prototest.dev/docs/foundation/infrastructure)
