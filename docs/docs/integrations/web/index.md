---
sidebar_position: 1
title: Browser integration testing
sidebar_label: Overview
description: "A driver-independent browser-testing model — pages, components, flows and login — run by Playwright or Selenium underneath."
---

# Web

`ProtoTest.Web` is a browser-testing model — pages, components, elements, tables, flows and login — that doesn't depend on any particular browser driver. A backend package plugs a real driver in underneath:

- `ProtoTest.Web.Playwright` — launches and manages browsers for you.
- `ProtoTest.Web.Selenium` — drives any `IWebDriver` you create.

Your page objects and tests stay the same for both. The backends differ underneath — most visibly in [locator translation](./locators.md#how-each-backend-translates-a-locator) — but everything in this section is backend-neutral.

## Install

```bash
dotnet add package ProtoTest.Web.Playwright
# or
dotnet add package ProtoTest.Web.Selenium
```

Either backend brings `ProtoTest.Web` with it. The packages target `net8.0`, `net9.0` and `net10.0`; the project templates default to `net10.0`, so pass `-f net8.0` or `-f net9.0` when a suite targets an older baseline.

## Browsers

Playwright launches Chromium by default, which runs on Windows, Linux and macOS; set `Channel` (`"msedge"`, `"chrome"`) to use an installed system browser instead. `InstallBrowsers = true` downloads the selected browser through the Playwright driver before the first launch, so a clean machine or CI runner needs no separate step — it is ignored when `Channel` names a system browser:

```csharp
builder.AddWeb(options =>
{
    options.Browser = PlaywrightBrowser.Chromium;   // the default
    options.InstallBrowsers = true;                 // download it when missing
});
```

On Linux, the operating-system libraries a bundled browser needs come from Playwright's own tooling; ProtoTest only runs the driver's install command for the browser binary.

Selenium takes the driver factory you provide, so the browser itself must already be installed on the machine.

A machine may have no browser at all. Rather than hand-rolling `Assert.Ignore`, gate Playwright tests with the opt-in skip condition from the Playwright package:

```csharp
[RequiresPlaywrightBrowser]                       // the configured browser
[RequiresPlaywrightBrowser(PlaywrightBrowser.Firefox)]
[RequiresPlaywrightBrowser(channel: "msedge")]
[RequiresPlaywrightBrowser(Session = "Admin")]    // the Admin session's options
public async Task ...() { ... }
```

It probes the installed browser before the lifecycle starts, without launching one, and skips with a reason naming Playwright and the install options; `InstallBrowsers = true` never skips. Selenium has no equivalent probe, so its tests combine `[RequiresCapability(ProtoCapabilityKinds.Browser, CapabilityName = "Selenium")]` with a try/catch around the first session. Both patterns are documented under [skip conditions](../../foundation/skip-conditions.md#requiring-a-playwright-browser) and summarized under [Skip](#skip).

A session can target its own address instead of the application's: `ProtoTest:Web:Sessions:{name}:BaseUrl` wins over `ProtoTest:Applications:{application}:BaseUrl`. Infrastructure that started an application with the run fills that key, so a browser journey needs no fixture code.

## Registering

Registration is the same `AddWeb(...)` call regardless of backend; the reference you install selects the backend. On a host builder:

```csharp
public static IProtoHostBuilder AddWeb(
    this IProtoHostBuilder builder,
    Action<PlaywrightWebOptions>? configure = null);          // Playwright

public static IProtoHostBuilder AddWeb(
    this IProtoHostBuilder builder,
    Func<IWebDriver> createDriver,
    Action<SeleniumWebOptions>? configure = null);            // Selenium
```

On an application builder — the form the sample suite uses, so the application's REST and GraphQL clients can share one address:

```csharp
public static IProtoApplicationBuilder AddWeb(
    this IProtoApplicationBuilder application,
    Action<PlaywrightWebOptions>? configure = null);

public static IProtoApplicationBuilder AddWeb(
    this IProtoApplicationBuilder application,
    Func<IWebDriver> createDriver,
    Action<SeleniumWebOptions>? configure = null);
```

The backend-neutral building blocks sit in `ProtoTest.Web`, so a hand-written backend can compose them directly:

```csharp
IProtoHostBuilder AddWebBackend(this IProtoHostBuilder builder, IWebBackendFactory factory);

IProtoHostBuilder AddWebMiddleware<TMiddleware>(this IProtoHostBuilder builder)
    where TMiddleware : class, IWebOperationMiddleware;

IProtoHostBuilder AddWebWait<TCondition>(
    this IProtoHostBuilder builder,
    WebWaitTiming timing,
    TimeSpan? timeout = null,               // default 5 s
    TimeSpan? pollInterval = null,          // default 50 ms
    params WebOperationKind[] operations)   // default Navigate, Click, Fill
    where TCondition : class, IWebWaitCondition;
```

Sessions are not declared at registration: a test names the sessions it needs with `Proto.Context.Web(name)` ([below](#sessions)). `AddWebBackend` is first-wins (`TryAddSingleton`), and a host resolves exactly one `IWebBackendFactory` — zero or more than one throws `InvalidOperationException`. For Playwright, every `AddWeb(...)` call still runs its `configure` callback while only the first supplies the skip-probe defaults; the Selenium application overload guards the whole call so a repeat is a no-op.

## Options and keys

Both backends also read their options from configuration, so CI can run headless on another browser without code changes. Values are applied in this order, later winning:

1. your `AddWeb(...)` callback,
2. `ProtoTest:Web:Playwright` or `ProtoTest:Web:Selenium` — every session of that backend,
3. `ProtoTest:Web:Sessions:{name}` — one named session.

Started infrastructure settings are merged over static configuration before binding, so they win over the same key in `appsettings.json`. Selenium's `ActionTimeout`/`PollInterval` are validated after binding; a non-positive value throws `ArgumentOutOfRangeException`. `TimeSpan` values bind as `"hh:mm:ss(.fffffff)"` and enums bind by name.

### Playwright options

Key names below are relative to `ProtoTest:Web:Playwright` or `ProtoTest:Web:Sessions:{name}` (`src/ProtoTest.Web.Playwright/PlaywrightWebOptions.cs`):

| Key | Type | Default | |
| --- | --- | --- | --- |
| `Browser` | `PlaywrightBrowser` | `Chromium` | `Chromium`, `Firefox` or `Webkit` |
| `Headless` | bool | `true` | |
| `SlowMo` | float? (ms) | `null` | delay between actions, for watching a run |
| `Channel` | string? | `null` | e.g. `"msedge"` or `"chrome"` to use an installed browser |
| `InstallBrowsers` | bool | `false` | download the selected browser before the first launch when it is missing |
| `Context` | `BrowserNewContextOptions` | `new()` | nested keys bind, e.g. `Context:Locale`, `Context:ViewportSize:Width` |
| `TraceRetention` | `PlaywrightTraceRetention` | `OnWebFailure` | `Off`, `OnWebFailure`, `Always` |
| `CorrelateTraceGroups` | bool | `true` | group Playwright trace actions under ProtoTest operations |
| `ConsoleCapture` | `PlaywrightConsoleCapture` | `WarningsAndErrors` | `Off`, `Errors`, `WarningsAndErrors`, `All` |
| `CapturePageErrors` | bool | `true` | uncaught page exceptions |
| `CaptureRequestFailures` | bool | `true` | failed network requests |

### Selenium options

Key names below are relative to `ProtoTest:Web:Selenium` or `ProtoTest:Web:Sessions:{name}` (`src/ProtoTest.Web.Selenium/SeleniumWebOptions.cs`):

| Key | Type | Default | |
| --- | --- | --- | --- |
| `ActionTimeout` | `TimeSpan` | 5 s | how long an action retries until the element is actionable |
| `PollInterval` | `TimeSpan` | 50 ms | how often it re-checks |
| `WaitForStableBounds` | bool | `true` | wait until the element stops moving before clicking |
| `CheckClickObstruction` | bool | `true` | fail if something covers the element |
| `DiagnosticTraceRetention` | `SeleniumDiagnosticTraceRetention` | `OnWebFailure` | `Off`, `OnWebFailure`, `Always` |

### Session keys

The session section `ProtoTest:Web:Sessions:{name}` also carries the backend-neutral keys (`src/ProtoTest.Web/Sessions/WebSession.cs`, `Authentication/WebSessionAttribute.cs`):

| Key | Type | Default | Infrastructure settings? |
| --- | --- | --- | --- |
| `ProtoTest:Web:Sessions:{name}:Application` | string | the session name | no — static configuration only |
| `ProtoTest:Web:Sessions:{name}:BaseUrl` | absolute URI | `ProtoTest:Applications:{application}:BaseUrl`; may be absent | yes, wins over static configuration |
| `ProtoTest:Web:Sessions:{name}:Open` | absolute or relative URL | `[WebSession(..., Open = "…")]`; may be absent | yes, wins over static configuration |
| `ProtoTest:Web:Sessions:{name}:DiscoverRoutes` | bool | `false` | yes, wins over static configuration |

`Application` selects which `ProtoTest:Applications:{application}` section supplies the base URL and whether the session is counted for that application's coverage. It does not consult infrastructure settings.

### From configuration

```json
{
  "ProtoTest": {
    "Web": {
      "Playwright": {
        "Browser": "Firefox",
        "Headless": true,
        "TraceRetention": "Always",
        "Context": { "Locale": "nl-BE", "ViewportSize": { "Width": 1280, "Height": 720 } }
      },
      "Sessions": {
        "Admin": { "Application": "ControlPlane", "BaseUrl": "https://ops.example.test" }
      }
    }
  }
}
```

Nested Playwright context options such as `Context:Locale` bind too. Options are bound once, the first time a session of that name opens a browser.

## Sessions

`Proto.Context.Web(sessionName = null, application = null)` returns the test's `WebSession`. The browser is created **lazily** on the first operation, so a test that never touches the browser never starts one, and the session is completed and disposed at teardown. `application` defaults to the application selected for the test; `sessionName` defaults to the application's `Web` client name, then `"Default"`.

```csharp
public sealed class WebSession : IAsyncDisposable
{
    string Name { get; }
    string Application { get; }
    Uri? BaseUrl { get; }
    string BackendName { get; }
    TPage Page<TPage>() where TPage : WebPage, new();
    TBackend GetBackend<TBackend>() where TBackend : class, IWebBackend;
    ValueTask<TBackend> GetBackendAsync<TBackend>(CancellationToken cancellationToken = default)
        where TBackend : class, IWebBackend;
    ValueTask WaitUntilAsync(
        Func<CancellationToken, ValueTask<bool>> condition,
        TimeSpan? timeout = null,               // default 5 s
        string? description = null,             // defaults to the predicate's source text
        CancellationToken cancellationToken = default);
}
```

`WaitUntilAsync` polls until the condition holds or the timeout passes, throwing `WebAssertionException` with the expectation when it doesn't. Inside the predicate, "element not found yet" and "not actionable yet" (`WebElementResolutionException`, `WebActionabilityException`) are treated as "not yet"; every other exception fails the wait immediately.

### Several sessions in one test

Sessions are per-test — the builder registers only the backend, and a test names the sessions it needs. Each named session is an isolated browser context (Playwright) or driver (Selenium), created lazily on first use and closed at teardown. Pages are cached per session, so `Web("Admin").Page<T>()` returns the same object each time.

```csharp
builder.AddWeb();   // register the backend once
```

```csharp
var admin = Proto.Context.Web("Admin").Page<BackOfficePage>();
var customer = Proto.Context.Web("Customer").Page<StorefrontPage>();

await admin.Orders.RowMatching(By.HasText("42")).Approve.ClickAsync();

// The customer waits until the admin's change is reflected, then verifies it.
await customer.Orders.RowMatching(By.HasText("42")).Status.Should.HaveTextAsync("Approved");
```

The `Should*` methods on an element poll until they pass, so they double as cross-session waits. For any other condition — including one that spans sessions — use `WaitUntilAsync`; its description defaults to the predicate's source text:

```csharp
await customer.WaitUntilAsync(async ct =>
    await customer.Orders.RowMatching(By.HasText("42")).Status.TextAsync(ct) == "Approved");

await customer.WaitUntilAsync(
    async _ => await customer.Total.TextAsync() == "€0.00",
    timeout: TimeSpan.FromSeconds(10));
```

Sessions can also be declared on the test, so setup creates (and optionally navigates) them before the body. `[WebSession]` defaults to `Order = -10`, so it runs before `[LoginAs]`:

```csharp
[WebSession("Admin", Open = "/back-office")]
[WebSession("Customer")]
[LoginAs<BackOfficeLogin>("billing.admin", Session = "Admin")]
public async Task ...() { ... }
```

`Application = "ControlPlane"` on the attribute names the application the session targets, defaulting to the session name. A relative `Open` resolves against the session's base URL, and the configuration key `ProtoTest:Web:Sessions:{name}:Open` supplies the whole URL when even the path differs per environment — configuration wins over the attribute.

### Dropping down to the driver

When you need something the model doesn't offer, get the native backend:

```csharp
var backend = await Proto.Context.Web().GetBackendAsync<PlaywrightWebBackend>();
await backend.Page.SetContentAsync(html);
```

`GetBackendAsync` creates the browser if needed; the synchronous `GetBackend` throws until the backend exists, and asking for the wrong type throws `WebBackendCapabilityException`.

## Quick start

A page object describes the page; a test drives it. The smallest working example — the demo's full journey is in [WebJourney.cs](../../../../samples/ProtoTest.Demo/WebJourney.cs):

```csharp
public sealed class LoginPage : WebPage
{
    public LoginForm Form => Component<LoginForm>(By.TestId("login"));
}

public sealed class LoginForm : WebComponent
{
    public WebElement Username => Element(By.Label("Username"));
    public WebElement Password => Element(By.Label("Password"));
    public WebElement Submit => Element(By.Role(WebRole.Button, "Sign in"));
    public WebElement Status => Element(By.Role(WebRole.Status));
}
```

```csharp
[ProtoTest]
public async Task Valid_credentials_sign_in()
{
    var login = Proto.Context.Web().Page<LoginPage>();
    await login.OpenAsync("/login");

    await login.Form.Flow("Sign in")
        .Fill(form => form.Username, "matthias")
        .Fill(form => form.Password, "correct horse")
        .Click(form => form.Submit)
        .RunAsync();

    await login.Form.Status.Should.HaveTextAsync("Signed in");
}
```

## Going further

- [Pages and components](./page-objects.md) — page objects, scoping, lazy collections and tables.
- [Locators](./locators.md) — roles, labels, text, `And`, and how each backend translates them.
- [Actions and assertions](./interactions.md) — `Should`/`ShouldNot`, polling, redaction.
- [Flows](./flows.md) — name a sequence of steps so the trace reads as one operation.
- [Logging in](./login.md) — an application-owned `IWebLoginStrategy` applied with `[LoginAs]`.
- [Waits and middleware](./middleware.md) — your application's notion of ready, applied once.
- [Diagnostics and artifacts](./diagnostics.md) — screenshots, console output, traces and the full trace reference.
- [ASP.NET Core](../aspnetcore.md) — when the application is hosted in-process, its page inventory comes with it.

## Tracing

Every operation is a ProtoTest trace entry; a passing assertion is also what makes a page count as covered:

| What | Recorded as |
| --- | --- |
| Navigate, click, fill, check, select, press, reads, count | `web.navigate`, `web.click`, `web.fill`, `web.check`, `web.select_option`, `web.press`, `web.read_text`, `web.read_value`, `web.is_visible`, `web.is_enabled`, `web.is_checked`, `web.count` |
| Assertions | `assert.web` with `web.expectation`, `web.assert.negated` and `web.assert.timeout` |
| Wait-until | `web.wait.until` |
| Flows | `web.flow` with `web.flow.step_count` |
| Login | `web.login` during setup |
| Session lifetime | `web.session.initialize` / `web.session.complete` |
| Backend call | child `web.backend.execute`, linked by `web.correlation_id` |
| Coverage | observations `web.page.visited`, `web.page.verified`, `web.page.available` |

The complete tables — common attributes, backend events, artifacts and the Selenium diagnostics schema — live on [Diagnostics and artifacts](./diagnostics.md#what-the-trace-records-for-every-operation).

## Page coverage

Coverage for a browser journey is measured in **pages**, not lines. `AddWeb` registers a `WebCoverageCollector` that reports one item per page path, covered only when a test **verified** something on it. Three observations feed it, all recorded under the `Web` target:

| Observation | Recorded when | `web.page.source` |
| --- | --- | --- |
| `web.page.visited` | a navigation succeeds — the final address after redirects when the backend can report one | `navigate` |
| `web.page.verified` | a `Should*` assertion passes on the page | `assert` |
| `web.page.available` | a page is known to exist but was not visited — from the inventory, a discovered frontend route or the ASP.NET Core server | `vue-router` / `aspnetcore` |

A page that was visited but never asserted is reported **uncovered**: reaching a page is not the same as checking it, and the report keeps the two apart. Only `web.page.verified` moves an item to covered (`Status = Success`) and increments its count; inventory-only pages stay neutral. When the session has a `BaseUrl`, coverage is attributed to that application's origin only: scheme, IDN host and port must all match, so a redirect to an identity provider or a payment gateway — and any assertion checked there — is not recorded as this application's coverage. A backend that cannot report its address contributes navigation coverage from the target address instead of failing.

### The explicit inventory

List the pages a suite knows about under `ProtoTest:Web:Pages`; they appear as uncovered until a verification lands on them. The value may be a single scalar path, an array, or an object whose child values are entries — `Source` and `Framework` are configuration, never page entries:

```json
{
  "ProtoTest": {
    "Web": {
      "Pages": [ "/", "/login", "/back-office/orders", "/settings" ]
    }
  }
}
```

### Frontend source folder

Instead of listing pages by hand, point ProtoTest at the frontend source folder and let it inventory the routes that exist there:

```json
{
  "ProtoTest": {
    "Web": {
      "Pages": {
        "Source": "frontend/src",
        "Framework": "auto"
      }
    }
  }
}
```

`Source` is absolute or relative to the test assembly's base directory (a relative path may not escape it); a missing, unreadable or empty folder contributes no pages and is never an error. `Framework` is `auto` (the default) or one of `next`, `nuxt`, `remix`, `vue`, `react`; an unknown value falls back to `auto`.

In `auto`, ProtoTest reads the nearest `package.json` — walking at most three folders up and stopping at the first one found, so a parent repository's dependencies never decide how this folder is scanned — and falls back to the folder layout (`next.config.*`, `nuxt.config.*`, `app/routes`, `app/page.*`, `pages/`). The detected framework picks the discovery strategy:

- **Next.js / Nuxt file routes** — files under `pages/` or `src/pages/` with `.ts`, `.tsx`, `.js`, `.jsx` or `.vue`: subfolders become path segments, `index` becomes the folder's route, `[id]` becomes `{id}`, and catch-alls `[...slug]` and `[[...slug]]` become `{...}`. `_app`, `_document`, `_error`, `404`, `500` and `_middleware` are skipped, and Next's `pages/api/…` handlers are not pages. Test/spec files (`*.test.*`, `*.spec.*`) and TypeScript declarations (`*.d.ts`) are never routes. Nuxt 2's underscore dynamics (`_id.vue`, `_.vue`) are not mapped — use the explicit inventory for those.
- **Next.js app router** — only `page.*` files under `app/` or `src/app/`, mapped the same way; route groups `(group)` drop out of the path, and `layout`, `template`, `loading`, `error` and `not-found` never produce a route.
- **Remix** — flat file names under `app/routes/`: dots become `/`, `_index` becomes the folder's route, leading `_` segments are pathless and drop out, `$id` becomes `{id}`, and a bare `$` splat becomes `{...}`.
- **Vue Router / React Router** — source files are walked (skipping `node_modules`, `dist`, `build`, `.next`, `coverage` and any symlinked or junctioned directory, capped at 10,000 files and 1 MB per file) for **absolute** route literals: `path: "..."`, `path: '...'`, `path = "..."` and JSX `<Route path="/…">`. Relative child routes and aliased imports are not resolved. This strategy is also the fallback when `auto` detects nothing file-based.

Discovered paths join `ProtoTest:Web:Pages` in the same inventory and start out uncovered. React has no runtime route table that ProtoTest reads; see [React and Next.js](#react-and-nextjs).

### Dynamic page matching

A verification on a concrete path covers the inventory pattern it matches: `/users/42` marks `/users/{id}` covered and increments its count, so a detail page verified once is done, not one per id. Page identity is the absolute HTTP/HTTPS path, with query and fragment dropped, a leading slash and no trailing slash except `/`. Percent-encoding is decoded per segment, so `/a%20b` and `/a b` are one page; an encoded slash (`%2F`) stays inside its segment, so one segment never becomes two.

Patterns come from route definitions: `:name`, `:name?`, `[...]` and `$name` become `{name}`; `*`, a bare `$`, `[...slug]`, `[[...slug]]`, `:rest*` and regex catch-alls such as `:pathMatch(.*)*` become `{...}`. `{name}` matches exactly one segment, `{...}` matches the rest and must be last, and matching is segment-wise and case-insensitive.

The collector resolves an observed path to its item in this order:

1. an existing item or an exact inventory entry with that path wins;
2. otherwise the first matching pattern **in inventory order**;
3. a catch-all (`{...}`) is used only when no non-catch-all pattern matched;
4. with no match, the concrete path keeps its own item.

### ASP.NET Core inventory

When the application runs **in-process** (`AddAspNetCoreServer<Program>()`), starting it also inventories its page-like GET routes and records a `web.page.available` for each. It is documented in full on [ASP.NET Core](../aspnetcore.md#page-coverage); the short version: only concrete, explicitly-GET, page-like endpoints count, API-shaped routes are excluded unless they declare HTML, and `ProtoTest:Applications:{app}:Web:Pages:Include` / `:Exclude` globs refine the result:

```json
{
  "ProtoTest": {
    "Applications": {
      "Api": {
        "Web": {
          "Pages": {
            "Include": [ "/portal/*" ],
            "Exclude": [ "/portal/legacy/*" ]
          }
        }
      }
    }
  }
}
```

The inventory is run-level: it is recorded once, by the first test that initializes the server, and a failed or empty discovery does not latch, so a later test still contributes it. A published application never starts in-process, so its inventory comes from the explicit list, the frontend source folder or Vue discovery instead.

### Vue discovery

For Vue 3 and Vue 2 applications, opt in per session and ProtoTest reads the router's route table in the page after the first navigation:

```json
{
  "ProtoTest": {
    "Web": {
      "Sessions": {
        "Default": { "DiscoverRoutes": true }
      }
    }
  }
}
```

It evaluates Vue 3's `[data-v-app].__vue_app__.config.globalProperties.$router.getRoutes()` first, then Vue 2's `#app.__vue__.$router.options.routes`, records each absolute path as `web.page.available`, and answers nothing when Vue or its router is absent. Vue 2 relative child paths resolve against their parent (`{ path: '/orders', children: [{ path: 'new' }] }` records `/orders/new`); a top-level relative path is not a page. Discovery runs once per session, only on backends that support JavaScript evaluation, and only after it actually read a route table: an evaluation that fails or a page without Vue stays unlatched and is retried on a later navigation, with the failure traced as `web.page.discovery.failed`.

The demo combines both: its Northstar Console is a real Vue 3 SPA, so page coverage comes from the console's source folder (`samples/ProtoTest.SampleApp/Ui`) plus Vue Router discovery from the running router.

### React and Next.js

React has no generic runtime route table to read, and ProtoTest deliberately does not guess at one. Next.js, Nuxt and Remix are inventoried from the frontend source folder, and Vue Router / React Router route literals are read from their definitions. For everything the scanner cannot see — routes built at runtime, aliased imports, relative child paths — publish the route list instead: a small build step that emits the application's routes as a JSON array loaded into `ProtoTest:Web:Pages`. The pages then show as uncovered until a test visits and verifies them, exactly like the explicit inventory.

## Skip

- `AddWeb(...)` registers a `browser` capability named `Playwright` or `Selenium`, so `[RequiresCapability(ProtoCapabilityKinds.Browser, CapabilityName = "…")]` proves the backend is composed. The required capability name must match the backend package.
- `[RequiresPlaywrightBrowser]` is the stronger gate: it probes the installed browser without launching one and skips with a reason. A recognized channel is accepted as-is, because only a real launch can resolve a system browser; `InstallBrowsers = true` always passes.
- Selenium ships no browser probe. Combine the capability gate with a try/catch around the first session that uses the browser, as shown in [skip conditions](../../foundation/skip-conditions.md#requiring-a-playwright-browser).
- The core web package registers no skip condition of its own: sessions are per-test, and the driver is only created on first use.

## Limits

- **One backend per host.** Resolving zero or more than one `IWebBackendFactory` throws `InvalidOperationException`, and `AddWebBackend` keeps the first registration.
- **`HasText` cannot stand alone** — it is a filter and must be composed with `And`. Selenium additionally accepts only `HasText` as the right-hand side and rejects a `By.Css` left side; Playwright accepts more combinations. See [Locators](./locators.md#combining-with-and).
- **`WaitUntilAsync`** only absorbs `WebElementResolutionException` and `WebActionabilityException`; any other exception fails it immediately.
- **Scanner:** a relative `Source` may not escape the test assembly's base directory; there is no Nuxt 2 underscore-dynamic support, only absolute route literals are collected, and there is no runtime React discovery.
- **Vue discovery** latches after the first non-null route table, so a router that later adds routes in the same session is not re-read.
- **Page origin:** an external redirect contributes no visited or verified coverage, and a backend that cannot report an address still passes the test.
- **Playwright:** the browser pool is scoped to one test — identical launch options share a process only inside that test. There is no retry layer beyond Playwright's own auto-waiting, `InstallBrowsers` does nothing when `Channel` is set, trace groups are serialized by a semaphore and skipped when `TraceRetention = Off`, console/page-error/request-failure text is truncated at 4096 characters, and the skip probe starts the Playwright driver.
- **Selenium:** one driver per session, no pooling, so sessions do not share cookies or storage; native failures surface as `WebActionabilityException` after `ActionTimeout`; `SelectOptionAsync` matches the `value` DOM property exactly and requires a single match.

## Next

- [Pages and components](./page-objects.md)
- [Locators](./locators.md)
- [Actions and assertions](./interactions.md)
- [Flows](./flows.md)
- [Logging in](./login.md)
- [Waits and middleware](./middleware.md)
- [Diagnostics and artifacts](./diagnostics.md)
