# ProtoTest.SampleApp

A small multi-tenant SaaS application used by the cross-integration ProtoTest samples.
It is a real runnable ASP.NET Core application rather than a mock server.

The application provides environment and user provisioning, bearer authentication,
tenant isolation, role-based authorization, orders, and billing over REST and GraphQL.
Future gRPC and browser samples can add transports around the same domain.

```bash
dotnet run --project samples/ProtoTest.SampleApp
```

Endpoints under `/test-support` exist solely to provision and clean up isolated test scenarios.
The GraphQL endpoint is available at `/graphql` and is exercised by
`ProtoTest.SampleApp.GraphQLDemo`.
