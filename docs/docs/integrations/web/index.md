---
sidebar_position: 1
title: Overview
description: "A driver-independent browser-testing model — pages, components, flows and login — run by Playwright or Selenium underneath."
---

# Web

`ProtoTest.Web` is a browser-testing model — pages, components, elements, tables, flows and login — that doesn't depend on any particular browser driver. A backend package plugs a real driver in underneath:

- `ProtoTest.Web.Playwright` — launches and manages browsers for you.
- `ProtoTest.Web.Selenium` — drives any `IWebDriver` you create.

Your page objects and tests stay the same for both, though the backends differ underneath: Playwright resolves role locators against implicit HTML roles, Selenium maps the documented roles, and text matching follows each backend's native locator options, including case handling.

```bash
dotnet add package ProtoTest.Web.Playwright    # brings ProtoTest.Web with it
# or
dotnet add package ProtoTest.Web.Selenium
```

## Browsers

Playwright launches Chromium by default, which runs on Windows, Linux and macOS; set `Channel` (`msedge`, `chrome`) to use an installed system browser instead. `InstallBrowsers` downloads the selected browser through the Playwright driver before the first launch, so a clean machine or CI runner needs no separate step:

```csharp
.AddWeb(options =>
{
    options.Browser = PlaywrightBrowser.Chromium;   // the default
    options.InstallBrowsers = true;                 // download it when missing
})
```

On a clean Linux image the operating-system libraries still come from `playwright.ps1 install --with-deps chromium`. Selenium takes a driver factory you provide (Selenium Manager resolves drivers), so the browser itself must already be installed.

A machine may have no browser at all. Rather than hand-rolling `Assert.Ignore`, gate browser tests with the opt-in skip condition from the Playwright package:

```csharp
[RequiresPlaywrightBrowser]                       // the browser from configuration
[RequiresPlaywrightBrowser(channel: "msedge")]
public async Task ...() { ... }
```

`AddWeb(...)` registers a `browser` capability named `Playwright` (or `Selenium`), so `[RequiresCapability(ProtoCapabilityKinds.Browser, CapabilityName = "Playwright")]` proves the backend is composed; `[RequiresPlaywrightBrowser]` is the stronger gate that also proves the browser is installed. It probes without launching a browser and reports a reason naming Playwright; `InstallBrowsers = true` means never skip. Selenium has no browser probe — the driver comes from your factory — so its tests combine `[RequiresCapability(ProtoCapabilityKinds.Browser, CapabilityName = "Selenium")]` with the try/catch pattern. Both are documented under [skip conditions](../../foundation/skip-conditions.md#requiring-a-playwright-browser).

A session can target its own address instead of the application's: `ProtoTest:Web:Sessions:{name}:BaseUrl` wins over `ProtoTest:Applications:{app}:BaseUrl`. Infrastructure that starts an application with the run fills that key, so a browser journey needs no fixture code.

## A first browser test

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
    await login.OpenAsync("https://portal.example.test/login");

    await login.Form.Flow("Sign in")
        .Fill(form => form.Username, "matthias")
        .Fill(form => form.Password, "correct horse")
        .Click(form => form.Submit)
        .RunAsync();

    await login.Form.Status.ShouldHaveTextAsync("Signed in");
}
```

## Registering a session

Registration is the same `AddWeb(...)` call regardless of backend. The backend is selected by which ProtoTest web package you reference — `ProtoTest.Web.Playwright` or `ProtoTest.Web.Selenium` — so test code never names the backend.

### Playwright

```csharp
builder.AddWeb(options =>
{
    options.Browser = PlaywrightBrowser.Chromium;
    options.Headless = true;
});
```

```csharp
public static IProtoHostBuilder AddWeb(
    this IProtoHostBuilder builder,
    Action<PlaywrightWebOptions>? configure = null);
