# ProtoTest.Demo

One parallel end-to-end demo for the Northstar multi-tenant control-plane SaaS. It exercises the real ASP.NET Core application through REST and GraphQL and combines tenant provisioning, role-based authentication, workspaces, releases, commerce, billing, audit, OpenAPI and GraphQL coverage.

The testing layer also demonstrates custom ProtoTest extension points: provisioning attributes, REST and GraphQL authenticators, a test hook, a named custom client initializer, contexts, observations, trace operations, and bundled JSON attachments.

```powershell
dotnet test samples/ProtoTest.Demo
```

The assembly runs testcases and fixtures concurrently with eight NUnit workers. Its HTML/JSON reports and `control-plane.prototrace` are written under `TestResults/ProtoTest.Demo`.

The normal run contains deliberately failed child operations whose exceptions are inspected by the test, so failure diagnostics remain visible without making CI red. Its top-level failure showcase is skipped unless `PROTOTEST_DEMO_INCLUDE_FAILURE=1` is set.

Regenerate the viewer's bundled trace with all successful scenarios and that intentional failure in one parallel run:

```powershell
./eng/update-viewer-demo.ps1
```
