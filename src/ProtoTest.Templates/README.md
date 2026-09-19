# ProtoTest.Templates

`dotnet new` templates for ProtoTest: a starter solution that is already composed, so the first test runs in one command.

```bash
dotnet new install ProtoTest.Templates
```

## Quick start

```bash
dotnet new prototest -n Shop
cd Shop
dotnet test
```

The generated `Shop.slnx` contains two projects:

- **Shop.Api** — a small ASP.NET Core API for orders.
- **Shop.Tests** — an NUnit suite that hosts the API in-process, calls it over REST, asserts with shapes and writes a trace and an HTML report on every run.

## What it adds

- **The `prototest` template** — a working end-to-end suite: `ProtoTestAssembly` setup, an in-process `AddAspNetCoreServer<Program>`, REST and shape assertions, and an HTML report sink.
- **Ready composition** — the API and its tests share one host, so the first run already produces `TestResults/Starter.prototrace` and `TestResults/Starter.html`.
- **Framework choice** — `--framework net8.0`, `net9.0` or the default `net10.0`.
- **Restore control** — pass `--no-restore` to skip the post-create restore.
- **Version pinning** — the generated projects reference the ProtoTest version the template shipped with.
- **Structure** — `Shop.slnx`, `Shop.Api/Program.cs` with a small orders API, and `Shop.Tests/Setup.cs` deriving from `ProtoTestAssembly` with `Shop.Tests/OrderTests.cs`.
- **Coverage** — the REST client registers `RestCoverageCollector`, so the report lists the endpoints the tests exercised.
- **Help and updates** — `dotnet new prototest --help` lists the parameters; run `dotnet new install ProtoTest.Templates` again after a new release to update it.

The template requires the .NET SDK for its target framework; `-n Shop` names the solution and both projects through the `Starter` source name.

The suite is CI-friendly: `dotnet test` restores, builds and writes its artifacts under `TestResults`. Uninstall with `dotnet new uninstall ProtoTest.Templates`.

## Learn more

- [Your first test](https://prototest.dev/docs/getting-started/first-test)
- [Starter test file](https://github.com/MSeys/ProtoTest/blob/main/src/ProtoTest.Templates/templates/prototest-starter/Starter.Tests/OrderTests.cs)
