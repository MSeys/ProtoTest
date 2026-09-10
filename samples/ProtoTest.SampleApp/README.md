# ProtoTest.SampleApp

A small multi-tenant SaaS application used by the cross-integration ProtoTest samples.
It is a real runnable ASP.NET Core application rather than a mock server.

The first vertical slice provides environment and user provisioning, bearer authentication,
tenant isolation, role-based authorization, orders, and billing. Future GraphQL, gRPC,
browser, and spreadsheet samples can add transports around the same domain.

```bash
dotnet run --project samples/ProtoTest.SampleApp
```

Endpoints under `/test-support` exist solely to provision and clean up isolated test scenarios.
