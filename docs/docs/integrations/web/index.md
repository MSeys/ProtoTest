---
sidebar_position: 1
title: Overview
---

# Web

`ProtoTest.Web` is a browser-testing model — pages, components, elements, tables, flows and login — that doesn't depend on any particular browser driver. A backend package plugs a real driver in underneath:

- `ProtoTest.Web.Playwright` — launches and manages browsers for you.
- `ProtoTest.Web.Selenium` — drives any `IWebDriver` you create.

Your page objects and tests are identical for both.

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
| `Context` | `new()` | Playwright's `BrowserNewContextOptions` — viewport, locale, base URL, storage state… |
| `TraceRetention` | `OnWebFailure` | keep Playwright's own trace: `Off`, `OnWebFailure`, `Always` |
| `CorrelateTraceGroups` | `true` | group Playwright trace actions under ProtoTest operations |
| `ConsoleCapture` | `WarningsAndErrors` | `Off`, `Errors`, `WarningsAndErrors`, `All` |
| `CapturePageErrors` | `true` | uncaught page exceptions |
| `CaptureRequestFailures` | `true` | failed network requests |

Playwright needs its browsers installed once per machine — the standard `playwright.ps1 install` script from the Microsoft.Playwright package. Using `Channel = "msedge"` or `"chrome"` avoids that by driving a browser that's already installed.

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

The factory is called once per test that uses the browser; ProtoTest quits and disposes the driver afterwards.

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

await admin.Orders.Row(42).ApproveAsync();

// The customer waits until the admin's change is reflected, then verifies it.
await customer.Orders.Row(42).Status.ShouldHaveTextAsync("Approved");
```

The `Should*` methods on an element or page poll until they pass, so they double as cross-session waits. For any other condition — including one that spans sessions — use `WaitUntilAsync`; its description defaults to the predicate's source text:

```csharp
await customer.WaitUntilAsync(async ct =>
    await customer.Orders.Row(42).Status.TextAsync(ct) == "Approved");

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

## Next

- [Pages and components](./page-objects.md)
- [Locators](./locators.md)
- [Actions and assertions](./interactions.md)
- [Flows](./flows.md)
- [Logging in](./login.md)
- [Waits and middleware](./middleware.md)
- [Diagnostics and artifacts](./diagnostics.md)
