# Northstar.ProtoTest

The application-specific test layer for Northstar. It holds what every journey against the Northstar
application would otherwise repeat: the scenario attributes and their contexts, the bearer
authenticator, the console page model, and the data provisioners that create fixtures through the
application's own code.

## What it adds

| Piece | What it does |
| --- | --- |
| `[NorthstarTenant(planId)]` | Provisions an isolated organization (Free by default), sets `NorthstarOrganizationContext`, and removes it after the test. |
| `[SignedInAs(role)]` | Acts as the owner or as a provisioned member; sets `NorthstarMemberContext` with the token. |
| `NorthstarAuthenticator` | Sends the signed-in member's bearer token for `[Auth<NorthstarAuthenticator>]`. |
| `NorthstarScenarioHook` | Stamps every test with a correlation id, a custom-client milestone trail, observations and a `scenario-summary.json` attachment. |
| `ConsolePage` and friends | The console screens as typed pages (`SignInPage`, `DashboardPage`, `ProjectsPage`, `ProjectDetailPage`, `BillingPage`, `ReportsPage`, `NotFoundPage`) and `NorthstarConsoleLogin` as the `[LoginAs<>]` strategy. `NorthstarConsole.Wait` and `NorthstarConsole.LiveUpdateWait` are the bounded waits. |
| `NorthstarDataDefaults` | The member email default. |
| Provisioners | See [Provisioning](#provisioning). |
| `NorthstarGraphQLWebSocketFactory` | Rides the in-process test server's WebSocket for GraphQL subscriptions. |
| `TestSupportProbe` | Fails once per run with a clear message when `/test-support` is absent. |

## Registration

```csharp
builder
    .AddNorthstarTestSupport(support => support.UseInProcessGraphQLWebSockets = hostedInProcess)
    .AddNorthstarData(data => data.UseDomainProvisioners = storeIsReachable);
```

`AddNorthstarTestSupport` registers the scenario client initializer and hook, and optionally the
in-process GraphQL WebSocket factory. `AddNorthstarData` registers the defaults, the test-support
provisioners and one route per fixture.

## Provisioning

Fixtures are requested through Data, never by calling the surface directly:

```csharp
var project = await Proto.Context.Data().CreateProjectAsync("atlas");
var invoice = await Proto.Context.Data().IssueInvoiceAsync();
var token = await Proto.Context.Data().CreateMemberTokenAsync(MemberRoles.Developer);
```

| Fixture | Domain route (`UseDomainProvisioners = true`) | Fallback route (`false`) |
| --- | --- | --- |
| Tenant | `NorthstarStore.ProvisionTenant` over the shared store | `POST /test-support/tenants` |
| Project / environment / deployment | the store's `CreateProject` / `CreateEnvironment` / `Deploy` | the public API |
| Invoice | record usage, advance the tenant clock, read the open invoice | `POST /api/v1/usage`, test-support clock, `GET /api/v1/invoices` |
| Member token (`[SignedInAs]`, `CreateMemberTokenAsync`) | `NorthstarStore.CreateMemberForRole` | `POST /test-support/tenants/{slug}/members` |
| Member (`InviteMemberRequest`) | public `POST /api/v1/members` in both modes — the portable example | same |
| Clock, webhook sink | `POST /test-support/...` in both modes — only the running application can do it | same |

The domain route resolves the application's own store when it runs in-process
(`ApplicationServices<Program>()`), so the application's events, webhook outbox and audit trail see
the write; otherwise it resolves the test-side composition (`AddSql` + `AddNorthstarDomain`) over the
same store. `[RequiresCapability(ProtoCapabilityKinds.Store)]` gates tests that only make sense with
that composition.

## Options

- `NorthstarTestSupportOptions.UseInProcessGraphQLWebSockets` — connect subscriptions through the
  in-process test server. Leave false against a published environment.
- `NorthstarDataOptions.UseDomainProvisioners` — create fixtures through the domain. Set it only when
  the store is composed and reachable; otherwise the API fallbacks run.

## Limits

- The domain route needs a reachable store; a published environment without one uses the fallback
  routes, and `UseDomainProvisioners` must be false there.
- The fallback tenant and member-token routes need the application's `/test-support` surface; the
  probe fails early with the flag to set when it is missing.
- Fixture cleanup is coarse: a tenant provisioner deletes the whole organization, so nothing else
  needs its own cleanup. Projects, environments and deployments have no delete route.
- `NorthstarGraphQLWebSocketFactory` only works in-process; it calls
  `ServerFactory<Program>(NorthstarTargets.Api)`, which requires a registered server.
