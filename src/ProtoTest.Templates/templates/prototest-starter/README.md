# Starter

A small ASP.NET Core API and a ProtoTest suite created by the `prototest` template.

```bash
dotnet test
```

The run writes a `.prototrace` file and an HTML coverage report under the test project's `TestResults` folder.

## Where things are

- `Starter.Api/Program.cs` contains the example API.
- `Starter.Tests/Setup.cs` hosts the API in-process and registers REST, tracing and reporting.
- `Starter.Tests/OrderTests.cs` contains the first scenarios.

Try changing an expected value and open the trace at [trace.prototest.dev](https://trace.prototest.dev/). The file is processed in the browser and is not uploaded.

## Conventions

- These are integration journeys, so each test is named as a PascalCase sentence (`CreatingAnOrderReturnsIt`). A pure unit fixture in the same project uses `Subject_ShouldOutcome` instead.
- `using` directives go inside the file-scoped namespace.
- Group related assertions with `using (Assert.EnterMultipleScope()) { /* assertions */ }`.

## Learn more

- [Your first test](https://prototest.dev/docs/getting-started/first-test)
- [Configuration](https://prototest.dev/docs/getting-started/configuration)
- [Integrations](https://prototest.dev/docs/integrations/overview)
