# ProtoTest.SampleApp

**Northstar** — a multi-tenant release/deployment control-plane SaaS used by the ProtoTest demos.
It is a real runnable ASP.NET Core application rather than a mock server.

```bash
dotnet run --project samples/ProtoTest.SampleApp
```

## Product surface (`/api/v1`)

Bearer-token authenticated; the token identifies the tenant, the member and its scopes.

- organization and subscription: plans, seats, cancel and resume
- members and API tokens
- projects, environments (preview/production), deployments and rollbacks
- usage metering and summaries
- invoices, payments and voids
- webhooks and their deliveries

Errors are `application/problem+json` with a stable `code`. `POST` requests honour `Idempotency-Key`, tokens are rate limited, and `POST /api/v1/usage` is the metering ingestion point.

## Scenario control (`/test-support`)

These routes exist only so tests can create an isolated tenant and control time; they would not ship.

- `POST /test-support/tenants` — provision an organization with an owner and token
- `POST /test-support/tenants/{slug}/members` — create a member with a specific role
- `POST /test-support/tenants/{slug}/clock/advance` — move the tenant's virtual clock
- `POST /test-support/webhook-sinks` — a configurable webhook receiver for tests

## Behaviour you can rely on

The sample build succeeds unless the commit SHA starts with `bad`. Deployments meter deploy minutes at a deterministic rate. Invoices are issued when a billing period closes, which the virtual clock can force.
