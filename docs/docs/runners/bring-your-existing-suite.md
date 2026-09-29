---
sidebar_position: 7
title: Bring an existing xUnit suite
description: "Move an existing xUnit v2 or v3 suite onto ProtoTest test by test: what keeps running, what converts, and the order that stays green."
---

# Bring an existing xUnit suite

ProtoTest takes over a test, not a project. An existing xUnit suite keeps running while you convert it one class at a time, and converted and plain tests can share a class.

## What stays

- Plain `[Fact]` and `[Theory]` tests keep running unchanged, whether their class is converted or not. They stay outside the trace.
- xUnit's discovery, ordering, parallelism and theory rows stay xUnit's. ProtoTest adds the per-test context and the trace around them.
- A converted test keeps its arrange/act/assert body. The composition moves into the host.

## What converts

| | xUnit v2 | xUnit v3 |
| --- | --- | --- |
| The host | `ProtoTestFixture : ProtoTestAssembly`, registered with `[CollectionDefinition]` | `Setup : ProtoTestAssembly`, registered with `[assembly: AssemblyFixture(typeof(Setup))]` |
| A test | `[ProtoTestFact]` / `[ProtoTestTheory]` replace `[Fact]` / `[Theory]` | the same, or keep `[Fact]` and add `[assembly: ProtoTestAutoWrap]` |
| A converted class | `[Collection(ProtoTestCollection.Name)]` is mandatory; without it the test-host crash aborts the run | nothing extra; the assembly fixture covers every class |
| The constructor | Already runs inside the context, so `Proto.Context` works there | Runs before the context; move context reads into `IAsyncLifetime.InitializeAsync` or the test body |
| Attachments | Files land under `%TEMP%\ProtoTest\attachments`, the path prints to the console with `--logger "console;verbosity=detailed"` | `TestContext.Current.AddAttachment(...)`, so artifacts appear with the test in xUnit's output |

The registration details, outcome mapping and limits are on [xUnit v2](./xunit.md) and [xUnit v3](./xunit3.md).

## The order that stays green

1. Add `ProtoTest.Xunit` (or `ProtoTest.Xunit3`) and whatever integration packages the tests use.
2. Write the host once: the fixture composes applications, clients and infrastructure.
3. Convert one class. Add the collection on v2, replace `[Fact]` with `[ProtoTestFact]`, and read `Proto.Context` in the body.
4. Run `dotnet test`. Converted tests get a context and a trace; the untouched ones behave exactly as before.
5. Delete the per-class harness code the converted tests no longer need.

On .NET SDK 10, an xUnit v3 project needs the Microsoft.Testing.Platform opt-in in `global.json` before `dotnet test` runs it; [xUnit v3](./xunit3.md) shows the file and the working commands.

## Limits

- A v2 class without the collection attribute that reaches `Proto.Context` crashes the test host and aborts the whole run, so convert the class in one change.
- The host is one per test process, started by the collection or assembly hook. A second registration does not layer a second host.
- Conversion does not fix shared state between tests. A class that writes to a fixed row, tenant or file still needs its own isolation; see [Concurrency](../foundation/concurrency.md).
