# Contributing to ProtoTest

Thanks for helping make integration tests easier to build and explain. Bug reports, focused improvements, new integrations and sharp documentation feedback are all welcome.

## Before writing code

- Search the [open issues](https://github.com/MSeys/ProtoTest/issues) for related work.
- For a substantial API, package or architecture change, open an issue first. A short design conversation can prevent a large patch from heading in the wrong direction.
- Keep a contribution focused. Unrelated cleanup is easier to review separately.

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

## Pull requests

- Add or update tests for behavior changes.
- Update the relevant package README and the docs site when public behavior changes.
- Keep public APIs deliberate and documented with XML comments.
- Explain the user problem, the chosen behavior and how you verified it in the pull request.
- Do not commit generated build output, local traces or credentials.

By contributing, you agree that your contribution is licensed under the repository's [MIT License](LICENSE).

## Conduct

Be precise, patient and constructive. See the [Code of Conduct](CODE_OF_CONDUCT.md) for the community standard and reporting route.
