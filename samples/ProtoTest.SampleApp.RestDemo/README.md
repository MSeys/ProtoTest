# ProtoTest.SampleApp.RestDemo

The first end-to-end slice of the shared ProtoTest sample application. It runs the real
ASP.NET Core app in-process while exercising it exclusively through its REST surface.

The scenarios demonstrate:

- isolated environment provisioning and automatic cleanup;
- per-test users with member, billing administrator, and tenant administrator roles;
- context-driven bearer authentication and tenant headers;
- order creation, retrieval, validation, authorization, and tenant isolation;
- REST and OpenAPI coverage exported as JSON and interactive HTML.

```bash
dotnet test samples/ProtoTest.SampleApp.RestDemo
```

Reports are written under `TestResults/ProtoTest.SampleApp` in the test output directory.
