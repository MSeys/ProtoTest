# ProtoTest.Templates

`dotnet new` templates for [ProtoTest](https://prototest.dev), a composable integration-testing foundation for .NET.

```bash
dotnet new install ProtoTest.Templates
dotnet new prototest -n Shop
cd Shop
dotnet test
```

`prototest` creates a solution with two projects:

- **Shop.Api** — a small ASP.NET Core API for orders.
- **Shop.Tests** — an NUnit suite that hosts the API in-process, calls it over REST, asserts with shapes, and writes a trace and an HTML report on every run.

Pick the framework with `--framework net8.0`, `net9.0` or `net10.0` (the default).

Start with [Your first test](https://prototest.dev/docs/getting-started/first-test) to see how each part works.
