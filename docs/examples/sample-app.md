# Unified control-plane SaaS demo

`ProtoTest.Demo` is the single runnable showcase. It exercises a real ASP.NET Core multi-tenant control-plane SaaS through REST and GraphQL while OpenAPI and GraphQL collectors produce coverage reports and ProtoTrace records the complete parallel execution.

The supporting projects are layers of this one demo:

- `ProtoTest.SampleApp.Contracts` contains transport-neutral contracts;
- `ProtoTest.SampleApp` owns organizations, users, roles, workspaces, releases, orders, billing, audit, and GraphQL;
- `ProtoTest.SampleApp.Testing` owns reusable provisioning attributes, typed contexts, REST/GraphQL authenticators, a lifecycle hook, a named custom client initializer, observations, attachments, and cleanup;
- `ProtoTest.Demo` contains the cross-integration journeys.

## Scenario model

Tests declare only the scenario capabilities they need:

```csharp
[RestClient(SampleAppTargets.Api)]
[SampleEnvironment]
[Auth<SampleUserAuthenticator>]
public sealed class PlatformLifecycleTests
{
    [ProtoTest]
    [SampleUser(SampleRoles.TenantAdministrator)]
    public async Task AdministratorCanDeployRelease()
    {
        // Exercise the real application through its public API.
    }
}
```

`SampleEnvironmentAttribute` provisions an isolated organization and removes it during teardown. `SampleUserAttribute` provisions a role-specific user. Authentication reads those typed contexts. `SaasScenarioHook` adds correlation, observations, and a JSON summary artifact, while `ScenarioProbeInitializer` demonstrates a custom named test-scoped client.

## Parallel execution

The NUnit assembly uses `ParallelScope.All`, eight workers, and one fixture instance per testcase. Independent tenant journeys overlap safely and appear as overlapping spans in the ProtoTrace run timeline.

## Run it

```powershell
dotnet test samples/ProtoTest.Demo
```

Reports and `control-plane.prototrace` are written below `TestResults/ProtoTest.Demo` in the output directory.
