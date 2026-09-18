# ProtoTest.Demo

A parallel end-to-end suite for **Northstar**, a multi-tenant release/deployment control-plane SaaS, driven through the real ASP.NET Core application in `samples/ProtoTest.SampleApp`.

The suite is organised as journeys rather than API probes:

| Journey | What it follows |
| --- | --- |
| `OnboardingJourney` | A new organization lands on Free, hits the seat and project limits, and upgrades. |
| `DeliveryJourney` | A release goes to preview, is promoted to production, fails a build, and is rolled back. |
| `BillingJourney` | Deploy minutes are metered, invoiced at period close, paid or declined, and a mid-cycle upgrade is prorated. |
| `AccessJourney` | The role matrix, API token scopes, tenant isolation and rate limiting. |
| `PlatformJourney` | REST, GraphQL (including a live subscription) and signed, retrying webhooks describing the same platform. |
| `DiagnosticsShowcase` | ProtoTest's own failure diagnostics, attachments and trace. |

The application is in memory but behaves like a product: plans and entitlements, billing state machines, a per-tenant virtual clock (`POST /test-support/tenants/{tenant}/clock/advance`) so periods can be closed deterministically, a webhook outbox with HMAC-SHA256 signatures and retries, an audit trail, `Idempotency-Key` replay and per-token rate limits.

```powershell
dotnet test samples/ProtoTest.Demo
```

The assembly runs testcases and fixtures concurrently with eight NUnit workers. Its HTML/JSON reports, the OpenAPI and GraphQL coverage and `prototest-demo.prototrace` are written under `TestResults/ProtoTest.Demo`.

`DiagnosticsShowcase.TheOrganizationReportsItsPlanAndProjectCount` is skipped unless `PROTOTEST_DEMO_INCLUDE_FAILURE=1` is set, so CI stays green while the run still contains deliberately failed child operations that the tests inspect.

Regenerate the viewer's bundled trace with all successful scenarios and that intentional failure in one parallel run:

```powershell
./eng/update-viewer-demo.ps1
```
