# Contributing to ProtoTest

Thanks for helping make integration tests easier to build and explain. Bug reports, focused improvements, new integrations and sharp documentation feedback are all welcome.

## Before writing code

- Search the [open issues](https://github.com/MSeys/ProtoTest/issues) for related work.
- For a substantial API, package or architecture change, open an issue first. A short design conversation can prevent a large patch from heading in the wrong direction.
- Keep a contribution focused. Unrelated cleanup is easier to review separately.

## Context lookups

ProtoTest has three scopes, and each has exactly one way to reach it:

- **Inside a test, on the test's flow**: use `Proto.Context`. This covers test bodies,
  test-author entries (`context.Data().For<T>()`, `row.ShouldMatchShape(...)`,
  `ProtoGrpcAssertions.For(exception).Should.HaveStatus(...)`) and the plumbing they call. Prefer
  passing the context down from an entry when the callee is easy to construct directly in a test; do
  not thread it through purely for style.
- **Run scope**: use the host, the reference a hook receives at registration, or
  `ProtoHost.CurrentHost`. There is no ambient run context: run hooks and report building run
  outside any test.
- **Off the test's flow**: telemetry callbacks and library threads (a message consumer's delivery
  callbacks, a span processor's worker) use `ProtoHost.FindTraceWriter(activity)` and correlate by
  W3C trace id. `Proto.Context` cannot answer there, because there is no flow-local test.

An object whose lifetime spans tests (a broker, a browser pool, a run-scoped resource) must not hold
a test context; it resolves per call, and only when it acts on the test's flow. Release callbacks get
their trace writer from `ProtoResourceReleaseContext`.

## Test conventions

A test's name states the behavior it pins. Two forms, by kind:

- **Pure unit fixtures** use `Subject_ShouldOutcome`, with `_WhenCondition` when the condition is the
  point (for example `RollbackFailure_ShouldStillDisposeTheTransactionAndConnection`). Name the subject
  as the reader knows it, not the type under test.
- **Integration journeys, samples and the `dotnet new` template** use a PascalCase sentence that reads
  as the behavior, matching the demo and the reference suite (`CreatingAnOrderReturnsIt`,
  `AChargingSessionBecomesAnInvoice`).

Group related assertions with NUnit 4's scoped block, `using (Assert.EnterMultipleScope()) { … }`, so a
failure reports every assertion in the scope. New tests, the samples and the template use the scoped
form; `Assert.Multiple` remains in older tests and is not churned for style.

Keep the phases visible with `// Arrange`, `// Act` and `// Assert` comments whenever the test is long
enough that the phases are not obvious from the code; a short test needs none. One behavior per test,
one act per test. Fixture data names its intent (`OverdueInvoice`, not `Invoice1`), and a helper used
by more than one suite lives in `ProtoTest.TestSupport`.

`using` directives go inside the file-scoped namespace, as every sample and test does. The exception is
an assembly-attribute file whose attributes must precede the namespace (the TUnit and xUnit v3
`Setup.cs` files); keep its usings above the namespace.

## Build and test

ProtoTest requires the .NET 8, 9 and 10 SDKs. Node.js 20 or newer is needed for the documentation and trace viewer.

```powershell
./eng/test.ps1
./eng/pack.ps1 -NoBuild -NoRestore
```

The first command restores and builds the solution and runs the test suites. The second validates the NuGet packages. Some browser and container-backed tests also require Chromium or a container runtime; the scripts report when an optional environment is unavailable.

For documentation changes:

```powershell
./eng/check-docs.ps1
cd docs
npm ci
npm run typecheck
npm run build
```

For trace viewer changes:

```powershell
cd viewer
npm ci
npm run build
```

## Trying the local packages

`eng/pack.ps1` writes the branch packages to `artifacts/packages`. A scratch project consumes them
through a `nuget.config` that points a local source at that folder, with package source mapping for
`ProtoTest.*` so only ProtoTest packages resolve locally. Install the template from its package file:
`dotnet new install artifacts/packages/ProtoTest.Templates.<version>.nupkg`. After a repack, delete
`~/.nuget/packages/prototest.*`; the global cache wins otherwise and the old bits run.

## Integration pages

Every page under `docs/docs/integrations` follows one shape, and `eng/check-docs.ps1` fails when a page that should carry it does not. The six sections, in order:

1. **What it adds.** The capability and what it can see.
2. **Install.** The `dotnet add package` lines and the supported targets.
3. **Compose.** The `Add...` registration, its options and the context accessor.
4. **The tasks.** Three to five things a suite does with the integration; each links the deeper page that explains it.
5. **In the trace and coverage.** The operations, observations and coverage items the integration records.
6. **Limits.** What it does not do, and where it loses.

`## Skip` and a link list after `Limits` are fine. The check covers the top-level pages in `docs/docs/integrations` and each protocol section's `index.md`; the deep task pages under a section are exempt, and the section index links them. `overview.md` is the map and is exempt; `wiremock.md` is the model page.

## Pull requests

- Add or update tests for behavior changes.
- Update the relevant package README and the docs site when public behavior changes.
- Keep public APIs deliberate and documented with XML comments.
- Explain the user problem, the chosen behavior and how you verified it in the pull request.
- Do not commit generated build output, local traces or credentials.

By contributing, you agree that your contribution is licensed under the repository's [MIT License](LICENSE).

## Community packages

First-party ProtoTest packages share one version and are released together. A package published outside this
repository versions independently and declares the ProtoTest it needs: depend on the lowest compatible
`ProtoTest.Core` (or integration) version and state it in the README. Do not take a dependency on an internal
API marked `internal`; if an extension point is missing, open an issue so it can be added deliberately.

## AI-assisted contributions

AI-assisted work is welcome and reviewed like any other contribution. Disclose it in the pull request
(which parts, with which tool), be ready to explain the design and verify the behavior yourself, and keep the
same evidence bar: tests for behavior changes, docs for public behavior, and no generated build output or
credentials. A reviewer may ask for a walkthrough of any part; the contributor stays accountable for it.

## Conduct

Be precise, patient and constructive. See the [Code of Conduct](CODE_OF_CONDUCT.md) for the community standard and reporting route.