```

| Option | Default | |
| --- | --- | --- |
| `Browser` | `Chromium` | `Chromium`, `Firefox` or `Webkit` |
| `Headless` | `true` | |
| `SlowMo` | `null` | milliseconds between actions, for watching a run |
| `Channel` | `null` | e.g. `"msedge"` or `"chrome"` to use an installed browser |
| `InstallBrowsers` | `false` | download the selected browser before the first launch when it is missing |
| `Context` | `new()` | Playwright's `BrowserNewContextOptions` — viewport, locale, base URL, storage state… |
| `TraceRetention` | `OnWebFailure` | keep Playwright's own trace: `Off`, `OnWebFailure`, `Always` |
| `CorrelateTraceGroups` | `true` | group Playwright trace actions under ProtoTest operations |
| `ConsoleCapture` | `WarningsAndErrors` | `Off`, `Errors`, `WarningsAndErrors`, `All` |
| `CapturePageErrors` | `true` | uncaught page exceptions |
| `CaptureRequestFailures` | `true` | failed network requests |

Playwright needs its browser on the machine: set `InstallBrowsers` to download it before the first launch, run the standard `playwright.ps1 install` script from the Microsoft.Playwright package yourself, or set `Channel = "msedge"` or `"chrome"` to drive a browser that's already installed. See [Browsers](#browsers). When even an installed browser may be absent, `[RequiresPlaywrightBrowser]` skips the test before setup with a reason instead of failing it; see [skip conditions](../../foundation/skip-conditions.md#requiring-a-playwright-browser).

Within a test, sessions registered with identical launch options (`Browser`, `Headless`, `SlowMo`, `Channel`) share one browser process, each with its own isolated browser context. The browser is disposed when the test finishes, which contains browser-level failures and cleans up any native contexts the test opens directly.

### Selenium

```csharp
builder.AddWeb(
    () => new ChromeDriver(),
    options => options.ActionTimeout = TimeSpan.FromSeconds(10));
```

```csharp
public static IProtoHostBuilder AddWeb(
    this IProtoHostBuilder builder,
    Func<IWebDriver> createDriver,
    Action<SeleniumWebOptions>? configure = null);
```

| Option | Default | |
| --- | --- | --- |
| `ActionTimeout` | 5 s | how long an action retries until the element is actionable |
| `PollInterval` | 50 ms | |
| `WaitForStableBounds` | `true` | wait until the element stops moving before clicking |
| `CheckClickObstruction` | `true` | fail if something covers the element |
| `DiagnosticTraceRetention` | `OnWebFailure` | `Off`, `OnWebFailure`, `Always` |

The factory is called once per session that uses the browser; ProtoTest quits and disposes the driver afterwards.

Selenium has no framework-level browser probe — the driver is created by your factory — so it has no `[RequiresPlaywrightBrowser]` equivalent. Gate tests with `[RequiresCapability(ProtoCapabilityKinds.Browser, CapabilityName = "Selenium")]` and a try/catch around the first session, as shown under [skip conditions](../../foundation/skip-conditions.md#requiring-a-playwright-browser).

Unlike Playwright, Selenium has no isolated-context-within-a-browser primitive — a driver *is* a browser instance with its own profile. Reusing one across tests would leak cookies and storage, so each session deliberately gets its own driver instead of a pooled one.

### From configuration

Both backends also read their options from configuration, so CI can run headless on another browser without code changes. Values are applied in this order, later winning:

1. your `AddWeb(...)` callback,
2. `ProtoTest:Web:Playwright` or `ProtoTest:Web:Selenium` — every session of that backend,
3. `ProtoTest:Web:Sessions:{name}` — one named session.

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
        "Admin": { "Channel": "msedge" }
      }
    }
  }
}
```

Nested Playwright context options such as `Context:Locale` bind too. Options are bound once, the first time a session of that name opens a browser, and Selenium's timeouts are validated again after binding.

## Sessions

`Proto.Context.Web(name = "Default")` returns the test's `WebSession`. The browser is created **lazily** on the first operation, so a test that never touches the browser never starts one, and it's closed during teardown.

```csharp
public sealed class WebSession : IAsyncDisposable
{
    string Name { get; }
    string BackendName { get; }
    TPage Page<TPage>() where TPage : WebPage, new();
    ValueTask WaitUntilAsync(Func<CancellationToken, ValueTask<bool>> condition, TimeSpan? timeout = null, string? description = null, CancellationToken cancellationToken = default);
    ValueTask<TBackend> GetBackendAsync<TBackend>(CancellationToken cancellationToken = default) where TBackend : class, IWebBackend;
    TBackend GetBackend<TBackend>() where TBackend : class, IWebBackend;
}
```

### Several sessions in one test

Sessions are per-test — the builder registers only the backend, and a test names the sessions it needs. Each named session is an isolated browser context (Playwright) or driver (Selenium), created lazily on first use and closed at teardown. Pages are cached per session, so `Web("Admin").Page<T>()` returns the same object each time.

```csharp
builder.AddWeb();   // register the backend once
```

