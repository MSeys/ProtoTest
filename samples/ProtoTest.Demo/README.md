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
| `DomainAccessJourney` | The test composes the application's own domain over the shared store. |
| `SheetsJourney` | The generated monthly report is verified as an OpenXML workbook. |
| `GrpcJourney` | The application's own gRPC service is called and its replies shape-asserted. |
| `MessagingJourney` | The application's `invoice.paid` event is awaited over the broker, whether the invoice was paid over REST or on the console's billing screen. |
| `WebJourney` | The Northstar console end to end in one session: sign-in, dashboard, project and environment creation, a deployment's status, an invoice paid in billing, and the monthly report verified with Sheets. |
| `ApiThenBrowserJourney` | A project created through REST appears in the console after it refreshes. |
| `BrowserThenApiJourney` | A project created on the console's form is asserted back through REST and GraphQL. |
| `DiagnosticsShowcase` | ProtoTest's own failure diagnostics, attachments and trace. |

Some journeys skip, before their lifecycle starts, unless their infrastructure is present: the domain
journey needs a composed store, the messaging journey a broker, and the console journeys a standalone
instance (`ProtoTest:TargetUrl` or the session's base URL) with a built SPA under
`samples/ProtoTest.SampleApp/Ui/dist`.

The application is in memory but behaves like a product: plans and entitlements, billing state machines, a per-tenant virtual clock (`POST /test-support/tenants/{tenant}/clock/advance`) so periods can be closed deterministically, a webhook outbox with HMAC-SHA256 signatures and retries, an audit trail, `Idempotency-Key` replay and per-token rate limits.

```powershell
dotnet test samples/ProtoTest.Demo
```

The assembly runs testcases and fixtures concurrently with eight NUnit workers. Its HTML/JSON reports, the OpenAPI and GraphQL coverage and `prototest-demo.prototrace` are written under `TestResults/ProtoTest.Demo`.

`DiagnosticsShowcase.TheOrganizationReportsItsPlanAndProjectCount` is skipped unless `ProtoTest:Demo:IncludeFailure=true` (or `ProtoTest__Demo__IncludeFailure=true`) is set, so CI stays green while the run still contains deliberately failed child operations that the tests inspect.

The viewer's bundled trace is produced from a full demo run that includes that intentional failure.
