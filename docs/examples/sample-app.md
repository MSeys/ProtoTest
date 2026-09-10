# Shared SaaS sample application

`ProtoTest.SampleApp` is the common application under test for the growing integration
showcase. Unlike the focused WireMock REST demo, it is a real runnable multi-tenant
ASP.NET Core application with environments, users, roles, authentication, tenant
isolation, orders, and billing.

The sample is split by responsibility:

- `ProtoTest.SampleApp.Contracts` contains shared transport-neutral contracts;
- `ProtoTest.SampleApp` owns the application and domain behavior;
- `ProtoTest.SampleApp.Testing` owns reusable scenario attributes, typed contexts,
  authentication, provisioning, and cleanup;
- `ProtoTest.SampleApp.RestDemo` is the first integration-specific test suite.

## Scenario model

Tests declare the scenario they need:

```csharp
[RestClient(SampleAppTargets.Api)]
[SampleEnvironment]
[Auth<SampleUserAuthenticator>]
public sealed class BillingTests
{
    [ProtoTest]
    [SampleUser(SampleRoles.BillingAdministrator)]
    public async Task BillingAdministrator_CanSeeOpenInvoices()
    {
        using var response = await Proto.Context.Rest()
            .GetAsync("/api/billing/invoices", new { state = "open" });

        response.ShouldHaveStatus(HttpStatusCode.OK);
    }
}
```

`SampleEnvironmentAttribute` provisions an isolated tenant and removes it during
teardown. `SampleUserAttribute` provisions a user inside that tenant.
`SampleUserAuthenticator` reads both typed contexts and applies the bearer token and
tenant header. Their internal ordering is defined once in the shared testing project.

## Run it

```powershell
dotnet test samples/ProtoTest.SampleApp.RestDemo
```

The first suite covers successful and invalid orders, tenant isolation, billing roles,
administrative access, OpenAPI coverage, and HTML/JSON reporting. Future GraphQL,
gRPC, Playwright, and spreadsheet demos should remain separate test projects while
reusing this application and scenario infrastructure.