```csharp
var admin = Proto.Context.Web("Admin").Page<BackOfficePage>();
var customer = Proto.Context.Web("Customer").Page<StorefrontPage>();

await admin.Orders.RowNumber(42).ApproveAsync();

// The customer waits until the admin's change is reflected, then verifies it.
await customer.Orders.RowNumber(42).Status.ShouldHaveTextAsync("Approved");
```

The `Should*` methods on an element or page poll until they pass, so they double as cross-session waits. For any other condition — including one that spans sessions — use `WaitUntilAsync`; its description defaults to the predicate's source text:

```csharp
await customer.WaitUntilAsync(async ct =>
    await customer.Orders.RowNumber(42).Status.TextAsync(ct) == "Approved");

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

Origins are environment-specific, so they come from the [application](../../getting-started/configuration.md) the session targets. Each session selects one with `ProtoTest:Web:Sessions:{name}:Application` (or `[WebSession(..., Application = "…")]`), defaulting to its own name, and reads its `ProtoTest:Applications:{application}:BaseUrl` — the same address a REST/GraphQL client targeting that application uses. A relative `Open` (or `Page<T>().OpenAsync("/path")`) resolves against that base. If even the path differs per environment, `ProtoTest:Web:Sessions:{name}:Open` supplies the whole URL and takes precedence over the attribute.

```json
{
  "ProtoTest": {
    "Web": {
      "BaseUrl": "https://staging.app.test",
      "Sessions": { "Admin": { "Open": "https://staging.app.test/ops" } }
    }
  }
}
```

Configure one named session's other settings through `ProtoTest:Web:Sessions:{name}`.

### Dropping down to the driver

When you need something the model doesn't offer, get the native backend:

```csharp
var backend = await Proto.Context.Web().GetBackendAsync<PlaywrightWebBackend>();
await backend.Page.SetContentAsync(html);
```

`GetBackendAsync` creates the browser if needed; the synchronous `GetBackend` throws if it hasn't been created yet. Asking for the wrong backend type throws `WebBackendCapabilityException`.

## Page coverage

Coverage for a browser journey is measured in **pages**, not lines. `AddWeb` registers a `WebCoverageCollector` that reports one item per page path, covered only when a test **verified** something on it. Three observations feed it:

| Observation | Recorded when |
| --- | --- |
| `web.page.visited` | a navigation succeeds — the path only, using the final address after redirects |
| `web.page.verified` | a `Should*` assertion passes — the page the assertion was checked on |
| `web.page.available` | a page is known to exist, but was not visited yet |

A page that was visited but never asserted is reported **uncovered**: reaching a page is not the same as checking it, and the report keeps the two apart. The verification count on each item is how many assertions passed on that page. When the session has a `BaseUrl` (the application it targets), coverage is attributed to that application's origin only: a redirect to an identity provider or a payment gateway, and any assertion checked there, is another origin's page and is not recorded as this application's visited or verified coverage.

### The explicit inventory

List the pages a suite knows about under `ProtoTest:Web:Pages`; they appear as uncovered until a verification lands on them:

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

`Source` is absolute or relative to the test assembly's base directory; a missing folder or an empty value simply contributes no discovered pages, never an error. `Framework` is `auto` (the default) or one of `next`, `nuxt`, `remix`, `vue`, `react`; an unknown value falls back to `auto`.

In `auto`, ProtoTest reads the nearest `package.json`, walking at most three folders up from the source (so a parent repository's dependencies never decide how this folder is scanned) and, when that says nothing, the folder layout (`next.config.*`, `nuxt.config.*`, `app/routes`, `app/page.*`, `pages/`). The detected framework picks the discovery strategies:

- **Next.js / Nuxt file routes** — files under `pages/` or `src/pages/` with `.ts`, `.tsx`, `.js`, `.jsx` or `.vue`: subfolders become path segments, `index` becomes the folder's route, `[id]` becomes `{id}`, and catch-alls `[...slug]` and `[[...slug]]` become `{...}`. `_app`, `_document`, `_error`, `404`, `500` and `_middleware` are skipped, and Next's `pages/api/…` handlers are not pages. Test/spec files (`*.test.*`, `*.spec.*`) and TypeScript declarations (`*.d.ts`) are never routes. Nuxt 2's underscore-prefixed dynamics (`_id.vue`, `_.vue`) are not mapped — use `Framework: "vue"` with the Vue Router literals, or the explicit inventory, for those.
- **Next.js app router** — `page.*` files under `app/` or `src/app/`, mapped the same way; route groups `(group)` drop out of the path, and `layout`, `template`, `loading`, `error` and `not-found` files never produce a route.
- **Remix** — flat file names under `app/routes/`: dots become `/`, `_index` becomes the folder's route, leading `_` segments are pathless and drop out, `$id` becomes `{id}`, and a bare `$` splat becomes `{...}`.
- **Vue Router / React Router** — source files are walked (skipping `node_modules`, `dist`, `build`, `.next`, `coverage` and directories that are symlinks or junctions, capped at 10,000 files and 1 MB per file) for absolute route literals: `path: "..."`, `path: '...'`, `path = "..."` and JSX `<Route path="/…">`; `:id` and `:id?` become `{id}`, `*` becomes `{...}`, and a Vue regex or trailing splat (`:pathMatch(.*)*`, `:rest*`) becomes `{...}` while `:id(\d+)` becomes `{id}`. This is also the fallback when `auto` detects nothing file-based.

Only absolute literals are collected: relative child routes and aliased imports are not resolved. Discovered paths join `ProtoTest:Web:Pages` in the same inventory and start out uncovered.

### Dynamic page matching

A verification on a concrete path covers the inventory pattern it matches: `/users/42` marks `/users/{id}` covered and increments its count, so a detail page verified once is done, not one per id. `{name}` matches exactly one segment and `{...}` matches the rest. Query and fragment are dropped and percent-encoding is decoded per segment, so a visit to `/a%20b` and an inventory entry `/a b` are one page; an encoded slash (`%2F`) stays inside its segment. Literal inventory entries win over patterns. Among patterns the first match in inventory order wins, except that a catch-all (`{...}`) is only used when no `{name}` pattern matches. A concrete path that matches no pattern keeps its own item, exactly as before.

### ASP.NET Core inventory

When the application runs **in-process** (`AddAspNetCoreServer<Program>()`), starting it also inventories its page-like GET routes and records a `web.page.available` for each. The inventory is recorded once, by the first test that initializes the server, and coverage aggregates those observations for the whole run: later tests reuse the server without repeating them, so the coverage report keeps the pages for the whole run. A failed inventory is not recorded, so a later test retries it. The filter is deliberately conservative:

- Razor Page endpoints count.
- An MVC controller action counts only with HTML evidence: a `text/html` response (`[Produces("text/html")]` or `ProducesResponseType` metadata) or a view-result return type. A JSON controller action is not a page.
- An `[ApiController]` action that produces HTML is a page even under an API-shaped route; JSON API actions are not.
- Only endpoints that explicitly declare GET count; an endpoint with no method metadata is not inventoried.
- Routes under `/api`, `/graphql`, `/swagger`, `/health`, `/_…` and other API shapes are excluded unless they declare HTML.
- Parameterized (`/orders/{id}`) and catch-all (`{**path}`) templates are excluded — this inventory lists concrete page paths; dynamic patterns come from the frontend source folder instead.

Refine the result per application with globs (`*` any run of characters, `?` exactly one); `Include` and `Exclude` take an array or a single scalar value:

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

A published application never starts in-process, so its inventory comes from the explicit list, the frontend source folder or Vue discovery instead.

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

It evaluates Vue 3's `$router.getRoutes()` and Vue 2's `$router.options.routes`, records each path as `web.page.available`, and never fails the test when Vue or its router is absent. Vue 2 relative child paths resolve against their parent (`{ path: '/orders', children: [{ path: 'new' }] }` records `/orders/new`), a top-level relative path is not a page, and regex catch-alls and trailing splats (`:pathMatch(.*)*`, `:rest*`) map to the `{...}` pattern. Discovery runs once, but only after it actually read a route table: an evaluation that fails or a page without Vue is retried on a later navigation, and a failure is recorded on the trace.

### React and Next.js

React has no generic runtime route table to read, and ProtoTest deliberately does not guess at one. Next.js, Nuxt and Remix are inventoried from the frontend source folder, and Vue Router / React Router route literals are read from their route definitions — see [Frontend source folder](#frontend-source-folder). For everything the scanner cannot see (routes built at runtime, aliased imports, relative child paths), publish the route list instead: a small build step that emits the application's routes as a JSON array, loaded into `ProtoTest:Web:Pages`. The pages then show as uncovered until a test visits and verifies them, exactly like the explicit inventory.

## Next

- [Pages and components](./page-objects.md)
- [Locators](./locators.md)
- [Actions and assertions](./interactions.md)
- [Flows](./flows.md)
- [Logging in](./login.md)
- [Waits and middleware](./middleware.md)
- [Diagnostics and artifacts](./diagnostics.md)
