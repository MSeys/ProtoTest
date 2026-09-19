# Northstar Console

A Vue 3 + TypeScript + Vite single-page console for the Northstar sample application. It talks to the
real `/api/v1` API and `/graphql` endpoint — there is no mock layer — and the .NET build never runs Node.

## Run it

```bash
# against a running samples/ProtoTest.SampleApp (http://localhost:60546)
npm install
npm run dev          # http://localhost:5180/console/, proxies /api, /graphql (ws), /test-support, /health
npm run build        # type-checks and writes dist/
```

The dev proxy target defaults to `http://localhost:60546`; set `VITE_API_TARGET` to move it. The console
is built with base `/console/` and is served by the sample app at `/console/` from `Ui/dist`.

## Sign-in and sessions

- **Browser flow**: open `/console/signin` and paste a tenant API token. It is exchanged for the same
  HttpOnly `northstar_token` cookie the server-rendered `/login` form sets (`POST /api/auth/login`).
  Every API and GraphQL call then carries the cookie. `POST /api/auth/logout` clears it.
- **API-created session**: append `?token=<owner token>` to any `/console/...` URL. The token is kept in
  `sessionStorage`, sent as `Authorization: Bearer ...`, and stripped from the address bar. This is how a
  journey uses a token created by `POST /test-support/tenants` without driving the form.
- A bearer session cannot authenticate a browser WebSocket, so live updates fall back to the visible
  polling loop (below).

## Serving and fallback

The sample app serves the build under `/console` from `Northstar:Ui:Path` (default `Ui/dist`, relative to
the content root). `/` redirects to `/console/`. Unknown `/console/*` paths fall back to `index.html`;
unknown paths elsewhere are 404, and `/api`, `/graphql`, `/test-support`, `/login` and `/projects` are
untouched. When the folder is missing, `/console` answers with the "UI not built - run npm install && npm
run build in Ui" page instead of failing.

## Screens and endpoints

| Screen | Route | Calls |
| --- | --- | --- |
| Login | `/console/signin` | `POST /api/auth/login`, `GET /api/v1/organization` |
| Dashboard | `/console/` | `GET /api/v1/organization`, `GET /api/v1/projects`, GraphQL `subscription`, `usageSummary`, `deployments(first: 5)` |
| Projects | `/console/projects` | `GET/POST /api/v1/projects` |
| Project | `/console/projects/:projectId` | `GET /api/v1/projects/{id}`, `GET/POST .../environments`, `GET/POST /api/v1/deployments`, `POST .../deployments/{id}/rollback`, GraphQL `deploymentStatusChanged` subscription |
| Billing | `/console/billing` | `GET /api/v1/subscription`, `GET /api/v1/invoices`, `POST /api/v1/invoices/{id}/pay` |
| Reports | `/console/reports` | `GET /api/v1/reports/monthly.xlsx`, `GET /api/v1/reports/monthly.html` |
| Not found | `/console/*` | none |

"Promote to production" is composed client-side: it reads the preview environment's current version and
its latest successful commit SHA, then deploys them to the production environment through the normal
deployment endpoint. There is no server-side promote route.

## Test hooks

Every interactive element and every rendered fact carries a stable `data-testid`. Repeated hooks (rows)
are scoped by their row: locate the row, then the element inside it. Attribute selectors are stable
contracts; classes and text are not.

