# ProtoTest.Hosting

Runs a background worker or generic host in-process with the suite: it starts with the run, reads the
run's settings and stops with the run.

```bash
dotnet add package ProtoTest.Hosting
```

## Includes

- Registration through `AddWorkerHost<TProgram>()`; the suite runs the worker's own entry point.
- Nested workers: `AddApplication(...).AddWorkerHost<TProgram>("Billing")` follows the application's provider chain: `UseEnvironment()` leaves the worker to the environment that runs the application, while `UseHost()` hosts it in this process and bridges the test clock.
- Run-scoped lifetime: started after the infrastructure registered before it, stopped with the run.
- The suite's configuration, the run's settings (container connection strings and settings
  infrastructure values) and `AddWorkerHost` options reach the worker's normal configuration, in that
  order of precedence, and are passed to the worker's `Main` as command-line arguments, so
  `Host.CreateApplicationBuilder(args)` reads final-precedence values before `Build()`.
- Access from a test through `Proto.Context.Host<TProgram>()` and
  `HostService<TProgram, TService>()`.

## Limits

- In-process only: a suite running against a published environment has no worker to start. Skip those
  tests with `[RequiresCapability(ProtoCapabilityKinds.Worker)]` - or nest the worker under its
  application, where `UseEnvironment()` decides from the application's winner and starts nothing.
- One worker instance per run; the run's state is shared between tests.
- Only a hosted worker (`UseHost()`) declares the `clock` capability and resolves
  `Proto.Context.Host<TProgram>()`; a worker the environment runs declares `worker` only.
- The worker's `Run()` never runs; the suite starts and stops its host.
- A parameterless `Main`, or one that builds its host without passing `args`, reads the overlay only
  when the host is built; options factories and hosted services still see it.
- Start does not wait for readiness; register a readiness probe before the worker when it depends on a
  container that is still booting.

## Learn more

- [Background workers](https://prototest.dev/docs/integrations/hosting)
- [Infrastructure](https://prototest.dev/docs/foundation/infrastructure)
