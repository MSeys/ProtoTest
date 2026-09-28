# ProtoTest.Templates

This package contains a small starter project that you can generate, run and experiment with.

For a larger (more complicated) example, see [Learning demo](https://github.com/MSeys/ProtoTest/tree/main/samples/Northstar.ProtoTest).

## Quick start

```bash
dotnet new install ProtoTest.Templates

dotnet new prototest -n Shop
cd Shop
dotnet test
```

## Choose the test runner

The starter is written for NUnit by default. Pass `--runner` for another runner:

```bash
dotnet new prototest -n Shop --runner xunit
```

The choices are `nunit` (the default), `xunit` (xUnit.net v2), `xunit3` (xUnit.net v3), `tunit` and
`mstest`. Each variant references that runner's ProtoTest adapter and test packages, and its
`Starter.Tests/Setup.cs` carries that runner's lifecycle wiring. The `xunit3` and `tunit` variants are
Microsoft Testing Platform projects: their generated `global.json` selects the MTP `dotnet test`
runner, so run the commands from the generated folder.

## Learn more

- [Your first test](https://prototest.dev/docs/getting-started/first-test)
- [Starter test file](https://github.com/MSeys/ProtoTest/blob/main/src/ProtoTest.Templates/templates/prototest-starter/Starter.Tests/OrderTests.cs)
- [ProtoTest documentation](https://prototest.dev/)