| Screen | Element | Hook |
| --- | --- | --- |
| Shell | brand link, tenant, plan, sign out | `brand`, `session-organization`, `session-plan`, `sign-out` |
| Shell | nav links | `nav-dashboard`, `nav-projects`, `nav-billing`, `nav-reports` |
| Shell | toast, loading block | `toast` (+ `data-tone`), `loading` |
| Login | page, form, token, submit, error | `login-page`, `login-form`, `login-token`, `login-submit`, `login-error` |
| Dashboard | page, title, refresh, error | `dashboard-page`, `dashboard-title`, `dashboard-refresh`, `dashboard-error` |
| Dashboard | stat tiles | `stat-plan`, `stat-plan-status`, `stat-projects`, `stat-projects-limit`, `stat-deploy-minutes`, `stat-deploy-included`, `stat-period-end` |
| Dashboard | allowance (GraphQL) | `allowance`, `allowance-used`, `allowance-included`, `allowance-overage`, `source-graphql` |
| Dashboard | latest releases (GraphQL) | `recent-deployments`, `recent-deployment`, `recent-deployment-project`, `recent-deployment-status`, `recent-deployments-empty` |
| Projects | page, title, new, search | `projects-page`, `projects-title`, `new-project`, `project-search` |
| Projects | create form | `create-project-form`, `project-name-input`, `create-project`, `cancel-project`, `create-project-error` |
| Projects | table and rows | `projects-table`, `project-row` (`data-project-id`), `project-link`, `project-status`, `project-environments`, `project-created` |
| Projects | empty/no-match/error | `projects-empty`, `projects-empty-create`, `projects-no-match`, `projects-clear-search`, `projects-error`, `projects-retry` |
| Project | page, title, live, updated, refresh | `project-detail`, `project-detail-name`, `live-indicator` (+ `data-state`: `live`/`polling`/`connecting`), `last-updated`, `refresh-deployments` |
| Project | environments | `environments`, `environment` (`data-environment-id`, `data-environment-kind`), `environment-name`, `environment-kind`, `environment-status`, `environment-version`, `promote`, `environments-empty`, `environments-empty-create` |
| Project | create environment | `new-environment`, `create-environment-form`, `environment-name-input`, `environment-kind-input`, `create-environment`, `cancel-environment`, `environment-error` |
| Project | deploy form | `deploy-form`, `deploy-environment`, `deploy-version`, `deploy-commit`, `deploy-submit`, `deploy-error` |
| Project | deployment rail | `deployments`, `deployment-row` (`data-deployment-id`, `data-deployment-status`), `deployment-version`, `deployment-status`, `deployment-environment`, `deployment-sha`, `deployment-time`, `rollback`, `deployments-empty` |
| Billing | page, title, refresh, error | `billing-page`, `billing-title`, `billing-refresh`, `billing-error` |
| Billing | subscription | `subscription`, `subscription-status`, `subscription-plan`, `subscription-seats`, `subscription-deploy-minutes`, `subscription-renewal`, `subscription-canceling` |
| Billing | invoice row and pay | `invoices-table`, `invoice-row` (`data-invoice-number`, `data-invoice-status`), `invoice-number`, `invoice-status`, `invoice-issued`, `invoice-due`, `invoice-total`, `pay-method`, `pay`, `invoice-last-payment`, `invoices-empty` |
| Reports | page, actions, status | `reports-page`, `reports-title`, `report-download`, `report-html-link`, `report-status` |
| Reports | HTML report document | `report-html`, `report-org`, `report-generated`, `report-table`, `report-row`, `report-project-name`, `report-project-status`, `report-project-environments` |
| Not found | page, home link | `not-found`, `not-found-home` |

Roles and labels: inputs have real `<label for>` associations (use `By.Label`); buttons are native
`<button>` with visible names (use `By.Role`); nav links set `aria-current="page"`; the allowance meter
is `role="meter"` with `aria-valuenow`; errors are `role="alert"`; toasts are `role="status"`/`alert`.
The layout reserves space for errors and uses fixed-corner toasts, so controls do not move while a
request is in flight.

## Live updates

The project screen subscribes to the app's own GraphQL subscription over WebSocket
(`graphql-transport-ws`, `/graphql`, field `deploymentStatusChanged`). On any event for the project it
re-reads project, environments and deployments. If the socket cannot connect (or the session is a bearer
token), the screen shows `live-indicator[data-state="polling"]` and re-reads every 5 seconds; the
`refresh-deployments` button is always available.

## What journeys will need that the app does not expose yet

- **Promote** has no endpoint; the console composes it from `Deploy`. Journeys that want a server-side
  promote must do the same.
- **Invoice issuance** only happens at period close; use
  `POST /test-support/tenants/{slug}/clock/advance` before asserting on billing.
- **Environments** can be created but not renamed, paused or deleted; **projects** can be created and
  archived, not deleted.
- There is no **session endpoint** (`GET /api/v1/organization` is the session probe) and no token
  introspection; a 401 sends the console to `/console/signin`.
- The **GraphQL subscription carries only deployments and invoices**; the dashboard's "Latest releases"
  is a plain query, not live.
